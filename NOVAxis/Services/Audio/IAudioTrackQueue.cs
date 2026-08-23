using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// An ordered collection of tracks waiting to be played. The queue never contains
    /// the track which is currently playing - that one is exposed as
    /// <see cref="IAudioPlayer.CurrentItem"/>.
    /// </summary>
    public interface IAudioTrackQueue : IReadOnlyList<AudioTrackQueueItem>
    {
        bool Contains(AudioTrackQueueItem item);

        ValueTask AddAsync(AudioTrackQueueItem item, CancellationToken cancellationToken = default);
        ValueTask AddRangeAsync(IEnumerable<AudioTrackQueueItem> items, CancellationToken cancellationToken = default);
        ValueTask<bool> RemoveAsync(AudioTrackQueueItem item, CancellationToken cancellationToken = default);
        ValueTask RemoveAtAsync(int index, CancellationToken cancellationToken = default);
        ValueTask RemoveRangeAsync(int index, int count, CancellationToken cancellationToken = default);
        ValueTask ClearAsync(CancellationToken cancellationToken = default);
    }
}
