using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Audio;
using Microsoft.Extensions.Logging;

namespace NOVAxis.Services.Music
{
    public class MusicService
    {
        private readonly ConcurrentQueue<Song> _queue = new();
        private CancellationTokenSource _playbackCts;
        private Task _playbackTask;
        private IAudioClient _audioClient;
        private MusicStream _musicStream;
        private AudioOutStream _audioOutStream;
        private ILogger<MusicService> _logger;

        public MusicService(ILogger<MusicService> logger)
        {
            _logger = logger;
        }

        public async Task JoinAsync(IVoiceChannel channel)
        {
            if (_audioClient == null)
            {
                _audioClient = await channel.ConnectAsync();
                _audioClient.Disconnected += async ex =>
                {
                    if (ex != null)
                        _logger.LogError("Audio client disconnected: {Message}", ex.Message);

                    await CleanupAsync();
                };
                _audioClient.Connected += () =>
                {
                    _logger.LogInformation("Connected to voice channel {ChannelName}", channel.Name);
                    StartPlayback();
                    return Task.CompletedTask;
                };
            }
        }

        public async Task<Song> SearchAsync(IUser user, string input)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"\"ytsearch1:{input}\" --no-playlist --print title,webpage_url",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to start yt-dlp process.");

            var title = await process.StandardOutput.ReadLineAsync();
            var url = await process.StandardOutput.ReadLineAsync();
            await process.WaitForExitAsync();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                return null;

            return new Song(url, title, user);
        }

        public void Enqueue(Song song)
        {
            _queue.Enqueue(song);
            StartPlayback();
        }

        public async Task SkipAsync()
        {
            await (_playbackCts?.CancelAsync() ?? Task.CompletedTask);
            await _playbackTask;
            StartPlayback();
        }

        public async Task StopAsync()
        {
            await (_playbackCts?.CancelAsync() ?? Task.CompletedTask);
            await _playbackTask;
            _queue.Clear();
            StartPlayback();
        }

        private void StartPlayback()
        {
            if (_audioClient == null)
                throw new InvalidOperationException("Bot is not connected to a voice channel.");

            if (_playbackTask is { IsCompleted: false })
                return; // Already playing

            _playbackCts = new CancellationTokenSource();
            _playbackTask = Task.Run(() => PlaybackLoopAsync(_playbackCts.Token), _playbackCts.Token);
        }

        private async Task CleanupAsync()
        {
            await (_playbackCts?.CancelAsync() ?? Task.CompletedTask);
            await (_audioOutStream?.DisposeAsync() ?? ValueTask.CompletedTask);
            await (_musicStream?.DisposeAsync() ?? ValueTask.CompletedTask);
            _audioClient?.Dispose();
            _audioClient = null;
            _playbackTask = null;
        }

        private async Task PlaybackLoopAsync(CancellationToken token)
        {
            while (_queue.TryDequeue(out var song))
            {
                try
                {
                    await StreamSongAsync(song, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task StreamSongAsync(Song song, CancellationToken token)
        {
            await (_audioOutStream?.DisposeAsync() ?? ValueTask.CompletedTask);
            _audioOutStream = _audioClient.CreatePCMStream(AudioApplication.Music);

            await (_musicStream?.DisposeAsync() ?? ValueTask.CompletedTask);
            _musicStream = new MusicStream(song.Url);
            _musicStream.Start(token);

            await _musicStream.PipeToAsync(_audioOutStream, token);
        }
    }
}
