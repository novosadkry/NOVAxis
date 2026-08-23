using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Lavalink4NET;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Integrations.Lavasrc;

namespace NOVAxis.Services.Audio.Lavalink
{
    public class LavalinkAudioSearchService : IAudioSearchService
    {
        private IAudioService AudioService { get; }

        public LavalinkAudioSearchService(IAudioService audioService)
        {
            AudioService = audioService;
        }

        public async ValueTask<AudioLoadResult> LoadAsync(string input, CancellationToken cancellationToken = default)
        {
            var options = new TrackLoadOptions(
                TrackSearchMode.YouTube,
                StrictSearchBehavior.Resolve);

            var result = await AudioService.Tracks
                .LoadTracksAsync(input, options, cancellationToken: cancellationToken);

            if (result.IsFailed)
                return AudioLoadResult.Failed;

            if (result.IsPlaylist)
            {
                var playlist = new ExtendedPlaylistInformation(result.Playlist!);

                return AudioLoadResult.FromPlaylist(
                    result.Tracks.Select(LavalinkAudioTrack.FromLavalink),
                    new AudioPlaylist
                    {
                        Name = playlist.Name,
                        Uri = playlist.Uri,
                        ArtworkUri = playlist.ArtworkUri,
                        TotalTracks = playlist.TotalTracks
                    });
            }

            return result.Track is null
                ? AudioLoadResult.Failed
                : AudioLoadResult.FromTrack(LavalinkAudioTrack.FromLavalink(result.Track));
        }
    }
}
