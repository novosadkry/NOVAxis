using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NOVAxis.Services.Video
{
    public class VideoDlWrapper
    {
        public async Task<string> Download(string url)
        {
            var args = new[]
            {
                "-s",
                "-q",
                "--no-warnings",
                "--print filename",
                "--restrict-filenames",
                url
            };

            var process = Process.Start("yt-dlp", args);
            var tcs = new TaskCompletionSource();

            process.Exited += (_, _) => tcs.TrySetResult();
            process.ErrorDataReceived += (_, _) => tcs.TrySetCanceled();

            await tcs.Task;

            return Guid.NewGuid().ToString();
        }
    }
}
