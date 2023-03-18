using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Discord;

using Victoria;
using Victoria.Enums;

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
        public AudioContext(LavaNode lavaNode, ulong id)
        {
            _lavaNodeInstance = lavaNode;
            _disconnectCts = new CancellationTokenSource();

            GuildId = id;
        }

        private bool _disposed;
        private readonly LavaNode _lavaNodeInstance;
        private CancellationTokenSource _disconnectCts;

        public LinkedQueue<AudioTrack> Queue { get; } = new();

        public AudioTrack Track => Queue.First();
        public AudioTrack LastTrack => Queue.Last();

        public RepeatMode Repeat { get; set; }
        public ulong GuildId { get; }

        public Task InitiateDisconnectAsync(LavaPlayer player, TimeSpan timeout)
        {
            // Renew cancelled token source
            if (_disconnectCts.IsCancellationRequested)
            {
                _disconnectCts.Dispose();
                _disconnectCts = new CancellationTokenSource();
            }

            // Pass cancellation token to thread
            var disconnectToken = _disconnectCts.Token;

            Task.Run(() =>
            {
                // Leave channel if token isn't cancelled within the timeout
                if (!SpinWait.SpinUntil(() => disconnectToken.IsCancellationRequested, timeout))
                {
                    // and the context wasn't disposed and the player isn't playing
                    if (!_disposed && player.PlayerState != PlayerState.Playing)
                    {
                        player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(52, 231, 231)
                            .WithTitle($"Odpojuji se od kanálu `{player.VoiceChannel.Name}`").Build());

                        _lavaNodeInstance.LeaveAsync(player.VoiceChannel);
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