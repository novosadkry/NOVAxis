using System;
using System.Collections.Generic;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// One downloadable rendition of a piece of media, as yt-dlp reports it. Most fields
    /// are extractor dependent and routinely absent - a null size in particular means
    /// "unknown", never "empty", and is the case the size watchdog exists for.
    /// </summary>
    public sealed record YtDlpFormat(
        string Id,
        string Ext,
        string Resolution,
        string VideoCodec,
        string AudioCodec,
        double? Fps,
        double? Bitrate,
        long? Size,
        string Note)
    {
        // Absent is not the same as "none": the generic extractor reports neither codec for
        // a plain file, and treating that as "no video" would drop every direct link
        public bool HasVideo => VideoCodec != "none";
        public bool HasAudio => AudioCodec != "none";

        /// <summary>
        /// A short human label - "1080p mp4 60fps". The size is left out: callers render
        /// it themselves, because they know whether it is an estimate or a total.
        /// </summary>
        public string Describe()
        {
            var parts = new List<string>(4);

            if (!string.IsNullOrEmpty(Resolution))
                parts.Add(Resolution);

            if (!string.IsNullOrEmpty(Ext))
                parts.Add(Ext);

            if (Fps is >= 1)
                parts.Add($"{Fps.Value:0}fps");

            if (parts.Count == 0 && Bitrate is > 0)
                parts.Add($"{Bitrate.Value:0} kbps");

            if (parts.Count == 0)
                parts.Add(Id ?? "?");

            return string.Join(' ', parts);
        }
    }

    /// <summary>
    /// What a download needs to know about a link before committing to fetching it.
    /// </summary>
    public sealed record YtDlpMediaInfo(
        string Title,
        Uri Url,
        Uri Thumbnail,
        TimeSpan Duration,
        bool IsLive,
        string Extractor,
        IReadOnlyList<YtDlpFormat> Formats);
}
