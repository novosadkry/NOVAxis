using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NOVAxis.Utilities;
using NOVAxis.Extensions;

using Discord;
using Victoria.Player;

namespace NOVAxis.Services.Audio
{
    public enum RepeatMode
    {
        None,
        Once,
        First,
        Queue
    }

    public class AudioContext : IDisposable
    {
        public AudioContext(AudioNode audioNode, ulong id)
        {
            _audioNode = audioNode;
            _disconnectCts = new CancellationTokenSource();

            GuildId = id;
        }

        private bool _disposed;
        private readonly AudioNode _audioNode;
        private CancellationTokenSource _disconnectCts;

        public LinkedQueue<AudioTrack> Queue { get; } = new();

        public AudioTrack Track => Queue.First();
        public AudioTrack LastTrack => Queue.Last();

        public RepeatMode Repeat { get; set; }
        public ulong GuildId { get; }

        public Task InitiateDisconnectAsync(AudioPlayer player, TimeSpan timeout)
        {
            // Renew cancelled token source
            if (_disconnectCts.IsCancellationRequested)
            {
                _disconnectCts.Dispose();
                _disconnectCts = new CancellationTokenSource();
            }

            // Pass cancellation token to thread
            var disconnectToken = _disconnectCts.Token;

            Task.Run(async () =>
            {
                // Leave channel if token isn't cancelled within the timeout
                if (!SpinWait.SpinUntil(() => disconnectToken.IsCancellationRequested, timeout))
                {
                    var userCount = await player.VoiceChannel.GetHumanUsers()
                        .CountAsync(cancellationToken: disconnectToken);

                    // and the context wasn't disposed and the player isn't playing
                    if (!_disposed && (player.PlayerState != PlayerState.Playing || userCount < 1))
                    {
                        await player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(52, 231, 231)
                            .WithTitle($"Odpojuji se od kanálu `{player.VoiceChannel.Name}`").Build());

                        await _audioNode.LeaveAsync(player.VoiceChannel);
                    }
                }
            }, disconnectToken);

            return Task.CompletedTask;
        }

        public Task CancelDisconnectAsync()
        {
            if (!_disconnectCts.IsCancellationRequested)
                _disconnectCts.Cancel();

            return Task.CompletedTask;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _disconnectCts.Cancel();
                _disconnectCts.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~AudioContext()
        {
            Dispose(false);
        }
    }
}