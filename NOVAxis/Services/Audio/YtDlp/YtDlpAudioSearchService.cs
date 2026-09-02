using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

using Microsoft.Extensions.Logging;

using NOVAxis.Extensions;
using NOVAxis.Utilities;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Resolves user input through yt-dlp. Links to services yt-dlp cannot stream from
    /// (Spotify, Deezer, Apple Music, Tidal) are downgraded to a YouTube search built from
    /// the page's own metadata.
    /// </summary>
    public class YtDlpAudioSearchService : IAudioSearchService
    {
        private YtDlpClient Client { get; }
        private AudioSearchCache Results { get; }
        private MetadataOnlyLinks Metadata { get; }
        private ILogger<YtDlpAudioSearchService> Logger { get; }

        private Coalescer<string, AudioLoadResult> Lookups { get; } = new();

        public YtDlpAudioSearchService(
            YtDlpClient client,
            AudioSearchCache results,
            MetadataOnlyLinks metadata,
            ILogger<YtDlpAudioSearchService> logger)
        {
            Client = client;
            Results = results;
            Metadata = metadata;
            Logger = logger;
        }

        public async ValueTask<AudioLoadResult> LoadAsync(string input, CancellationToken cancellationToken = default)
        {
            input = Sanitize(input);

            if (string.IsNullOrEmpty(input))
                return AudioLoadResult.Failed;

            if (Results.TryGetValue(input, out var cached))
            {
                Logger.Debug($"Reusing the track already loaded for '{input}'");
                return cached;
            }

            return await Lookups.RunAsync(input, async token =>
            {
                if (Results.TryGetValue(input, out var found))
                    return found;

                var result = await LookUpAsync(input, token);

                // A playlist can gain entries between two requests, a single track cannot
                if (!result.IsFailed && !result.IsPlaylist)
                    Results[input] = result;

                return result;
            }, cancellationToken);
        }

        public async ValueTask<AudioLoadResult> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
        {
            query = Sanitize(query);

            if (string.IsNullOrEmpty(query) || limit < 1)
                return AudioLoadResult.Failed;

            // A pasted link has exactly one right answer, so let the lookup handle it
            if (Uri.TryCreate(query, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return await LoadAsync(query, cancellationToken);

            // The limit is part of the key - a single hit must not stand in for ten
            var input = $"ytsearch{limit}:{query}";

            if (Results.TryGetValue(input, out var cached))
            {
                Logger.Debug($"Reusing the results already loaded for '{input}'");
                return cached;
            }

            return await Lookups.RunAsync(input, async token =>
            {
                if (Results.TryGetValue(input, out var found))
                    return found;

                Logger.Debug($"Searching for {limit} results matching '{query}'");

                var result = await Client.LoadAsync(input, token);

                if (!result.IsFailed)
                    Results[input] = result;

                return result;
            }, cancellationToken);
        }

        private async ValueTask<AudioLoadResult> LookUpAsync(string input, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                Logger.Debug($"Loading '{input}' as a YouTube search");
                return await Client.LoadAsync($"ytsearch1:{input}", cancellationToken);
            }

            if (!MetadataOnlyLinks.Covers(uri))
            {
                Logger.Debug($"Loading '{uri}' through yt-dlp");
                return await Client.LoadAsync(uri.AbsoluteUri, cancellationToken);
            }

            var resolved = await Metadata.ResolveAsync(uri, cancellationToken);

            return string.IsNullOrEmpty(resolved)
                ? AudioLoadResult.Failed
                : await Client.LoadAsync(resolved, cancellationToken);
        }

        /// <summary>
        /// Discord wraps links in angle brackets to suppress embeds - those must not reach yt-dlp.
        /// </summary>
        private static string Sanitize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            input = input.Trim();

            if (input.Length > 2 && input[0] == '<' && input[^1] == '>')
                input = input[1..^1].Trim();

            return input;
        }

    }
}
