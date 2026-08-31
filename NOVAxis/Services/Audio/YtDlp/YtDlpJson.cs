using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Reads the info dictionaries yt-dlp emits with <c>-J</c>. Every field yt-dlp produces is
    /// optional and extractor dependent, so each lookup falls back to the next best key.
    /// </summary>
    public static class YtDlpJson
    {
        public static AudioLoadResult ReadLoadResult(string json, int maxPlaylistSize)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return AudioLoadResult.Failed;

            if (!IsPlaylist(root))
                return AudioLoadResult.FromTrack(ReadTrack(root));

            var entries = ReadEntries(root, maxPlaylistSize).ToList();

            if (entries.Count == 0)
                return AudioLoadResult.Failed;

            // A search is modelled as a playlist by yt-dlp, but the user asked for a track
            if (IsSearch(root))
                return AudioLoadResult.FromTrack(entries[0]);

            return AudioLoadResult.FromPlaylist(entries, new AudioPlaylist
            {
                Name = GetString(root, "title") ?? GetString(root, "id"),
                Uri = GetUri(root, "webpage_url", "original_url"),
                ArtworkUri = ReadArtwork(root) ?? entries[0].ArtworkUri,
                TotalTracks = GetInt(root, "playlist_count") ?? entries.Count
            });
        }

        /// <summary>
        /// Extracts the playable media URL and the headers yt-dlp expects it to be requested with.
        /// </summary>
        /// <summary>
        /// Reads what a download needs: the titling, and every rendition on offer with
        /// whatever yt-dlp could say about its size.
        /// </summary>
        public static YtDlpMediaInfo ReadMediaInfo(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // A link resolving to a playlist is not something a download can act on,
            // so the first entry stands in for it
            if (IsPlaylist(root) &&
                TryGet(root, "entries", out var entries) &&
                entries.ValueKind == JsonValueKind.Array &&
                entries.GetArrayLength() > 0)
                root = entries[0];

            var uri = GetUri(root, "webpage_url", "original_url", "url");
            var title = GetString(root, "title") ?? GetString(root, "fulltitle");

            if (uri == null && title == null)
                return null;

            var isLive = GetBool(root, "is_live") ??
                         GetString(root, "live_status") == "is_live";

            return new YtDlpMediaInfo(
                title ?? uri!.AbsoluteUri,
                uri,
                ReadArtwork(root),
                ReadDuration(root),
                isLive,
                GetString(root, "extractor_key") ?? GetString(root, "ie_key"),
                ReadFormats(root));
        }

        public static YtDlpStreamInfo ReadStreamInfo(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // yt-dlp reports the format it picked under requested_downloads
            if (TryGet(root, "requested_downloads", out var downloads) &&
                downloads.ValueKind == JsonValueKind.Array &&
                downloads.GetArrayLength() > 0)
            {
                var download = downloads[0];
                var downloadUrl = GetString(download, "url");

                if (!string.IsNullOrEmpty(downloadUrl))
                    return new YtDlpStreamInfo(downloadUrl, ReadHeaders(download));
            }

            var url = GetString(root, "url");

            return string.IsNullOrEmpty(url)
                ? null
                : new YtDlpStreamInfo(url, ReadHeaders(root));
        }

        private static bool IsPlaylist(JsonElement element)
        {
            return GetString(element, "_type") == "playlist" ||
                   TryGet(element, "entries", out var entries) &&
                   entries.ValueKind == JsonValueKind.Array;
        }

        private static bool IsSearch(JsonElement element)
        {
            var url = GetString(element, "webpage_url") ?? GetString(element, "original_url") ?? string.Empty;
            return url.Contains("search", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http");
        }

        private static IEnumerable<AudioTrack> ReadEntries(JsonElement root, int maxPlaylistSize)
        {
            if (!TryGet(root, "entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                yield break;

            var count = 0;

            foreach (var entry in entries.EnumerateArray())
            {
                if (count >= maxPlaylistSize)
                    yield break;

                // Unavailable entries (private, deleted, geo blocked) come through as null
                if (entry.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;

                // Nested playlists are flattened away
                if (IsPlaylist(entry))
                {
                    foreach (var nested in ReadEntries(entry, maxPlaylistSize - count))
                    {
                        count++;
                        yield return nested;
                    }

                    continue;
                }

                var track = ReadTrack(entry);
                if (track == null) continue;

                count++;
                yield return track;
            }
        }

        public static AudioTrack ReadTrack(JsonElement element)
        {
            var uri = GetUri(element, "webpage_url", "original_url", "url");
            var title = GetString(element, "title") ?? GetString(element, "fulltitle");

            if (uri == null && title == null)
                return null;

            var isLive = GetBool(element, "is_live") ??
                         GetString(element, "live_status") == "is_live";

            return new AudioTrack
            {
                Title = title ?? uri!.AbsoluteUri,
                Author = GetString(element, "uploader") ??
                         GetString(element, "channel") ??
                         GetString(element, "artist") ??
                         GetString(element, "uploader_id"),
                Uri = uri,
                ArtworkUri = ReadArtwork(element),
                Duration = ReadDuration(element),
                IsLiveStream = isLive,
                Identifier = GetString(element, "id"),
                SourceName = GetString(element, "extractor_key") ?? GetString(element, "ie_key")
            };
        }

        private static TimeSpan ReadDuration(JsonElement element)
        {
            if (TryGet(element, "duration", out var duration))
            {
                switch (duration.ValueKind)
                {
                    case JsonValueKind.Number when duration.TryGetDouble(out var seconds):
                        return TimeSpan.FromSeconds(seconds);

                    case JsonValueKind.String when double.TryParse(duration.GetString(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                        return TimeSpan.FromSeconds(parsed);
                }
            }

            return TimeSpan.Zero;
        }

        private static IReadOnlyList<YtDlpFormat> ReadFormats(JsonElement element)
        {
            if (!TryGet(element, "formats", out var formats) ||
                formats.ValueKind != JsonValueKind.Array)
                return Array.Empty<YtDlpFormat>();

            var results = new List<YtDlpFormat>(formats.GetArrayLength());

            foreach (var format in formats.EnumerateArray())
            {
                if (format.ValueKind != JsonValueKind.Object)
                    continue;

                var id = GetString(format, "format_id");

                if (string.IsNullOrEmpty(id))
                    continue;

                // Storyboards are images yt-dlp lists alongside the real renditions
                var ext = GetString(format, "ext");

                if (ext == "mhtml" || GetString(format, "format_note") == "storyboard")
                    continue;

                results.Add(new YtDlpFormat(
                    id,
                    ext,
                    ReadResolution(format),
                    GetString(format, "vcodec"),
                    GetString(format, "acodec"),
                    GetDouble(format, "fps"),
                    GetDouble(format, "tbr"),
                    ReadFormatSize(format),
                    GetString(format, "format_note")));
            }

            return results;
        }

        private static string ReadResolution(JsonElement element)
        {
            var resolution = GetString(element, "resolution");

            if (!string.IsNullOrEmpty(resolution) && resolution != "audio only")
                return resolution;

            var height = GetInt(element, "height");

            if (height is > 0)
                return $"{height}p";

            return resolution;
        }

        /// <summary>
        /// The exact size when yt-dlp knows it, the estimate otherwise, and null when it
        /// knows neither - which is normal for fragmented (HLS/DASH) renditions.
        /// </summary>
        private static long? ReadFormatSize(JsonElement element)
        {
            return GetLong(element, "filesize") ?? GetLong(element, "filesize_approx");
        }

        private static Uri ReadArtwork(JsonElement element)
        {
            var thumbnail = GetUri(element, "thumbnail");
            if (thumbnail != null) return thumbnail;

            if (!TryGet(element, "thumbnails", out var thumbnails) ||
                thumbnails.ValueKind != JsonValueKind.Array)
                return null;

            // yt-dlp orders thumbnails from worst to best
            Uri best = null;

            foreach (var candidate in thumbnails.EnumerateArray())
            {
                var uri = GetUri(candidate, "url");
                if (uri != null) best = uri;
            }

            return best;
        }

        private static IReadOnlyDictionary<string, string> ReadHeaders(JsonElement element)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (TryGet(element, "http_headers", out var element2) &&
                element2.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in element2.EnumerateObject())
                {
                    if (header.Value.ValueKind == JsonValueKind.String)
                        headers[header.Name] = header.Value.GetString();
                }
            }

            return headers;
        }

        /// <summary>
        /// Reads a property off an element which may not be an object at all. yt-dlp answers
        /// a failed extraction with a bare <c>null</c> document, and the raw TryGetProperty
        /// throws rather than saying no.
        /// </summary>
        private static bool TryGet(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
                return element.TryGetProperty(name, out value);

            value = default;
            return false;
        }

        private static string GetString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static int? GetInt(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt32(out var parsed)
                ? parsed
                : null;
        }

        private static long? GetLong(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt64(out var parsed)
                ? parsed
                : null;
        }

        private static double? GetDouble(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.Number &&
                   value.TryGetDouble(out var parsed)
                ? parsed
                : null;
        }

        private static bool? GetBool(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        private static Uri GetUri(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                var value = GetString(element, name);

                if (!string.IsNullOrEmpty(value) &&
                    Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "http" or "https")
                    return uri;
            }

            return null;
        }
    }
}
