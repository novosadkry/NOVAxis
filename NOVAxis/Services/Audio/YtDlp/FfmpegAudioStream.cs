using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NOVAxis.Core;
using NOVAxis.Extensions;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Decodes a remote stream into the raw format Discord expects: 48 kHz, stereo,
    /// signed 16 bit little endian.
    /// </summary>
    /// <remarks>
    /// A pipe read already in flight cannot be interrupted by a cancellation token, so
    /// cancellation kills ffmpeg instead: closing the write end releases the read.
    /// </remarks>
    public sealed class FfmpegAudioStream : IAsyncDisposable
    {
        public const int SampleRate = 48000;
        public const int Channels = 2;
        public const int BytesPerSample = sizeof(short);
        public const int FrameMilliseconds = 20;

        /// <summary>
        /// One Opus frame worth of PCM. Writing anything else to Discord's encoder
        /// produces distorted audio.
        /// </summary>
        public const int FrameSize = SampleRate / 1000 * FrameMilliseconds * Channels * BytesPerSample;

        public const int BytesPerSecond = SampleRate * Channels * BytesPerSample;

        /// <summary>
        /// Headers which ffmpeg either sets itself or rejects when passed through.
        /// </summary>
        private static readonly string[] IgnoredHeaders =
        {
            "User-Agent", "Host", "Accept-Encoding", "Connection", "Content-Length", "Range"
        };

        private readonly Process _process;
        private readonly Task<string> _standardError;
        private readonly CancellationTokenRegistration _registration;
        private readonly ILogger _logger;

        private bool _disposed;

        private FfmpegAudioStream(Process process, Task<string> standardError, CancellationTokenRegistration registration, ILogger logger)
        {
            _process = process;
            _standardError = standardError;
            _registration = registration;
            _logger = logger;
        }

        /// <summary>
        /// Number of bytes ffmpeg emitted so far, which is how playback position is derived.
        /// </summary>
        public long BytesRead { get; private set; }

        public static FfmpegAudioStream Start(
            AudioYtDlpOptions options,
            YtDlpStreamInfo stream,
            TimeSpan position,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var arguments = BuildArguments(options, stream, position);

            var startInfo = new ProcessStartInfo
            {
                FileName = options.FfmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            logger.Trace(ProcessRunner.Describe(options.FfmpegPath, arguments));

            var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                process.Dispose();
                throw new ProcessException(options.FfmpegPath, -1, e.Message);
            }

            // ffmpeg blocks once the stderr pipe fills up, so it has to be drained continuously
            var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

            // Disposal gives up on this read, and its diagnostics are
            // not worth reporting a failure over
            ProcessRunner.Observe(standardError);

            var registration = cancellationToken.UnsafeRegister(
                static (state, _) => ProcessRunner.Terminate((Process)state), process);

            return new FfmpegAudioStream(process, standardError, registration, logger);
        }

        /// <summary>
        /// Fills <paramref name="buffer"/> with a whole frame, returning fewer bytes only at
        /// the end of the stream.
        /// </summary>
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _process.StandardOutput.BaseStream.ReadAtLeastAsync(
                buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);

            BytesRead += read;
            return read;
        }

        /// <summary>
        /// Waits for ffmpeg to exit and surfaces its diagnostics, which is how a
        /// finished track is told apart from a failed one.
        /// </summary>
        public async ValueTask<int> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await _process.WaitForExitAsync(cancellationToken);
            return _process.ExitCode;
        }

        public string GetErrorOutput()
        {
            return _standardError.IsCompletedSuccessfully
                ? _standardError.Result
                : null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // Waits for a kill already in flight, without blocking the thread doing so
            await _registration.DisposeAsync();

            ProcessRunner.Terminate(_process);

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(timeout.Token);

                var error = await _standardError.WaitAsync(timeout.Token);

                if (!string.IsNullOrWhiteSpace(error))
                    _logger.Debug($"ffmpeg: {error.Trim()}");

                _logger.Debug($"ffmpeg exited with code {_process.ExitCode} after producing " +
                              $"{BytesRead / (double)BytesPerSecond:0.#}s of audio");
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("ffmpeg did not exit in time and was left to the operating system");
            }
            catch (Exception e)
            {
                _logger.Warning("Failed to shut down ffmpeg cleanly", e);
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static List<string> BuildArguments(AudioYtDlpOptions options, YtDlpStreamInfo stream, TimeSpan position)
        {
            var isHttp = stream.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);

            var arguments = new List<string>
            {
                "-hide_banner",
                "-loglevel", "warning",
                "-nostdin"
            };

            if (isHttp)
            {
                // Long tracks outlive a single connection, so let ffmpeg re-establish it
                arguments.AddRange(new[]
                {
                    "-reconnect", "1",
                    "-reconnect_streamed", "1",
                    "-reconnect_delay_max", "5"
                });

                var userAgent = stream.Headers
                    .FirstOrDefault(x => x.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)).Value;

                if (!string.IsNullOrWhiteSpace(userAgent ?? options.UserAgent))
                {
                    arguments.Add("-user_agent");
                    arguments.Add(userAgent ?? options.UserAgent);
                }

                var headers = BuildHeaders(stream.Headers);

                if (headers.Length > 0)
                {
                    arguments.Add("-headers");
                    arguments.Add(headers);
                }
            }

            // Input seeking, which lets ffmpeg jump instead of decoding up to the position
            if (position > TimeSpan.Zero)
            {
                arguments.Add("-ss");
                arguments.Add(position.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            arguments.Add("-i");
            arguments.Add(stream.Url);

            arguments.AddRange(new[]
            {
                "-vn", "-sn", "-dn",
                "-f", "s16le",
                "-ar", SampleRate.ToString(),
                "-ac", Channels.ToString(),
                "pipe:1"
            });

            return arguments;
        }

        private static string BuildHeaders(IReadOnlyDictionary<string, string> headers)
        {
            var builder = new StringBuilder();

            foreach (var (key, value) in headers)
            {
                if (IgnoredHeaders.Contains(key, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(value) || value.Contains('\n') || value.Contains('\r'))
                    continue;

                builder.Append(key).Append(": ").Append(value).Append("\r\n");
            }

            return builder.ToString();
        }
    }
}
