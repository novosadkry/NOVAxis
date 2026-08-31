using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

namespace NOVAxis.Services.Download
{
    /// <summary>
    /// Frees what nobody can reach any more. Expiry has to be swept rather than left to a
    /// cache's own eviction, because a file only stops costing disk when something actually
    /// deletes it.
    /// </summary>
    public class DownloadSweeper : BackgroundService
    {
        private DownloadStore Store { get; }
        private IOptions<DownloadOptions> Options { get; }
        private ILogger<DownloadSweeper> Logger { get; }

        public DownloadSweeper(
            DownloadStore store,
            IOptions<DownloadOptions> options,
            ILogger<DownloadSweeper> logger)
        {
            Store = store;
            Options = options;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Options.Value.Active)
                return;

            // Records do not survive a restart, so anything already on disk - half written
            // .part files included - belongs to a run which is over
            Store.PurgeRoot();

            var interval = Options.Value.SweepInterval;

            if (interval <= TimeSpan.Zero)
                interval = TimeSpan.FromMinutes(1);

            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync();
                }
                catch (Exception e)
                {
                    Logger.Warning("Download sweep failed", e);
                }
            }
        }

        private async Task SweepAsync()
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var record in Store.All)
            {
                if (now < record.ExpiresAt)
                    continue;

                await Store.RemoveAsync(record, DownloadState.Expired);

                Logger.Debug($"Download {record.Id} expired and was freed");
            }

            Store.TrimSlots(Options.Value.QuotaWindow);

            SweepOrphans();
        }

        /// <summary>
        /// Picks up directories no record points at. On Windows a delete can lose the race
        /// with a response still reading the file, and this is the retry.
        /// </summary>
        private void SweepOrphans()
        {
            var root = new DirectoryInfo(Store.Root);

            if (!root.Exists)
                return;

            var live = Store.All
                .Select(r => r.Id.ToString())
                .ToHashSet(StringComparer.Ordinal);

            foreach (var directory in root.EnumerateDirectories())
            {
                if (live.Contains(directory.Name))
                    continue;

                try
                {
                    directory.Delete(recursive: true);
                    Logger.Debug($"Removed orphaned download directory '{directory.Name}'");
                }
                catch (IOException) { /* still in use; the next sweep tries again */ }
                catch (UnauthorizedAccessException e)
                {
                    Logger.Warning($"Not allowed to remove '{directory.FullName}'", e);
                }
            }
        }
    }
}
