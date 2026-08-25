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
        /// <summary>
        /// How long reading a page of one of the hosts below may take.
        /// </summary>
        public static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(15);

        private static readonly HttpClient Http = new()
        {
            Timeout = MetadataTimeout
        };

        private static readonly string[] MetadataOnlyHosts =
        {
            "spotify.com",
            "deezer.com",
            "music.apple.com",
            "tidal.com",
            "soundcloud.app.goo.gl"
        };

        /// <summary>
        /// Words describing the kind of a page rather than its performer.
        /// </summary>
        private static readonly string[] DescriptorWords =
        {
            "song", "album", "single", "ep", "playlist", "podcast", "episode", "track", "video"
        };

        private static readonly Regex TitleRegex = MetaRegex("og:title");
        private static readonly Regex DescriptionRegex = MetaRegex("og:description");

        private YtDlpClient Client { get; }
        private AudioSearchCache Results { get; }
        private ILogger<YtDlpAudioSearchService> Logger { get; }

        public YtDlpAudioSearchService(
            YtDlpClient client,
            AudioSearchCache results,
            ILogger<YtDlpAudioSearchService> logger)
        {
            Client = client;
            Results = results;
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

            var result = await LookUpAsync(input, cancellationToken);

            // A playlist can gain entries between two requests, a single track cannot
            if (!result.IsFailed && !result.IsPlaylist)
                Results[input] = result;

            return result;
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

            Logger.Debug($"Searching for {limit} results matching '{query}'");

            var result = await Client.LoadAsync(input, cancellationToken);

            if (!result.IsFailed)
                Results[input] = result;

            return result;
        }

        private async ValueTask<AudioLoadResult> LookUpAsync(string input, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                Logger.Debug($"Loading '{input}' as a YouTube search");
                return await Client.LoadAsync($"ytsearch1:{input}", cancellationToken);
            }

            if (!IsMetadataOnly(uri))
            {
                Logger.Debug($"Loading '{uri}' through yt-dlp");
                return await Client.LoadAsync(uri.AbsoluteUri, cancellationToken);
            }

            var query = await DescribeAsync(uri, cancellationToken);

            if (string.IsNullOrEmpty(query))
            {
                Logger.Warning($"Unable to derive a search query from '{uri}'");
                return AudioLoadResult.Failed;
            }

            Logger.Debug($"Resolved '{uri}' to a YouTube search for '{query}'");

            return await Client.LoadAsync($"ytsearch1:{query}", cancellationToken);
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

        private static bool IsMetadataOnly(Uri uri)
        {
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;

            return MetadataOnlyHosts.Any(x =>
                host.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{x}", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Reads the OpenGraph tags of a page and turns them into "title artist".
        /// </summary>
        private async Task<string> DescribeAsync(Uri uri, CancellationToken cancellationToken)
        {
            string html;

            try
            {
                html = await Http.GetStringAsync(uri, cancellationToken);
            }
            catch (HttpRequestException e)
            {
                Logger.Warning($"Unable to read metadata of '{uri}'", e);
                return null;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Warning($"Timed out while reading metadata of '{uri}'");
                return null;
            }

            var title = ReadMeta(TitleRegex, html);

            if (string.IsNullOrWhiteSpace(title))
                return null;

            var artist = ReadArtist(ReadMeta(DescriptionRegex, html));

            return string.IsNullOrWhiteSpace(artist)
                ? title
                : $"{title} {artist}";
        }

        private static string ReadMeta(Regex regex, string html)
        {
            var match = regex.Match(html);

            return match.Success
                ? WebUtility.HtmlDecode(match.Groups["content"].Value).Trim()
                : null;
        }

        /// <summary>
        /// Descriptions of those pages read like "Song · Artist · Album · 2019".
        /// </summary>
        private static string ReadArtist(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return null;

            var segments = description
                .Split(new[] { '·', '•', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var segment in segments)
            {
                if (DescriptorWords.Contains(segment.ToLowerInvariant()))
                    continue;

                // Release years carry no search value
                if (segment.Length == 4 && segment.All(char.IsDigit))
                    continue;

                return segment;
            }

            return null;
        }

        private static Regex MetaRegex(string property)
        {
            var pattern = "<meta[^>]+(?:property|name)\\s*=\\s*[\"']" + Regex.Escape(property) +
                          "[\"'][^>]+content\\s*=\\s*[\"'](?<content>[^\"']*)[\"']";

            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
