using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;
using NOVAxis.Services.Audio.YtDlp;

namespace NOVAxis.Services.Download
{
    public enum DownloadFailure
    {
        Failed,
        TooLarge,
        Timeout,
        Stalled,
        Busy,
        QuotaExceeded,
        StorageFull,
        Unsupported
    }

    /// <summary>
    /// A download which could not be delivered. The reason is what the surfaces branch on;
    /// yt-dlp's own words stay in the log, because they carry cookie paths and local
    /// filesystem layout that nobody in a Discord channel should be reading.
    /// </summary>
    public class DownloadException : Exception
    {
        public DownloadFailure Reason { get; }

        public DownloadException(DownloadFailure reason, string message) : base(message)
        {
            Reason = reason;
        }
    }

    public sealed record DownloadOutcome(string FilePath, long Size);

    /// <summary>
    /// Runs yt-dlp to completion against one link. Unlike the metadata lookups this can take
    /// minutes and write gigabytes, so it manages the child process directly - watching what
    /// it writes rather than trusting it to stop where it was told.
    /// </summary>
    public class YtDlpDownloader
    {
        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(2);
        private const int ErrorOutputLimit = 4096;

        /// <summary>Where yt-dlp is told to write the path of what it produced.</summary>
        private const string PathFileName = ".ytdlp-path";

        private const int KillNone = 0;
        private const int KillOversize = 1;
        private const int KillStalled = 2;

        private readonly SemaphoreSlim _slots;

        private IOptions<DownloadOptions> Options { get; }
        private IOptions<AudioOptions> Audio { get; }
        private ILogger<YtDlpDownloader> Logger { get; }

        public YtDlpDownloader(
            IOptions<DownloadOptions> options,
            IOptions<AudioOptions> audio,
            ILogger<YtDlpDownloader> logger)
        {
            Options = options;
            Audio = audio;
            Logger = logger;

            var limit = Math.Max(1, options.Value.MaxConcurrentDownloads);
            _slots = new SemaphoreSlim(limit, limit);
        }

        /// <summary>
        /// Claims one of the download slots, or fails at once. Deliberately never queues: a
        /// user watching "downloading..." for eight minutes because two other downloads are
        /// ahead of them is worse served than one told to come back.
        /// </summary>
        public bool TryEnter() => _slots.Wait(0);

        public void Exit() => _slots.Release();

        public async Task<DownloadOutcome> RunAsync(DownloadRecord record, CancellationToken cancellationToken)
        {
            var options = Options.Value;
            var ytDlp = Audio.Value.YtDlp;

            Directory.CreateDirectory(record.DirectoryPath);

            var arguments = BuildArguments(record, options, ytDlp);

            var startInfo = new ProcessStartInfo
            {
                FileName = ytDlp.ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            Logger.Trace(ProcessRunner.Describe(ytDlp.ExecutablePath, arguments));

            using var timeoutSource = new CancellationTokenSource(options.Timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);

            using var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                throw new ProcessException(ytDlp.ExecutablePath, -1, e.Message);
            }

            var started = Stopwatch.GetTimestamp();

            // --print writes one line here; the progress output is off, so this stays small
            var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var standardError = DrainErrorAsync(process, Logger);

            ProcessRunner.Observe(standardOutput);
            ProcessRunner.Observe(standardError);

            await using var registration = linkedSource.Token.UnsafeRegister(
                static (state, _) => ProcessRunner.Terminate((Process)state), process);

            var killReason = new StrongBox<int>(KillNone);

            // Disposed before the reason is read, so no tick is still deciding
            using (StartWatchdog(record, process, options, killReason))
            {
                try
                {
                    await process.WaitForExitAsync(linkedSource.Token);
                }
                catch (OperationCanceledException)
                {
                    ProcessRunner.Terminate(process);

                    // The caller giving up propagates; running out of time is a failure
                    cancellationToken.ThrowIfCancellationRequested();

                    throw new DownloadException(DownloadFailure.Timeout,
                        $"Stahování se nestihlo do {options.Timeout.TotalMinutes:0.#} min");
                }
            }

            var error = await SafeAwait(standardError);
            var output = await SafeAwait(standardOutput);

            Logger.Debug($"yt-dlp finished download {record.Id} in " +
                         $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.#}s with code {process.ExitCode}");

            switch (Volatile.Read(ref killReason.Value))
            {
                case KillOversize:
                    throw new DownloadException(DownloadFailure.TooLarge,
                        $"Soubor přerostl povolených {Megabytes(options.MaxFileSize)} MB");

                case KillStalled:
                    throw new DownloadException(DownloadFailure.Stalled,
                        $"Stahování se na {options.StallTimeout.TotalSeconds:0}s zaseklo");
            }

            var path = ResolveOutput(record);

            if (path == null)
            {
                // yt-dlp treats --max-filesize as a filter: it declines the download and
                // still exits cleanly, so an empty result with that in the log is a size
                // rejection rather than a breakage
                if (MentionsSizeLimit(output))
                    throw new DownloadException(DownloadFailure.TooLarge,
                        $"Soubor je větší než povolených {Megabytes(options.MaxFileSize)} MB");

                if (process.ExitCode != 0)
                {
                    Logger.Warning($"yt-dlp failed for download {record.Id}: {Summarize(error)}");
                    throw new DownloadException(DownloadFailure.Failed, "Tenhle odkaz se stáhnout nepodařilo");
                }

                Logger.Warning($"yt-dlp produced no file for download {record.Id}: {Summarize(error)}");
                throw new DownloadException(DownloadFailure.Failed, "Nevznikl žádný soubor");
            }

            var size = new FileInfo(path).Length;

            // The last line of defence: each leg of a merge can sit under the ceiling
            // while the muxed result does not
            if (size > options.MaxFileSize)
                throw new DownloadException(DownloadFailure.TooLarge,
                    $"The finished file is larger than the {Megabytes(options.MaxFileSize)} MB limit");

            return new DownloadOutcome(path, size);
        }

        /// <summary>
        /// Watches what lands on disk. This is the only size check that holds when yt-dlp
        /// cannot say up front how big something is - which is every fragmented stream, and
        /// exactly where --max-filesize quietly does nothing.
        /// </summary>
        private IDisposable StartWatchdog(
            DownloadRecord record, Process process, DownloadOptions options, StrongBox<int> reason)
        {
            var limit = (long)(options.MaxFileSize * Math.Max(1d, options.SizeWatchdogFactor));
            var lastSize = -1L;
            var lastGrowth = Stopwatch.GetTimestamp();
            var ticking = 0;

            return new Timer(_ =>
            {
                // A walk of a large directory can outlast the interval
                if (Interlocked.Exchange(ref ticking, 1) == 1)
                    return;

                try
                {
                    if (process.HasExited)
                        return;

                    var size = DirectorySize(record.DirectoryPath);
                    record.Received = size;

                    if (size > limit)
                    {
                        Interlocked.CompareExchange(ref reason.Value, KillOversize, KillNone);
                        Logger.Warning($"Download {record.Id} passed {Megabytes(limit)} MB on disk and was stopped");
                        ProcessRunner.Terminate(process);
                        return;
                    }

                    if (size != lastSize)
                    {
                        lastSize = size;
                        lastGrowth = Stopwatch.GetTimestamp();
                        return;
                    }

                    if (Stopwatch.GetElapsedTime(lastGrowth) >= options.StallTimeout)
                    {
                        Interlocked.CompareExchange(ref reason.Value, KillStalled, KillNone);
                        Logger.Warning($"Download {record.Id} stopped growing and was given up on");
                        ProcessRunner.Terminate(process);
                    }
                }
                catch (Exception e)
                {
                    Logger.Debug($"Download watchdog tick failed for {record.Id}: {e.Message}");
                }
                finally
                {
                    Volatile.Write(ref ticking, 0);
                }
            }, null, WatchdogInterval, WatchdogInterval);
        }

        private static async Task<string> SafeAwait(Task<string> task)
        {
            try
            {
                return await task;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// The path yt-dlp reported, checked to actually be inside the download's own
        /// directory. An extractor bug or a stray -o in the operator's extra arguments
        /// must not be able to publish a file from somewhere else on the disk.
        /// </summary>
        private string ResolveOutput(DownloadRecord record)
        {
            var root = Path.GetFullPath(record.DirectoryPath);
            var pathFile = Path.Combine(root, PathFileName);
            string printed = null;

            if (File.Exists(pathFile))
            {
                // Appended to, so the last line is the one this run wrote
                printed = File.ReadLines(pathFile)
                    .Select(line => line.Trim())
                    .LastOrDefault(line => line.Length > 0);

                TryDelete(pathFile);
            }

            var candidate = !string.IsNullOrEmpty(printed) && File.Exists(printed)
                ? printed
                : LargestFile(root);

            if (candidate == null)
                return null;

            var full = Path.GetFullPath(candidate);

            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Logger.Warning($"yt-dlp wrote download {record.Id} outside its own directory, discarding it");
                return null;
            }

            if (string.IsNullOrEmpty(printed))
                Logger.Warning($"yt-dlp named no finished file for download {record.Id}, taking the largest one");

            return full;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException) { /* it goes with the directory anyway */ }
            catch (UnauthorizedAccessException) { /* likewise */ }
        }

        private static string LargestFile(string root)
        {
            var directory = new DirectoryInfo(root);

            if (!directory.Exists)
                return null;

            return directory
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => f.Name != PathFileName)
                .Where(f => f.Extension is not (".part" or ".ytdl" or ".temp"))
                .OrderByDescending(f => f.Length)
                .FirstOrDefault()?.FullName;
        }

        private static long DirectorySize(string path)
        {
            var directory = new DirectoryInfo(path);

            if (!directory.Exists)
                return 0;

            var total = 0L;

            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    total += file.Length;
                }
                catch (IOException) { /* vanished mid-walk */ }
                catch (UnauthorizedAccessException) { /* not ours to measure */ }
            }

            return total;
        }

        private static bool MentionsSizeLimit(string output)
        {
            return !string.IsNullOrEmpty(output) &&
                   output.Contains("larger than max-filesize", StringComparison.OrdinalIgnoreCase);
        }

        private static long Megabytes(long bytes) => bytes / 1024 / 1024;

        private static string Summarize(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return "(no output)";

            var trimmed = error.Trim();

            return trimmed.Length > 500 ? trimmed[..500] + "..." : trimmed;
        }

        /// <summary>
        /// Reports yt-dlp's diagnostics as they arrive, and keeps the pipe from filling up
        /// and blocking the download.
        /// </summary>
        private static async Task<string> DrainErrorAsync(Process process, ILogger logger)
        {
            var builder = new StringBuilder();

            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                logger.Debug($"yt-dlp: {line}");

                if (builder.Length < ErrorOutputLimit)
                    builder.AppendLine(line);
            }

            return builder.ToString();
        }

        private static List<string> BuildArguments(
            DownloadRecord record, DownloadOptions options, AudioYtDlpOptions ytDlp)
        {
            var arguments = new List<string>
            {
                "--no-playlist",
                "--no-warnings",
                "--no-progress",
                "--no-colors",
                "--no-continue",
                "--no-overwrites",
                "--no-exec",
                "--restrict-filenames",
                "--max-filesize", options.MaxFileSize.ToString(CultureInfo.InvariantCulture)
            };

            // Only when it names a real location - yt-dlp reads a bare name as a relative
            // path rather than looking it up the way the shell would
            if (!string.IsNullOrWhiteSpace(ytDlp.FfmpegPath) &&
                ytDlp.FfmpegPath.Contains(Path.DirectorySeparatorChar))
            {
                arguments.Add("--ffmpeg-location");
                arguments.Add(ytDlp.FfmpegPath);
            }

            if (record.Kind == DownloadKind.Video)
            {
                arguments.Add("--merge-output-format");
                arguments.Add(options.MergeOutputFormat);
                arguments.Add("-f");
                arguments.Add(string.IsNullOrEmpty(record.FormatId)
                    ? options.VideoFormat
                    : $"{record.FormatId}+bestaudio/{record.FormatId}");
            }
            else
            {
                arguments.Add("-f");
                arguments.Add("bestaudio/best");
                arguments.Add("-x");
                arguments.Add("--audio-format");
                arguments.Add(record.FormatId);
                arguments.Add("--audio-quality");
                arguments.Add("0");
            }

            YtDlpClient.AddCommonArguments(ytDlp, arguments);

            // Ours go last on purpose: yt-dlp honours the final occurrence of an option, so
            // an operator's ExtraArguments cannot redirect the output somewhere we do not own
            arguments.Add("-P");
            arguments.Add($"home:{record.DirectoryPath}");
            arguments.Add("-P");
            arguments.Add($"temp:{record.DirectoryPath}");
            arguments.Add("-o");
            arguments.Add("%(title).100s.%(ext)s");

            // Not --print: that implies --quiet, and the line saying a file was refused for
            // being too large is one of the ordinary messages it would swallow
            arguments.Add("--print-to-file");
            arguments.Add("after_move:filepath");
            arguments.Add(Path.Combine(record.DirectoryPath, PathFileName));

            // A link beginning with a dash would otherwise be read as a flag
            arguments.Add("--");
            arguments.Add(record.SourceUrl);

            return arguments;
        }
    }
}
