using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// An in-memory queue guarded by a lock. Reads return a snapshot, so callers may enumerate
    /// the queue while the playback loop keeps dequeuing from it.
    /// </summary>
    public sealed class AudioTrackQueue : IAudioTrackQueue
    {
        private readonly List<AudioTrackQueueItem> _items = new();
        private readonly Lock _sync = new();

        /// <summary>
        /// Raised whenever items became available, so that an idle player can pick them up.
        /// </summary>
        public event Action Enqueued;

        public int Count
        {
            get { lock (_sync) return _items.Count; }
        }

        public AudioTrackQueueItem this[int index]
        {
            get { lock (_sync) return _items[index]; }
        }

        public bool Contains(AudioTrackQueueItem item)
        {
            lock (_sync) return _items.Contains(item);
        }

        public ValueTask AddAsync(AudioTrackQueueItem item, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync) _items.Add(item);

            Enqueued?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask AddRangeAsync(IEnumerable<AudioTrackQueueItem> items, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(items);
            cancellationToken.ThrowIfCancellationRequested();

            var added = items.Where(x => x != null).ToList();
            if (added.Count == 0) return ValueTask.CompletedTask;

            lock (_sync) _items.AddRange(added);

            Enqueued?.Invoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveAsync(AudioTrackQueueItem item, int toIndex, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                var index = _items.IndexOf(item);

                if (index < 0)
                    return ValueTask.FromResult(false);

                // Keep the stored entry - the argument may be a mere probe with the same id
                var found = _items[index];
                _items.RemoveAt(index);

                toIndex = Math.Clamp(toIndex, 0, _items.Count);
                _items.Insert(toIndex, found);

                return ValueTask.FromResult(true);
            }
        }

        public ValueTask<bool> RemoveAsync(AudioTrackQueueItem item, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync) return ValueTask.FromResult(_items.Remove(item));
        }

        public ValueTask RemoveAtAsync(int index, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (index >= 0 && index < _items.Count)
                    _items.RemoveAt(index);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveRangeAsync(int index, int count, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                index = Math.Max(index, 0);
                count = Math.Min(count, _items.Count - index);

                if (count > 0)
                    _items.RemoveRange(index, count);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync) _items.Clear();

            return ValueTask.CompletedTask;
        }

        public bool TryDequeue(out AudioTrackQueueItem item)
        {
            lock (_sync)
            {
                if (_items.Count == 0)
                {
                    item = null;
                    return false;
                }

                item = _items[0];
                _items.RemoveAt(0);

                return true;
            }
        }

        public IEnumerator<AudioTrackQueueItem> GetEnumerator()
        {
            lock (_sync) return _items.ToList().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
