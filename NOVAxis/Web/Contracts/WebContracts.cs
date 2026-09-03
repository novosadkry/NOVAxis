using System;
using System.Collections.Generic;
using System.Linq;

using NOVAxis.Database.Playlists;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Polls;
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
        IReadOnlyList<QueueItemDto> Queue,
        SkipVoteDto SkipVote)
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
            Array.Empty<QueueItemDto>(),
            null);
    }

    /// <summary>
    /// The guild's open skip vote. Who voted travels rather than whether "you" did: the
    /// snapshot goes to a whole group over one push, so it cannot be about any one viewer.
    /// </summary>
    public record SkipVoteDto(
        string Id,
        string Title,
        int InFavour,
        int Needed,
        int Listeners,
        IReadOnlyList<string> FavourIds,
        IReadOnlyList<string> AgainstIds)
    {
        public static SkipVoteDto FromVote(SkipVote vote)
        {
            if (vote == null)
                return null;

            return new SkipVoteDto(
                vote.Id.ToString(),
                vote.Track?.Title,
                vote.InFavour,
                vote.Needed,
                vote.Listeners,
                Who(vote, SkipVote.Yes),
                Who(vote, SkipVote.No));
        }

        private static IReadOnlyList<string> Who(SkipVote vote, int choice)
        {
            return vote.Votes
                .Where(x => x.Value == choice)
                .Select(x => x.Key.Id.ToString())
                .ToList();
        }
    }

    public record GuildDto(string Id, string Name, string IconUrl, bool Connected);

    public record PlayRequest(string Query);
    public record SkipRequest(int Count = 1);
    public record SeekRequest(double PositionMs);
    public record VolumeRequest(int Percent);
    public record RepeatRequest(string Mode);
    public record MoveRequest(int ToIndex);

    public record PlayResponse(int Enqueued, TrackDto Track, string PlaylistName);

    public record PlaylistTrackDto(
        string Title,
        string Author,
        string Uri,
        string ArtworkUri,
        long DurationMs)
    {
        public static PlaylistTrackDto FromTrack(PlaylistTrack track)
        {
            return new PlaylistTrackDto(
                track.Title,
                track.Author,
                track.Url,
                track.ArtworkUrl,
                track.DurationMs);
        }
    }

    public record PlaylistDto(
        string Id,
        string Name,
        string OwnerId,
        string OwnerName,
        bool Mine,
        bool Shared,
        int TrackCount,
        long TotalMs,
        long UpdatedAt,
        IReadOnlyList<PlaylistTrackDto> Tracks)
    {
        /// <summary>
        /// <paramref name="tracks"/> decides whether the contents travel: a listing of
        /// twenty playlists has no use for every track in each of them.
        /// </summary>
        public static PlaylistDto FromPlaylist(Playlist playlist, ulong viewerId, bool tracks)
        {
            return new PlaylistDto(
                playlist.Id.ToString(),
                playlist.Name,
                playlist.OwnerId.ToString(),
                playlist.OwnerName,
                playlist.OwnerId == viewerId,
                playlist.GuildId != null,
                playlist.Tracks.Count,
                playlist.Tracks.Sum(x => x.DurationMs),
                new DateTimeOffset(DateTime.SpecifyKind(playlist.UpdatedAt, DateTimeKind.Utc))
                    .ToUnixTimeMilliseconds(),
                tracks
                    ? playlist.Tracks.Select(PlaylistTrackDto.FromTrack).ToList()
                    : []);
        }
    }

    public record SavePlaylistRequest(string Name, string GuildId, bool Share);
    public record LoadPlaylistRequest(string GuildId, bool Replace);
    public record SharePlaylistRequest(string GuildId, bool Shared);

    public record DownloadFormatDto(
        string Id,
        string Kind,
        string Label,
        string Extension,
        long? SizeBytes,
        bool WithinLimit,

        /// <summary>
        /// True where the size is what the bitrate implies rather than what the source
        /// reported, so the page can show it as an approximation.
        /// </summary>
        bool Estimated)
    {
        public static DownloadFormatDto FromChoice(DownloadChoice choice) => new(
            choice.Id,
            choice.Kind.ToString(),
            choice.Label,
            choice.Extension,
            choice.Size,
            choice.WithinLimit,
            choice.Estimated);
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
        string Error,

        /// <summary>
        /// Older links of the caller's retired to fit this one in, so the page can say
        /// what went rather than leaving them to find a link that stopped working.
        /// </summary>
        IReadOnlyList<string> Freed)
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
                record.Error,
                record.Freed);
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

    /// <summary>
    /// What one person is holding: the links themselves, how much of their space those
    /// take, and how many more downloads the hour allows.
    /// </summary>
    public record DownloadOverviewDto(
        IReadOnlyList<DownloadDto> Downloads,
        DownloadStorageDto Storage,
        DownloadQuotaDto Quota);

    public record DownloadStorageDto(long UsedBytes, long LimitBytes)
    {
        public static DownloadStorageDto From((long Used, long Limit) storage)
            => new(storage.Used, storage.Limit);
    }

    /// <summary>
    /// Title is what the caller already calls it, and spares a lookup when no format is
    /// named. It is only ever shown back to the person who sent it.
    /// </summary>
    public record DownloadRequest(string Url, string Kind, string FormatId, string Title);
}
