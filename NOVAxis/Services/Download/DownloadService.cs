using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Utilities;

namespace NOVAxis.Services.Download
{
    /// <summary>
    /// One rendition offered to the person choosing, already judged against the size limit.
    /// </summary>
    public sealed record DownloadChoice(
        string Id,
        DownloadKind Kind,
        string Label,
        string Extension,
        long? Size,
        bool WithinLimit);

    public sealed record DownloadQuota(int Limit, int Remaining, DateTimeOffset? ResetsAt);

    /// <summary>
    /// The whole download feature, and the only place its rules live. Both the slash command
    /// and the web api come through here, which is what stops the two surfaces being played
    /// against each other for twice the quota.
    /// </summary>
    public class DownloadService
    {
        private readonly Coalescer<string, YtDlpMediaInfo> _probes = new();

        private IOptions<DownloadOptions> Options { get; }
        private YtDlpClient Client { get; }
        private YtDlpDownloader Downloader { get; }
        private DownloadStore Store { get; }
        private IHostApplicationLifetime Lifetime { get; }
        private ILogger<DownloadService> Logger { get; }

        public DownloadService(
            IOptions<DownloadOptions> options,
            YtDlpClient client,
            YtDlpDownloader downloader,
            DownloadStore store,
            IHostApplicationLifetime lifetime,
            ILogger<DownloadService> logger)
        {
            Options = options;
            Client = client;
            Downloader = downloader;
            Store = store;
            Lifetime = lifetime;
            Logger = logger;
        }

        public bool Active => Options.Value.Active;

        /// <summary>The limits the surfaces quote back to the person asking.</summary>
        public DownloadOptions Settings => Options.Value;

        public DownloadRecord Find(ulong id) => Store.Find(id);

        public DownloadRecord ForUser(ulong userId) => Store.FindByOwner(userId);

        public DownloadQuota QuotaFor(ulong userId)
        {
            var options = Options.Value;
            var (used, resetsAt) = Store.SlotsUsed(userId, options.QuotaWindow);

            return new DownloadQuota(
                options.MaxPerWindow,
                System.Math.Max(0, options.MaxPerWindow - used),
                resetsAt);
        }

        /// <summary>
        /// Looks a link up without committing to fetching it. Coalesced, so two people
        /// pasting the same link - or one double submitting - costs a single extractor run.
        /// </summary>
        public async Task<YtDlpMediaInfo> ProbeAsync(string url, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(url);

            var info = await _probes.RunAsync(normalized,
                token => Client.ProbeAsync(normalized, token).AsTask(), cancellationToken);

            if (info == null)
                throw new DownloadException(DownloadFailure.Failed, "Na této adrese se nic nenašlo");

            if (info.IsLive)
                throw new DownloadException(DownloadFailure.Unsupported,
                    "Živé vysílání stáhnout nelze - nemá konec ani velikost");

            return info;
        }

        /// <summary>
        /// The renditions worth offering, best first, with the ones which cannot fit already
        /// marked. Video formats without audio are costed together with the audio they would
        /// be merged with, because that is what actually lands on disk.
        /// </summary>
        public IReadOnlyList<DownloadChoice> ChoicesFor(YtDlpMediaInfo info, DownloadKind kind)
        {
            var options = Options.Value;

            if (kind == DownloadKind.Audio)
            {
                return options.AudioFormats
                    .Select(f => new DownloadChoice(f, DownloadKind.Audio, f.ToUpperInvariant(), f, null, true))
                    .ToList();
            }

            var bestAudio = info.Formats
                .Where(f => f.HasAudio && !f.HasVideo)
                .Select(f => f.Size)
                .Where(s => s.HasValue)
                .DefaultIfEmpty(null)
                .Max();

            return info.Formats
                .Where(f => f.HasVideo)
                .OrderByDescending(f => f.Size ?? 0)
                .ThenByDescending(f => f.Bitrate ?? 0)
                .Select(f =>
                {
                    var size = f.Size;

                    if (size.HasValue && !f.HasAudio && bestAudio.HasValue)
                        size += bestAudio.Value;

                    return new DownloadChoice(
                        f.Id,
                        DownloadKind.Video,
                        f.Describe(),
                        f.Ext,
                        size,
                        !size.HasValue || size.Value <= options.MaxFileSize);
                })
                .ToList();
        }

        /// <summary>
        /// Prepares a download and starts it. Returns as soon as the record exists - the work
        /// itself outlives the request that asked for it.
        /// </summary>
        public async Task<DownloadRecord> RequestAsync(
            ulong userId,
            string url,
            DownloadKind kind,
            string formatId,
            YtDlpMediaInfo info = null,
            CancellationToken cancellationToken = default)
        {
            var options = Options.Value;
            var normalized = Normalize(url);

            info ??= await ProbeAsync(normalized, cancellationToken);

            if (info.IsLive)
                throw new DownloadException(DownloadFailure.Unsupported,
                    "Živé vysílání stáhnout nelze - nemá konec ani velikost");

            var choice = Resolve(info, kind, formatId);

            if (choice != null && !choice.WithinLimit)
                throw new DownloadException(DownloadFailure.TooLarge,
                    $"Tenhle formát má {Megabytes(choice.Size!.Value)} MB, " +
                    $"povoleno je {Megabytes(options.MaxFileSize)} MB");

            var gate = Store.Gate(userId);
            await gate.WaitAsync(cancellationToken);

            DownloadRecord record;

            try
            {
                var previous = Store.FindByOwner(userId);

                // Whatever is admitted below has to be decided before the previous download
                // is touched: being turned away must not also cost the caller the link they
                // already had
                var reclaimable = previous?.Size ?? 0;

                if (Store.TotalBytes - reclaimable + options.MaxFileSize > options.OutputFolderLimit)
                {
                    Logger.Warning("The download folder is full, refusing new downloads");
                    throw new DownloadException(DownloadFailure.StorageFull,
                        "Úložiště je plné, zkus to prosím později");
                }

                if (!Store.TryTakeSlot(userId, options.MaxPerWindow, options.QuotaWindow, out var retryAfter))
                {
                    throw new DownloadException(DownloadFailure.QuotaExceeded,
                        $"Vyčerpal jsi {options.MaxPerWindow} stažení za hodinu, " +
                        $"zkus to znovu za {Minutes(retryAfter)}");
                }

                // A download still running holds a concurrency slot which replacing it gives
                // back, so in that case the slot is claimed afterwards rather than before -
                // otherwise a single-slot host could never replace its own running download
                var holdsSlot = previous != null && !previous.IsFinished;

                if (!holdsSlot && !Downloader.TryEnter())
                {
                    Store.ReturnSlot(userId);

                    throw new DownloadException(DownloadFailure.Busy,
                        "Právě běží jiná stahování, zkus to za chvíli");
                }

                // Only one link per person stays live, so the previous one goes now, files
                // and all, once the new one is certain to be admitted
                if (previous != null)
                {
                    Logger.Debug($"Replacing download {previous.Id} of user {userId}");
                    await Store.RemoveAsync(previous, DownloadState.Revoked);
                }

                if (holdsSlot && !Downloader.TryEnter())
                {
                    Store.ReturnSlot(userId);

                    throw new DownloadException(DownloadFailure.Busy,
                        "Právě běží jiná stahování, zkus to za chvíli");
                }

                try
                {
                    record = Begin(userId, kind, normalized, info, choice, formatId, options);
                }
                catch (Exception)
                {
                    // Nothing ran, so the slot and the quota go straight back
                    Downloader.Exit();
                    Store.ReturnSlot(userId);
                    throw;
                }
            }
            finally
            {
                gate.Release();
            }

            Logger.Info($"Download {record.Id} started for user {userId} ({kind}, {record.FormatLabel})");

            return record;
        }

        private DownloadRecord Begin(
            ulong userId,
            DownloadKind kind,
            string url,
            YtDlpMediaInfo info,
            DownloadChoice choice,
            string formatId,
            DownloadOptions options)
        {
            var id = Snowflake.Next();
            var now = DateTimeOffset.UtcNow;

            var record = new DownloadRecord
            {
                Id = id,
                OwnerId = userId,
                Kind = kind,
                SourceUrl = url,
                Title = info.Title,
                FormatId = choice?.Id ?? formatId,
                FormatLabel = choice?.Label ?? formatId,
                DirectoryPath = Path.Combine(Store.Root, id.ToString()),
                CreatedAt = now,
                ExpiresAt = now + options.Ttl,
                EstimatedSize = choice?.Size,
                State = DownloadState.Pending
            };

            // Tied to the application, never to the request or interaction which asked:
            // a ten minute job must not die because a POST returned in forty milliseconds
            record.Lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                Lifetime.ApplicationStopping);

            // Held back until the record is registered, so a teardown arriving in between
            // cannot find a download with no work attached to wait for
            var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            record.Worker = Task.Run(async () =>
            {
                await registered.Task;
                await RunAsync(record);
            }, CancellationToken.None);

            Store.Add(record);
            registered.SetResult();

            return record;
        }

        public async Task<bool> RevokeAsync(ulong userId, ulong id)
        {
            var gate = Store.Gate(userId);
            await gate.WaitAsync();

            try
            {
                var record = Store.Find(id);

                if (record == null || record.OwnerId != userId)
                    return false;

                await Store.RemoveAsync(record, DownloadState.Revoked);
                return true;
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task RunAsync(DownloadRecord record)
        {
            try
            {
                record.State = DownloadState.Running;

                var outcome = await Downloader.RunAsync(record, record.Lifetime.Token);

                // A replacement or a revoke may have landed while yt-dlp was finishing, and
                // the teardown will already have given up waiting - so clean up after
                // ourselves rather than publishing a file nothing points at
                if (record.State == DownloadState.Revoked || record.Lifetime.IsCancellationRequested)
                {
                    Store.Forget(record);
                    return;
                }

                Store.Publish(record, outcome.FilePath, outcome.Size);

                Logger.Info($"Download {record.Id} ready: {Megabytes(outcome.Size)} MB");
            }
            catch (OperationCanceledException)
            {
                record.State = DownloadState.Revoked;
                Store.DiscardFiles(record);
            }
            catch (DownloadException e)
            {
                Fail(record, e.Message);
            }
            catch (ProcessException e)
            {
                Logger.Warning($"Download {record.Id} failed", e);
                Fail(record, "Stahování se nezdařilo");
            }
            catch (Exception e)
            {
                Logger.Error($"Download {record.Id} failed unexpectedly", e);
                Fail(record, "Stahování se nezdařilo");
            }
            finally
            {
                Downloader.Exit();
            }
        }

        private void Fail(DownloadRecord record, string message)
        {
            record.Error = message;
            record.State = DownloadState.Failed;

            Store.DiscardFiles(record);
        }

        private DownloadChoice Resolve(YtDlpMediaInfo info, DownloadKind kind, string formatId)
        {
            var choices = ChoicesFor(info, kind);

            if (kind == DownloadKind.Audio)
            {
                // The audio formats are ours, not yt-dlp's, and go straight into an argument
                var match = choices.FirstOrDefault(c =>
                    string.Equals(c.Id, formatId, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                    throw new DownloadException(DownloadFailure.Unsupported, "Neznámý formát");

                return match;
            }

            if (string.IsNullOrEmpty(formatId))
                return null;

            return choices.FirstOrDefault(c => c.Id == formatId)
                   ?? throw new DownloadException(DownloadFailure.Unsupported, "Neznámý formát");
        }

        /// <summary>
        /// Checks a link before yt-dlp ever sees it. Accepting anything absolute - as the
        /// first attempt at this did - hands yt-dlp's generic extractor a file:// path or an
        /// address on the host's own network and serves whatever it finds back to the caller.
        /// </summary>
        public static string Normalize(string input)
        {
            if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri))
                throw new DownloadException(DownloadFailure.Unsupported, "Neplatná adresa");

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                throw new DownloadException(DownloadFailure.Unsupported,
                    "Podporované jsou jen adresy http a https");

            if (IPAddress.TryParse(uri.DnsSafeHost, out var address) && IsBlocked(address))
                throw new DownloadException(DownloadFailure.Unsupported,
                    "Na tuhle adresu se stahovat nedá");

            return uri.AbsoluteUri;
        }

        private static bool IsBlocked(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.IPv6Any))
                return true;

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;

            var octets = address.GetAddressBytes();

            return octets[0] switch
            {
                0 => true,                                        // this network
                10 => true,                                       // private
                127 => true,                                      // loopback
                169 when octets[1] == 254 => true,                // link local, and the cloud metadata service
                172 when octets[1] >= 16 && octets[1] <= 31 => true,
                192 when octets[1] == 168 => true,
                >= 224 => true,                                   // multicast and reserved
                _ => false
            };
        }

        private static long Megabytes(long bytes) => bytes / 1024 / 1024;

        private static string Minutes(TimeSpan span)
        {
            var minutes = (int)System.Math.Ceiling(span.TotalMinutes);
            return minutes <= 1 ? "chvíli" : $"{minutes} min";
        }
    }
}
