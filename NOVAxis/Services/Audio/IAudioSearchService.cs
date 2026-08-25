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

        /// <summary>
        /// Searches for up to <paramref name="limit"/> tracks matching <paramref name="query"/>,
        /// for surfaces which let the user pick one instead of taking the first hit.
        /// </summary>
        ValueTask<AudioLoadResult> SearchAsync(string query, int limit, CancellationToken cancellationToken = default);
    }
}
