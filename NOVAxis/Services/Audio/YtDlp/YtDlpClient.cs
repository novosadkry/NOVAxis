using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

namespace NOVAxis.Services.Audio.YtDlp
{
    public sealed record YtDlpStreamInfo(string Url, IReadOnlyDictionary<string, string> Headers);

    /// <summary>
    /// A thin wrapper over the yt-dlp executable. Only ever asks for metadata: the media
    /// itself is fetched by ffmpeg from the URL resolved here.
    /// </summary>
    public class YtDlpClient
    {
        private readonly SemaphoreSlim _lookups;

        private IOptions<AudioOptions> Options { get; }
        private ILogger<YtDlpClient> Logger { get; }

        private AudioYtDlpOptions YtDlp => Options.Value.YtDlp;

        public YtDlpClient(IOptions<AudioOptions> options, ILogger<YtDlpClient> logger)
        {
            Options = options;
            Logger = logger;

            var limit = Math.Max(1, options.Value.YtDlp.MaxConcurrentLookups);
            _lookups = new SemaphoreSlim(limit, limit);
        }

        /// <summary>
        /// Resolves a search phrase or an URL into tracks, without touching the media itself.
        /// </summary>
        public virtual async ValueTask<AudioLoadResult> LoadAsync(string input, CancellationToken cancellationToken = default)
        {
            var arguments = new List<string>
            {
                "--dump-single-json",
                "--flat-playlist",
                "--ignore-errors",
                "--no-warnings",
                "--no-progress",
                "--skip-download"
            };

            AddCommonArguments(YtDlp, arguments);
            arguments.Add(input);

            string json;

            if (!await _lookups.WaitAsync(0, cancellationToken))
            {
                Logger.Debug($"Waiting for a free lookup slot before resolving '{input}'");
                await _lookups.WaitAsync(cancellationToken);
            }

            try
            {
                json = await RunAsync(arguments, cancellationToken);
            }
            finally
            {
                _lookups.Release();
            }

            if (string.IsNullOrWhiteSpace(json))
                return AudioLoadResult.Failed;

            var result = YtDlpJson.ReadLoadResult(json, YtDlp.MaxPlaylistSize);
            Logger.Debug($"Loaded {result.Tracks.Length} track(s) for '{input}'");

            return result;
        }

        /// <summary>
        /// Resolves the direct media URL of a track. Those URLs are short lived and bound to
        /// the requesting address, so this is deliberately done right before playback.
        /// </summary>
        public virtual async ValueTask<YtDlpStreamInfo> ResolveStreamAsync(AudioTrack track, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(track);

            if (track.Uri == null)
                throw new InvalidOperationException($"Track '{track.Title}' carries no address to resolve");

            var arguments = new List<string>
            {
                "--dump-single-json",
                "--no-playlist",
                "--no-warnings",
                "--no-progress",
                "--skip-download",
                "-f", YtDlp.Format
            };

            AddCommonArguments(YtDlp, arguments);
            arguments.Add(track.Uri.AbsoluteUri);

            var json = await RunAsync(arguments, cancellationToken);
            var streamInfo = string.IsNullOrWhiteSpace(json) ? null : YtDlpJson.ReadStreamInfo(json);

            if (streamInfo == null)
                throw new InvalidOperationException($"yt-dlp returned no playable format for '{track.Title}'");

            Logger.Debug($"Resolved '{track.Title}' to a stream on {HostOf(streamInfo.Url)}");

            return streamInfo;
        }

        /// <summary>
        /// The arguments every invocation carries. Static so the downloader inherits the
        /// same cookies, user agent and retry policy without a second copy of the config.
        /// </summary>
        /// <summary>
        /// Reads everything a download needs to decide: the titling and every rendition on
        /// offer. Shares the lookup gate with <see cref="LoadAsync"/> - it is the same
        /// kind of work, and the same extractor cost.
        /// </summary>
        public virtual async ValueTask<YtDlpMediaInfo> ProbeAsync(string url, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);

            var arguments = new List<string>
            {
                "--dump-single-json",
                "--no-playlist",
                "--no-warnings",
                "--no-progress",
                "--skip-download"
            };

            AddCommonArguments(YtDlp, arguments);

            // A url beginning with a dash would otherwise be read as a flag
            arguments.Add("--");
            arguments.Add(url);

            string json;

            if (!await _lookups.WaitAsync(0, cancellationToken))
            {
                Logger.Debug("Waiting for a free lookup slot before probing a download");
                await _lookups.WaitAsync(cancellationToken);
            }

            try
            {
                json = await RunAsync(arguments, cancellationToken);
            }
            finally
            {
                _lookups.Release();
            }

            if (string.IsNullOrWhiteSpace(json))
                return null;

            var info = YtDlpJson.ReadMediaInfo(json);

            if (info != null)
                Logger.Debug($"Probed '{info.Title}' with {info.Formats.Count} format(s)");

            return info;
        }

        internal static void AddCommonArguments(AudioYtDlpOptions ytDlp, List<string> arguments)
        {
            arguments.Add("--socket-timeout");
            arguments.Add("15");
            arguments.Add("--retries");
            arguments.Add("3");

            if (!string.IsNullOrWhiteSpace(ytDlp.CookiesFile))
            {
                arguments.Add("--cookies");
                arguments.Add(ytDlp.CookiesFile);
            }

            if (!string.IsNullOrWhiteSpace(ytDlp.UserAgent))
            {
                arguments.Add("--user-agent");
                arguments.Add(ytDlp.UserAgent);
            }

            if (ytDlp.ExtraArguments != null)
                arguments.AddRange(ytDlp.ExtraArguments);
        }

        private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Logger.Trace(ProcessRunner.Describe(YtDlp.ExecutablePath, arguments));

            var started = Stopwatch.GetTimestamp();

            var result = await ProcessRunner.RunAsync(
                YtDlp.ExecutablePath, arguments, YtDlp.ResolveTimeout, cancellationToken);

            Logger.Debug($"yt-dlp finished in {Stopwatch.GetElapsedTime(started).TotalSeconds:0.#}s " +
                         $"with code {result.ExitCode}");

            // With --ignore-errors yt-dlp reports a failure whenever a single entry of a
            // playlist could not be extracted, while still writing a usable document
            if (!result.IsSuccess)
            {
                if (string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    Logger.Warning($"yt-dlp exited with code {result.ExitCode}: {result.StandardError?.Trim()}");
                    result.EnsureSuccess();
                }

                Logger.Warning($"yt-dlp reported errors while resolving: {result.StandardError?.Trim()}");
            }

            return result.StandardOutput;
        }

        /// <summary>
        /// Names the host serving a resolved address. The address itself is signed and
        /// bound to this machine, so it is kept out of the log.
        /// </summary>
        private static string HostOf(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? uri.Host
                : "an unknown host";
        }
    }
}
