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
            _disconnectTokenSource = new CancellationTokenSource();

            GuildId = id;
        }

        private bool _disposed;
        private readonly LavaNode _lavaNodeInstance;
        private CancellationTokenSource _disconnectTokenSource;

        public LinkedQueue<AudioTrack> Queue { get; set; } = new LinkedQueue<AudioTrack>();

        public AudioTrack Track => Queue.First();
        public AudioTrack LastTrack => Queue.Last();

        public RepeatMode Repeat { get; set; }
        public ulong GuildId { get; }

        public async Task InitiateDisconnectAsync(LavaPlayer player, TimeSpan timeout)
        {
            await Task.Run(() =>
            {
                if (_disconnectTokenSource.IsCancellationRequested)
                {
                    _disconnectTokenSource.Dispose();
                    _disconnectTokenSource = new CancellationTokenSource();
                }

                // Leave channel if token isn't cancelled within the timeout
                if (!SpinWait.SpinUntil(() => _disconnectTokenSource.IsCancellationRequested, timeout))
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
            });
        }

        public Task CancelDisconnectAsync()
        {
            if (!_disconnectTokenSource.IsCancellationRequested)
                _disconnectTokenSource.Cancel();

            return Task.CompletedTask;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                Queue.Clear();
                Repeat = RepeatMode.None;
                _disconnectTokenSource.Dispose();
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