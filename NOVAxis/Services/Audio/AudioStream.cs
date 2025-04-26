using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NOVAxis.Services.Audio
{
    public sealed class AudioStream : IDisposable
    {
        private Task _stream;
        private readonly Process _ytDlp;
        private readonly Process _ffmpeg;

        public AudioStream(string url)
        {
            _ytDlp = new Process();
            _ffmpeg = new Process();

            _ytDlp.StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"--quiet -f bestaudio -o - \"{url}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _ffmpeg.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-hide_banner -loglevel panic -i pipe:0 -ac 2 -f s16le -ar 48000 pipe:1",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        public void Start()
        {
            _ytDlp.Start();
            _ffmpeg.Start();

            _stream = _ytDlp.StandardOutput.BaseStream
                .CopyToAsync(_ffmpeg.StandardInput.BaseStream)
                .ContinueWith(_ => _ffmpeg.StandardInput.Close());
        }

        public void PipeTo(Stream stream)
        {
            _ = Task.Run(async () =>
            {
                var output = _ffmpeg.StandardOutput.BaseStream;

                try
                {
                    await output.CopyToAsync(stream);
                }
                finally
                {
                    await stream.FlushAsync();
                }
            });
        }

        public void Dispose()
        {
            _ytDlp.Kill();
            _ytDlp.Dispose();

            _ffmpeg.Kill();
            _ffmpeg.Dispose();

            _stream?.Dispose();
        }
    }
}
