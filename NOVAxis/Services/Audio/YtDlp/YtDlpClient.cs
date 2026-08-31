using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;
using NOVAxis.Services.Net;

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
        private GuardedProxy Guard { get; }
        private ILogger<YtDlpClient> Logger { get; }

        private AudioYtDlpOptions YtDlp => Options.Value.YtDlp;

        public YtDlpClient(IOptions<AudioOptions> options, GuardedProxy guard, ILogger<YtDlpClient> logger)
        {
            Options = options;
            Guard = guard;
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

            AddCommonArguments(YtDlp, Guard?.ProxyUrl, arguments);
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

            AddCommonArguments(YtDlp, Guard?.ProxyUrl, arguments);
            arguments.Add(track.Uri.AbsoluteUri);

            var json = await RunAsync(arguments, cancellationToken);
            var streamInfo = string.IsNullOrWhiteSpace(json) ? null : YtDlpJson.ReadStreamInfo(json);

            if (streamInfo == null)
                throw new InvalidOperationException($"yt-dlp returned no playable format for '{track.Title}'");

            // ffmpeg fetches this address itself, and it does not go through the guard, so
            // here is the one place it can be checked. A page is free to name whatever
            // address it likes as the media, including one on this host's own network.
            if (!await IsReachableAsync(streamInfo.Url, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"yt-dlp resolved '{track.Title}' to an address which is not allowed");
            }

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

            AddCommonArguments(YtDlp, Guard?.ProxyUrl, arguments);

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

        /// <summary>
        /// Whether an address ffmpeg is about to be pointed at leads somewhere public.
        /// </summary>
        private static async ValueTask<bool> IsReachableAsync(string url, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            // Not only http: ffmpeg is handed rtmp and the like too, and a local path would
            // have it read this machine's disk out into a voice channel
            if (uri.IsFile || uri.IsUnc || string.IsNullOrEmpty(uri.Host))
                return false;

            var addresses = await PrivateNetworks.ResolveAsync(uri.DnsSafeHost, cancellationToken);

            return addresses.Count > 0;
        }

        internal static void AddCommonArguments(AudioYtDlpOptions ytDlp, string proxyUrl, List<string> arguments)
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

            // Last, so that it is ours and not something an operator's extra arguments set
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                arguments.Add("--proxy");
                arguments.Add(proxyUrl);
            }
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
