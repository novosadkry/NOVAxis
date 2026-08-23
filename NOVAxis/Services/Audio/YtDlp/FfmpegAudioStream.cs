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
        /// How long a frame the player is waiting for may take to arrive. A stream ffmpeg
        /// cannot fetch leaves it reconnecting rather than failing, which would hold the
        /// track in place for as long as the player is up. Long enough that a connection
        /// which does come back is not cut short.
        /// </summary>
        private static readonly TimeSpan ReadStallTimeout = TimeSpan.FromSeconds(20);

        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(2);

        /// <summary>
        /// How much of ffmpeg's output is kept for the failure message. Every line is
        /// logged as it arrives, so the rest is only ever a duplicate.
        /// </summary>
        private const int ErrorOutputLimit = 4096;

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
        private readonly Timer _watchdog;
        private readonly long _startedAt;

        private long _bytesRead;
        private long _readPendingSince;
        private volatile bool _disposed;

        private FfmpegAudioStream(Process process, Task<string> standardError, CancellationTokenRegistration registration, ILogger logger)
        {
            _process = process;
            _standardError = standardError;
            _registration = registration;
            _logger = logger;
            _startedAt = Stopwatch.GetTimestamp();

            _watchdog = new Timer(
                static state => ((FfmpegAudioStream)state).CheckForStall(),
                this, WatchdogInterval, WatchdogInterval);
        }

        /// <summary>
        /// Number of bytes ffmpeg emitted so far, which is how playback position is derived.
        /// </summary>
        public long BytesRead => Interlocked.Read(ref _bytesRead);

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
            var standardError = DrainErrorAsync(process, logger);

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
            // Lets the watchdog tell a decoder nobody is reading from apart from a stuck one
            Interlocked.Exchange(ref _readPendingSince, Stopwatch.GetTimestamp());

            try
            {
                var read = await _process.StandardOutput.BaseStream.ReadAtLeastAsync(
                    buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);

                if (read > 0 && Interlocked.Add(ref _bytesRead, read) == read)
                {
                    _logger.Debug("ffmpeg delivered its first frame after " +
                                  $"{Stopwatch.GetElapsedTime(_startedAt).TotalSeconds:0.#}s");
                }

                return read;
            }
            finally
            {
                Interlocked.Exchange(ref _readPendingSince, 0);
            }
        }

        /// <summary>
        /// Reports ffmpeg's diagnostics as they arrive. Waiting for it to exit would keep
        /// them hidden for exactly as long as a stream it cannot fetch keeps it busy.
        /// </summary>
        private static async Task<string> DrainErrorAsync(Process process, ILogger logger)
        {
            var builder = new StringBuilder();

            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                logger.Debug($"ffmpeg: {line}");

                if (builder.Length < ErrorOutputLimit)
                    builder.AppendLine(line);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Stops a decoder the player is stuck waiting on. A pipe read in flight cannot be
        /// interrupted, so the process has to go for the track to fail and the queue to
        /// move on.
        /// </summary>
        private void CheckForStall()
        {
            if (_disposed)
                return;

            var pendingSince = Interlocked.Read(ref _readPendingSince);

            // Nothing is waiting on ffmpeg, so a paused player is not read as a stall
            if (pendingSince == 0 || Stopwatch.GetElapsedTime(pendingSince) < ReadStallTimeout)
                return;

            _logger.Warning($"ffmpeg delivered nothing for {ReadStallTimeout.TotalSeconds:0}s and was stopped");
            ProcessRunner.Terminate(_process);
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

            await _watchdog.DisposeAsync();

            // Waits for a kill already in flight, without blocking the thread doing so
            await _registration.DisposeAsync();

            ProcessRunner.Terminate(_process);

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(timeout.Token);

                await _standardError.WaitAsync(timeout.Token);

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
