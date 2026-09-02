using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NOVAxis.Extensions;
using NOVAxis.Utilities;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Resolves user input through yt-dlp. Links to services yt-dlp cannot stream from
    /// (Spotify, Deezer, Apple Music, Tidal) are downgraded to a YouTube search built from
    /// the page's own metadata.
    ///
    /// Both surfaces come through here: playback asks what to play, a download asks what a
    /// link is and in which renditions. The two ask yt-dlp different questions - a flat
    /// listing against a full extraction - but everything around the question is shared,
    /// including the metadata-only detour a link nobody can stream from needs.
    /// </summary>
    public class YtDlpAudioSearchService : IAudioSearchService
    {
        /// <summary>How long reading a metadata-only page may take.</summary>
        public static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(15);

        private static readonly HttpClient Http = new() { Timeout = MetadataTimeout };

        /// <summary>Services which publish a page about the track but no media to read.</summary>
        private static readonly string[] MetadataOnlyHosts =
        {
            "spotify.com",
            "deezer.com",
            "music.apple.com",
            "tidal.com",
            "soundcloud.app.goo.gl"
        };

        /// <summary>Words describing the kind of a page rather than its performer.</summary>
        private static readonly string[] DescriptorWords =
        {
            "song", "album", "single", "ep", "playlist", "podcast", "episode", "track", "video"
        };

        private static readonly Regex TitleRegex = MetaRegex("og:title");
        private static readonly Regex DescriptionRegex = MetaRegex("og:description");

        private YtDlpClient Client { get; }
        private AudioSearchCache Results { get; }
        private AudioMediaCache Media { get; }
        private ILogger<YtDlpAudioSearchService> Logger { get; }

        private Coalescer<string, AudioLoadResult> Lookups { get; } = new();
        private Coalescer<string, AudioTrack> Inspections { get; } = new();

        public YtDlpAudioSearchService(
            YtDlpClient client,
            AudioSearchCache results,
            AudioMediaCache media,
            ILogger<YtDlpAudioSearchService> logger)
        {
            Client = client;
            Results = results;
            Media = media;
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

        /// <summary>
        /// What a link turns out to be, with every rendition it offers - the question a
        /// download asks. Coalesced and cached the same way a playback lookup is, so two
        /// people pasting the same link cost a single extractor run.
        /// </summary>
        public async ValueTask<AudioTrack> InspectAsync(string input, CancellationToken cancellationToken = default)
        {
            input = Sanitize(input);

            if (string.IsNullOrEmpty(input))
                return null;

            if (Media.TryGetValue(input, out var cached))
            {
                Logger.Debug($"Reusing the media already inspected for '{input}'");
                return cached;
            }

            return await Inspections.RunAsync(input, async token =>
            {
                // Checked again inside: the caller this one joined may have just stored it
                if (Media.TryGetValue(input, out var found))
                    return found;

                var target = await ResolveAsync(input, token);

                if (string.IsNullOrEmpty(target))
                    return null;

                var media = await Client.ProbeAsync(target, token);

                if (media != null)
                    Media[input] = media;

                return media;
            }, cancellationToken);
        }

        private async ValueTask<AudioLoadResult> LookUpAsync(string input, CancellationToken cancellationToken)
        {
            var target = await ResolveAsync(input, cancellationToken);

            return string.IsNullOrEmpty(target)
                ? AudioLoadResult.Failed
                : await Client.LoadAsync(target, cancellationToken);
        }

        /// <summary>
        /// What to actually hand yt-dlp. A phrase becomes a search, a link it can read is
        /// itself, and a link it cannot becomes a search for what the page says the track
        /// is. Null when the page said nothing worth searching for.
        /// </summary>
        private async ValueTask<string> ResolveAsync(string input, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                Logger.Debug($"Loading '{input}' as a YouTube search");
                return $"ytsearch1:{input}";
            }

            if (!IsMetadataOnly(uri))
            {
                Logger.Debug($"Loading '{uri}' through yt-dlp");
                return uri.AbsoluteUri;
            }

            var query = await DescribeAsync(uri, cancellationToken);

            if (string.IsNullOrEmpty(query))
            {
                Logger.Warning($"Unable to derive a search query from '{uri}'");
                return null;
            }

            Logger.Debug($"Resolved '{uri}' to a search for '{query}'");

            return $"ytsearch1:{query}";
        }

        /// <summary>
        /// Whether a host publishes a page about the track but nothing yt-dlp can stream -
        /// which is also nothing it can download.
        /// </summary>
        public static bool IsMetadataOnly(Uri uri)
        {
            if (uri == null)
                return false;

            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;

            return MetadataOnlyHosts.Any(x =>
                host.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{x}", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Reads the OpenGraph tags of a page and turns them into "title artist", or null
        /// when the page says nothing useful.
        /// </summary>
        private async ValueTask<string> DescribeAsync(Uri uri, CancellationToken cancellationToken)
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
