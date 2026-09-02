using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NOVAxis.Extensions;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Links to services yt-dlp cannot read the media of - Spotify and its kind. The page
    /// still says what the track is, so it is read for a search phrase and the work is
    /// handed to a service which can. Shared, because a link nobody can stream from is one
    /// nobody can download either.
    /// </summary>
    public class MetadataOnlyLinks
    {
        /// <summary>How long reading one of those pages may take.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private static readonly HttpClient Http = new() { Timeout = Timeout };

        private static readonly string[] Hosts =
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

        private ILogger<MetadataOnlyLinks> Logger { get; }

        public MetadataOnlyLinks(ILogger<MetadataOnlyLinks> logger)
        {
            Logger = logger;
        }

        public static bool Covers(Uri uri)
        {
            if (uri == null)
                return false;

            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;

            return Hosts.Any(x =>
                host.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{x}", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Reads the OpenGraph tags of a page and turns them into "title artist", or null
        /// when the page says nothing useful.
        /// </summary>
        public async Task<string> DescribeAsync(Uri uri, CancellationToken cancellationToken = default)
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

        /// <summary>
        /// The search which stands in for a link, or null when the page gave nothing to
        /// search for. Anything else is handed back untouched.
        /// </summary>
        public async Task<string> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            if (!Covers(uri))
                return uri?.AbsoluteUri;

            var query = await DescribeAsync(uri, cancellationToken);

            if (string.IsNullOrEmpty(query))
            {
                Logger.Warning($"Unable to derive a search query from '{uri}'");
                return null;
            }

            Logger.Debug($"Resolved '{uri}' to a search for '{query}'");

            return $"ytsearch1:{query}";
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
