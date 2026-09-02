using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

namespace NOVAxis.Services.Download
{
    public enum DownloadKind
    {
        Video,
        Audio
    }

    public enum DownloadState
    {
        Pending,
        Running,
        Ready,
        Failed,
        Revoked,
        Expired
    }

    /// <summary>
    /// One prepared download. Lives only as long as the process does - see
    /// <see cref="DownloadStore"/> for why nothing here is written to a database.
    /// </summary>
    public sealed class DownloadRecord
    {
        private long _size;
        private long _received;
        private long _accounted;
        private int _state;

        public ulong Id { get; init; }
        public ulong OwnerId { get; init; }
        public DownloadKind Kind { get; init; }
        public string SourceUrl { get; init; }
        public string Title { get; init; }
        public string FormatId { get; init; }
        public string FormatLabel { get; init; }

        /// <summary>The download's own directory, which nothing else ever writes into.</summary>
        public string DirectoryPath { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }

        /// <summary>What was known about the size before the download started, if anything.</summary>
        public long? EstimatedSize { get; init; }

        /// <summary>
        /// Titles of the owner's older links retired to fit this one - at admission where
        /// the size was known, and again once the real one settled the budget. Almost
        /// always empty; never anybody else's.
        /// </summary>
        public IReadOnlyList<string> Freed { get; set; } = [];

        public string FilePath { get; set; }
        public string Error { get; set; }

        public DownloadState State
        {
            get => (DownloadState)Volatile.Read(ref _state);
            set => Volatile.Write(ref _state, (int)value);
        }

        /// <summary>The finished file's size, zero until it is.</summary>
        public long Size
        {
            get => Interlocked.Read(ref _size);
            set => Interlocked.Exchange(ref _size, value);
        }

        /// <summary>Bytes on disk right now - the watchdog's running total, and the progress readout.</summary>
        public long Received
        {
            get => Interlocked.Read(ref _received);
            set => Interlocked.Exchange(ref _received, value);
        }

        public bool IsFinished => State is DownloadState.Ready or DownloadState.Failed
            or DownloadState.Revoked or DownloadState.Expired;

        internal CancellationTokenSource Lifetime { get; set; }
        internal Task Worker { get; set; }

        /// <summary>
        /// Claims the bytes this record has charged against the global total, leaving zero
        /// behind, so a double teardown cannot credit them twice.
        /// </summary>
        internal long TakeAccounted() => Interlocked.Exchange(ref _accounted, 0);
        internal void Account(long bytes) => Interlocked.Add(ref _accounted, bytes);
    }

    /// <summary>
    /// Every live download, in memory. Deliberately not a database: a record is useful for
    /// an hour at most, the files do not survive a redeploy, and a row which outlived its
    /// file would be a link that only ever 404s. Records and files are therefore given the
    /// same lifetime - the process - and the output directory is emptied at startup.
    /// </summary>
    public class DownloadStore
    {
        /// <summary>How long a teardown waits for a running download to notice it was cancelled.</summary>
        private static readonly TimeSpan WorkerGrace = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<ulong, DownloadRecord> _byId = new();
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new();
        private readonly ConcurrentDictionary<ulong, Queue<DateTimeOffset>> _attempts = new();

        private long _totalBytes;

        private IOptions<DownloadOptions> Options { get; }
        private ILogger<DownloadStore> Logger { get; }

        public DownloadStore(IOptions<DownloadOptions> options, ILogger<DownloadStore> logger)
        {
            Options = options;
            Logger = logger;

            Root = Path.GetFullPath(options.Value.OutputFolder);
        }

        /// <summary>The absolute root every download directory must sit under.</summary>
        public string Root { get; }

        public long TotalBytes => Interlocked.Read(ref _totalBytes);

        public IReadOnlyList<DownloadRecord> All => _byId.Values.ToList();

        public DownloadRecord Find(ulong id)
        {
            return _byId.TryGetValue(id, out var record) ? record : null;
        }

        /// <summary>Everything one person is holding, newest first.</summary>
        public IReadOnlyList<DownloadRecord> FindByOwner(ulong ownerId)
        {
            return _byId.Values
                .Where(r => r.OwnerId == ownerId)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
        }

        /// <summary>
        /// What a person's downloads take up. One still running is charged whichever is
        /// larger of what it was expected to be and what it has actually written - never a
        /// blanket ceiling, because the budget is settled against the real figure the
        /// moment it lands, and charging a guess would retire links to cover it.
        /// </summary>
        public long BytesOf(ulong ownerId)
        {
            return _byId.Values
                .Where(r => r.OwnerId == ownerId)
                .Sum(r => r.IsFinished
                    ? r.Size
                    : System.Math.Max(r.Received, r.EstimatedSize ?? 0));
        }

        /// <summary>Whether this person already has one being fetched.</summary>
        public int RunningFor(ulong ownerId)
        {
            return _byId.Values.Count(r => r.OwnerId == ownerId && !r.IsFinished);
        }

        /// <summary>
        /// The lock every request from one user passes through. Freeing room and admitting
        /// what needed it is one indivisible step, or two clicks in quick succession would
        /// each decide against a total the other was about to change.
        /// </summary>
        public SemaphoreSlim Gate(ulong ownerId)
        {
            return _gates.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        }

        public void Add(DownloadRecord record)
        {
            _byId[record.Id] = record;
        }

        /// <summary>
        /// Records the finished file and charges its bytes against the global total.
        /// </summary>
        public void Publish(DownloadRecord record, string filePath, long size)
        {
            record.FilePath = filePath;
            record.Size = size;
            record.Received = size;
            record.Account(size);

            Interlocked.Add(ref _totalBytes, size);

            record.State = DownloadState.Ready;
        }

        /// <summary>
        /// Tears a download down: stops the work, waits a bounded moment for it to actually
        /// stop, and only then deletes the files. Deleting first would leave yt-dlp writing
        /// into a directory nobody owns any more, and some extractors recreate it.
        /// </summary>
        public async Task RemoveAsync(DownloadRecord record, DownloadState finalState)
        {
            if (record == null)
                return;

            record.State = finalState;

            var lifetime = record.Lifetime;

            if (lifetime != null)
            {
                try
                {
                    await lifetime.CancelAsync();
                }
                catch (ObjectDisposedException) { /* the worker got there first */ }
            }

            var worker = record.Worker;

            if (worker != null)
            {
                try
                {
                    await worker.WaitAsync(WorkerGrace);
                }
                catch (TimeoutException)
                {
                    Logger.Warning($"Download {record.Id} did not stop within " +
                                   $"{WorkerGrace.TotalSeconds:0}s, freeing it anyway");
                }
                catch (Exception) { /* the worker's own failure is its to report */ }
            }

            Forget(record);
        }

        /// <summary>
        /// Drops a record and its files without touching the work - for a worker cleaning
        /// up after itself, where cancelling and awaiting would mean awaiting itself.
        /// </summary>
        public void Forget(DownloadRecord record)
        {
            DeleteDirectory(record);

            Interlocked.Add(ref _totalBytes, -record.TakeAccounted());

            _byId.TryRemove(record.Id, out _);

            record.Lifetime?.Dispose();
            record.Lifetime = null;
        }

        /// <summary>
        /// Throws away what a download wrote while keeping the record, so the person who
        /// asked for it still gets told why it failed.
        /// </summary>
        public void DiscardFiles(DownloadRecord record)
        {
            DeleteDirectory(record);

            Interlocked.Add(ref _totalBytes, -record.TakeAccounted());

            record.FilePath = null;
            record.Size = 0;
        }

        private void DeleteDirectory(DownloadRecord record)
        {
            if (string.IsNullOrEmpty(record.DirectoryPath) || !Directory.Exists(record.DirectoryPath))
                return;

            try
            {
                Directory.Delete(record.DirectoryPath, recursive: true);
            }
            catch (IOException e)
            {
                // A file still being served holds no lock on Linux - the unlink succeeds and
                // the response finishes off the freed inode. Windows disagrees; there the
                // next sweep picks it up.
                Logger.Debug($"Could not delete the directory of download {record.Id} yet: {e.Message}");
            }
            catch (UnauthorizedAccessException e)
            {
                Logger.Warning($"Not allowed to delete the directory of download {record.Id}", e);
            }
        }

        /// <summary>
        /// Empties the output root. Records never outlive the process, so anything already
        /// there - half written .part files included - belongs to a run which is over.
        /// </summary>
        public void PurgeRoot()
        {
            try
            {
                Directory.CreateDirectory(Root);

                var root = new DirectoryInfo(Root);
                var removed = 0;

                foreach (var directory in root.EnumerateDirectories())
                {
                    directory.Delete(recursive: true);
                    removed++;
                }

                foreach (var file in root.EnumerateFiles())
                {
                    file.Delete();
                    removed++;
                }

                Interlocked.Exchange(ref _totalBytes, 0);

                if (removed > 0)
                    Logger.Info($"Cleared {removed} leftover download(s) from '{Root}'");
            }
            catch (Exception e)
            {
                Logger.Warning($"Could not clear the download folder '{Root}'", e);
            }
        }

        /// <summary>
        /// Takes one slot from the caller's rolling window, or reports how long until the
        /// oldest one frees up. The owner's gate serialises admission, but the sweeper and
        /// the read path reach these queues without it, so each one guards itself.
        /// </summary>
        public bool TryTakeSlot(ulong ownerId, int max, TimeSpan window, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;

            var now = DateTimeOffset.UtcNow;
            var attempts = _attempts.GetOrAdd(ownerId, _ => new Queue<DateTimeOffset>());

            lock (attempts)
            {
                Trim(attempts, now, window);

                if (attempts.Count >= max)
                {
                    retryAfter = attempts.Peek() + window - now;

                    if (retryAfter < TimeSpan.Zero)
                        retryAfter = TimeSpan.Zero;

                    return false;
                }

                attempts.Enqueue(now);
                return true;
            }
        }

        /// <summary>
        /// Hands a slot back, for a request which never reached yt-dlp. A download which ran
        /// and failed keeps its slot: extraction is the expensive part, and refunding it
        /// would let a reliably failing link be retried without limit.
        /// </summary>
        public void ReturnSlot(ulong ownerId)
        {
            if (!_attempts.TryGetValue(ownerId, out var attempts))
                return;

            lock (attempts)
            {
                if (attempts.Count == 0)
                    return;

                // The slot just taken is the newest, and a Queue only gives up its oldest
                var kept = attempts.ToArray();
                attempts.Clear();

                for (var i = 0; i < kept.Length - 1; i++)
                    attempts.Enqueue(kept[i]);
            }
        }

        public (int Used, DateTimeOffset? ResetsAt) SlotsUsed(ulong ownerId, TimeSpan window)
        {
            if (!_attempts.TryGetValue(ownerId, out var attempts))
                return (0, null);

            lock (attempts)
            {
                Trim(attempts, DateTimeOffset.UtcNow, window);

                return attempts.Count == 0
                    ? (0, null)
                    : (attempts.Count, attempts.Peek() + window);
            }
        }

        /// <summary>Drops quota history nobody is counting any more.</summary>
        public void TrimSlots(TimeSpan window)
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var (ownerId, attempts) in _attempts)
            {
                bool empty;

                lock (attempts)
                {
                    Trim(attempts, now, window);
                    empty = attempts.Count == 0;
                }

                if (empty)
                    _attempts.TryRemove(new KeyValuePair<ulong, Queue<DateTimeOffset>>(ownerId, attempts));
            }
        }

        private static void Trim(Queue<DateTimeOffset> attempts, DateTimeOffset now, TimeSpan window)
        {
            while (attempts.Count > 0 && now - attempts.Peek() >= window)
                attempts.Dequeue();
        }
    }
}
