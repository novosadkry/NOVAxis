using System.Threading;
using System.Threading.Tasks;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// Turns free-form user input - a search phrase or an URL - into playable tracks.
    /// </summary>
    public interface IAudioSearchService
    {
        ValueTask<AudioLoadResult> LoadAsync(string input, CancellationToken cancellationToken = default);
    }
}
