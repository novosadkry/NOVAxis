using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;

namespace NOVAxis.Services.Audio.Lavalink
{
    /// <summary>
    /// Projects Lavalink4NET's queue onto <see cref="IAudioTrackQueue"/>.
    /// </summary>
    internal sealed class LavalinkAudioTrackQueue : IAudioTrackQueue
    {
        private readonly ITrackQueue _queue;

        public LavalinkAudioTrackQueue(ITrackQueue queue)
        {
            _queue = queue;
        }

        public int Count => _queue.Count;

        public AudioTrackQueueItem this[int index] => LavalinkTrackQueueItem.Unwrap(_queue[index]);

        public bool Contains(AudioTrackQueueItem item)
        {
            return IndexOf(item) >= 0;
        }

        public async ValueTask AddAsync(AudioTrackQueueItem item, CancellationToken cancellationToken = default)
        {
            await _queue.AddAsync(new LavalinkTrackQueueItem(item), cancellationToken);
        }

        public async ValueTask AddRangeAsync(IEnumerable<AudioTrackQueueItem> items, CancellationToken cancellationToken = default)
        {
            var wrapped = LavalinkTrackQueueItem.Wrap(items).ToList();
            await _queue.AddRangeAsync(wrapped, cancellationToken);
        }

        public async ValueTask<bool> RemoveAsync(AudioTrackQueueItem item, CancellationToken cancellationToken = default)
        {
            var index = IndexOf(item);
            if (index < 0) return false;

            return await _queue.RemoveAtAsync(index, cancellationToken);
        }

        public async ValueTask RemoveAtAsync(int index, CancellationToken cancellationToken = default)
        {
            await _queue.RemoveAtAsync(index, cancellationToken);
        }

        public ValueTask RemoveRangeAsync(int index, int count, CancellationToken cancellationToken = default)
        {
            return _queue.RemoveRangeAsync(index, count, cancellationToken);
        }

        public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            await _queue.ClearAsync(cancellationToken);
        }

        public IEnumerator<AudioTrackQueueItem> GetEnumerator()
        {
            return _queue.Select(LavalinkTrackQueueItem.Unwrap).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private int IndexOf(AudioTrackQueueItem item)
        {
            return _queue.IndexOf((ITrackQueueItem x) =>
                x is LavalinkTrackQueueItem wrapper &&
                wrapper.Item.RequestId == item.RequestId);
        }
    }
}
