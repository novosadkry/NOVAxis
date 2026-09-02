using System;
using System.Collections.Generic;

using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Services.Download;

using Discord;

namespace NOVAxis.Web.Contracts
{
    /// <summary>
    /// Shapes going over the wire to the web player. Snowflakes travel as strings,
    /// because they do not survive JavaScript's number precision.
    /// </summary>
    public record TrackDto(
        string Title,
        string Author,
        string Uri,
        string ArtworkUri,
        double DurationMs,
        bool IsLiveStream,
        string SourceName)
    {
        public static TrackDto FromTrack(AudioTrack track) => new(
            track.Title,
            track.Author,
            track.Uri?.AbsoluteUri,
            track.ArtworkUri?.AbsoluteUri,
            track.Duration.TotalMilliseconds,
            track.IsLiveStream,
            track.SourceName);
    }

    public record WebUserDto(string Id, string Name, string AvatarUrl)
    {
        public static WebUserDto FromUser(IUser user) => user == null ? null : new WebUserDto(
            user.Id.ToString(),
            user.GlobalName ?? user.Username,
            user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());
    }

    public record QueueItemDto(string RequestId, TrackDto Track, WebUserDto RequestedBy)
    {
        public static QueueItemDto FromItem(AudioTrackQueueItem item) => item == null ? null : new QueueItemDto(
            item.RequestId.ToString(),
            TrackDto.FromTrack(item.Track),
            WebUserDto.FromUser(item.RequestedBy));
    }

    public record VoiceChannelDto(string Id, string Name);

    public record PlayerStateDto(
        string GuildId,
        bool Connected,
        string State,
        bool IsPaused,
        float Volume,
        string RepeatMode,
        double PositionMs,
        long SampledAt,
        VoiceChannelDto VoiceChannel,
        QueueItemDto Current,
        IReadOnlyList<QueueItemDto> Queue)
    {
        public static PlayerStateDto Disconnected(ulong guildId) => new(
            guildId.ToString(),
            false,
            nameof(AudioPlayerState.Destroyed),
            false,
            1f,
            nameof(AudioRepeatMode.None),
            0,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            null,
            null,
            Array.Empty<QueueItemDto>());
    }

    public record GuildDto(string Id, string Name, string IconUrl, bool Connected);

    public record PlayRequest(string Query);
    public record SkipRequest(int Count = 1);
    public record SeekRequest(double PositionMs);
    public record VolumeRequest(int Percent);
    public record RepeatRequest(string Mode);
    public record MoveRequest(int ToIndex);

    public record PlayResponse(int Enqueued, TrackDto Track, string PlaylistName);

    public record DownloadFormatDto(
        string Id,
        string Kind,
        string Label,
        string Extension,
        long? SizeBytes,
        bool WithinLimit)
    {
        public static DownloadFormatDto FromChoice(DownloadChoice choice) => new(
            choice.Id,
            choice.Kind.ToString(),
            choice.Label,
            choice.Extension,
            choice.Size,
            choice.WithinLimit);
    }

    public record DownloadProbeDto(
        string Url,
        string Title,
        string ThumbnailUrl,
        double DurationMs,
        bool IsLiveStream,
        IReadOnlyList<DownloadFormatDto> Formats);

    public record DownloadQuotaDto(int Limit, int Remaining, long? ResetsAt)
    {
        public static DownloadQuotaDto FromQuota(DownloadQuota quota) => new(
            quota.Limit,
            quota.Remaining,
            quota.ResetsAt?.ToUnixTimeMilliseconds());
    }

    public record DownloadDto(
        string Id,
        string State,
        string Kind,
        string Title,
        string SourceUrl,
        string FormatLabel,
        string FileName,
        long? SizeBytes,
        long ReceivedBytes,
        double? Progress,
        long CreatedAt,
        long ExpiresAt,
        long SampledAt,
        string FileUrl,
        string Error)
    {
        public static DownloadDto FromRecord(DownloadRecord record)
        {
            if (record == null)
                return null;

            var ready = record.State == DownloadState.Ready;
            var total = record.Size > 0 ? record.Size : record.EstimatedSize;

            return new DownloadDto(
                record.Id.ToString(),
                record.State.ToString(),
                record.Kind.ToString(),
                record.Title,
                record.SourceUrl,
                record.FormatLabel,
                ready && record.FilePath != null ? System.IO.Path.GetFileName(record.FilePath) : null,
                total,
                record.Received,
                Ratio(record.Received, total),
                record.CreatedAt.ToUnixTimeMilliseconds(),
                record.ExpiresAt.ToUnixTimeMilliseconds(),

                // The countdown is drawn against this, never against the browser's own idea
                // of now - the two clocks are not the same one
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                ready ? $"/api/downloads/{record.Id}/file" : null,
                record.Error);
        }

        private static double? Ratio(long received, long? total)
        {
            if (total is not > 0)
                return null;

            var ratio = (double)received / total.Value;

            return ratio switch
            {
                < 0 => 0,
                > 1 => 1,
                _ => ratio
            };
        }
    }

    public record DownloadOverviewDto(DownloadDto Active, DownloadQuotaDto Quota);

    /// <summary>
    /// Title is what the caller already calls it, and spares a lookup when no format is
    /// named. It is only ever shown back to the person who sent it.
    /// </summary>
    public record DownloadRequest(string Url, string Kind, string FormatId, string Title);
}
