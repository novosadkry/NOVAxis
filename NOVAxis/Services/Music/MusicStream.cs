using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NOVAxis.Services.Music
{
    public sealed class MusicStream : IDisposable, IAsyncDisposable
    {
        private Task _stream;
        private readonly Process _ytDlp;
        private readonly Process _ffmpeg;

        public MusicStream(string url)
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

        public void Start(CancellationToken token = default)
        {
            _ytDlp.Start();
            _ffmpeg.Start();

            _stream = _ytDlp.StandardOutput.BaseStream
                .CopyToAsync(_ffmpeg.StandardInput.BaseStream, token)
                .ContinueWith(_ => _ffmpeg.StandardInput.Close(), token);
        }

        public async Task PipeToAsync(Stream stream, CancellationToken token = default)
        {
            var output = _ffmpeg.StandardOutput.BaseStream;
            try { await output.CopyToAsync(stream, token); }
            finally { await stream.FlushAsync(token); }
        }

        public void Dispose()
        {
            _ytDlp.Dispose();
            _ffmpeg.Dispose();
            _stream?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_stream != null) await CastAndDispose(_stream);
            if (_ytDlp != null) await CastAndDispose(_ytDlp);
            if (_ffmpeg != null) await CastAndDispose(_ffmpeg);

            return;

            static async ValueTask CastAndDispose(IDisposable resource)
            {
                if (resource is IAsyncDisposable resourceAsyncDisposable)
                    await resourceAsyncDisposable.DisposeAsync();
                else
                    resource.Dispose();
            }
        }
    }
}
