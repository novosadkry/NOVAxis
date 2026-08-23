using System;
using System.Collections.Generic;
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
    /// A thin wrapper over the yt-dlp executable. It only ever asks yt-dlp for metadata -
    /// the media itself is fetched by ffmpeg from the URL resolved here, which keeps seeking
    /// and reconnecting in a single process.
    /// </summary>
    public class YtDlpClient
    {
        private IOptions<AudioOptions> Options { get; }
        private ILogger<YtDlpClient> Logger { get; }

        private AudioYtDlpOptions YtDlp => Options.Value.YtDlp;

        public YtDlpClient(IOptions<AudioOptions> options, ILogger<YtDlpClient> logger)
        {
            Options = options;
            Logger = logger;
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

            AddCommonArguments(arguments);
            arguments.Add(input);

            var json = await RunAsync(arguments, cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
                return AudioLoadResult.Failed;

            return YtDlpJson.ReadLoadResult(json, YtDlp.MaxPlaylistSize);
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

            AddCommonArguments(arguments);
            arguments.Add(track.Uri.AbsoluteUri);

            var json = await RunAsync(arguments, cancellationToken);
            var streamInfo = string.IsNullOrWhiteSpace(json) ? null : YtDlpJson.ReadStreamInfo(json);

            if (streamInfo == null)
                throw new InvalidOperationException($"yt-dlp returned no playable format for '{track.Title}'");

            return streamInfo;
        }

        private void AddCommonArguments(List<string> arguments)
        {
            arguments.Add("--socket-timeout");
            arguments.Add("15");
            arguments.Add("--retries");
            arguments.Add("3");

            if (!string.IsNullOrWhiteSpace(YtDlp.CookiesFile))
            {
                arguments.Add("--cookies");
                arguments.Add(YtDlp.CookiesFile);
            }

            if (!string.IsNullOrWhiteSpace(YtDlp.UserAgent))
            {
                arguments.Add("--user-agent");
                arguments.Add(YtDlp.UserAgent);
            }
        }

        private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Logger.Trace(ProcessRunner.Describe(YtDlp.ExecutablePath, arguments));

            var result = await ProcessRunner.RunAsync(
                YtDlp.ExecutablePath, arguments, YtDlp.ResolveTimeout, cancellationToken);

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
    }
}
