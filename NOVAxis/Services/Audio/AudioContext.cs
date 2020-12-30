using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Victoria;

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
        private readonly object _disposeLock = new object();

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
                if (!SpinWait.SpinUntil(() => _disconnectTokenSource.Token.IsCancellationRequested, timeout))
                    _lavaNodeInstance.LeaveAsync(player.VoiceChannel);
            });
        }

        public Task CancelDisconnectAsync()
        {
            if (!_disconnectTokenSource.IsCancellationRequested)
                _disconnectTokenSource.Cancel();

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                    return;

                Queue.Clear();
                Repeat = RepeatMode.None;
                _disconnectTokenSource.Dispose();

                _disposed = true;
            }
        }

        ~AudioContext()
        {
            Dispose();
        }
    }
}