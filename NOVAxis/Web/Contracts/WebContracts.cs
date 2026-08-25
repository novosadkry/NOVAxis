using System;
using System.Collections.Generic;

using NOVAxis.Services.Audio;

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
}
