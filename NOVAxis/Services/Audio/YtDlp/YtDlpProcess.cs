using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NOVAxis.Services.Audio.YtDlp
{
    public class ProcessException : Exception
    {
        public string FileName { get; }
        public int ExitCode { get; }
        public string StandardError { get; }

        public ProcessException(string fileName, int exitCode, string standardError)
            : base($"'{fileName}' exited with code {exitCode}: {Summarize(standardError)}")
        {
            FileName = fileName;
            ExitCode = exitCode;
            StandardError = standardError;
        }

        private static string Summarize(string standardError)
        {
            if (string.IsNullOrWhiteSpace(standardError))
                return "(no output)";

            var trimmed = standardError.Trim();

            return trimmed.Length > 500
                ? trimmed[..500] + "..."
                : trimmed;
        }
    }

    /// <summary>
    /// Runs a child process to completion and collects its output. Used for the short lived
    /// yt-dlp metadata lookups - streaming processes are managed by <see cref="FfmpegAudioStream"/>.
    /// </summary>
    public sealed record ProcessResult(string FileName, int ExitCode, string StandardOutput, string StandardError)
    {
        public bool IsSuccess => ExitCode == 0;

        public ProcessResult EnsureSuccess()
        {
            if (!IsSuccess)
                throw new ProcessException(FileName, ExitCode, StandardError);

            return this;
        }
    }

    public static class ProcessRunner
    {
        public static async Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                throw new ProcessException(fileName, -1, e.Message);
            }

            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);

            var standardOutput = process.StandardOutput.ReadToEndAsync(linkedSource.Token);
            var standardError = process.StandardError.ReadToEndAsync(linkedSource.Token);

            // The failure paths below never await those tasks, and an
            // unobserved cancellation surfaces later as a stray event
            Observe(standardOutput);
            Observe(standardError);

            try
            {
                await process.WaitForExitAsync(linkedSource.Token);
                await Task.WhenAll(standardOutput, standardError);
            }
            catch (OperationCanceledException)
            {
                Terminate(process);

                // A timeout is reported as a failure, an actual cancellation propagates
                cancellationToken.ThrowIfCancellationRequested();

                throw new ProcessException(fileName, -1,
                    $"The process did not finish within {timeout.TotalSeconds:0.#}s");
            }

            return new ProcessResult(fileName, process.ExitCode, await standardOutput, await standardError);
        }

        /// <summary>
        /// Marks a task's failure as seen. A faulted task nobody awaits raises
        /// <see cref="TaskScheduler.UnobservedTaskException"/> when it is collected, so
        /// tasks which are deliberately left behind say so here.
        /// </summary>
        internal static void Observe(Task task)
        {
            _ = task.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Kills a process and everything it spawned, ignoring the race
        /// with a process which exits on its own in the meantime.
        /// </summary>
        public static void Terminate(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already exited or never started */ }
            catch (NotSupportedException) { /* remote process */ }
            catch (SystemException) { /* the process is being torn down by the OS */ }
        }

        /// <summary>
        /// Builds a single line description of a command, for logging only.
        /// </summary>
        public static string Describe(string fileName, IEnumerable<string> arguments)
        {
            var builder = new StringBuilder(fileName);

            foreach (var argument in arguments)
                builder.Append(' ').Append(argument.Contains(' ') ? $"\"{argument}\"" : argument);

            return builder.ToString();
        }
    }
}
