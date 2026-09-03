using System;
using System.Collections.Generic;

using Discord;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// Every embed and component the audio feature renders. Kept in one place so that
    /// both backends and the command module stay visually identical.
    /// </summary>
    public static class AudioEmbeds
    {
        private const int AccentR = 52, AccentG = 231, AccentB = 231;
        private const int ErrorR = 220, ErrorG = 20, ErrorB = 60;
        private const int WarningR = 255, WarningG = 150, WarningB = 0;

        public static readonly Emoji PlayingEmoji = new("\u25B6");
        public static readonly Emoji PausedEmoji = new("\u23F8");

        public static Embed Info(string title, IUser author = null)
        {
            var builder = new EmbedBuilder()
                .WithColor(AccentR, AccentG, AccentB)
                .WithTitle(title);

            if (author != null)
                builder.WithAuthor($"{author}", author.GetAvatarUrl());

            return builder.Build();
        }

        public static Embed Error(string title, string description)
        {
            return new EmbedBuilder()
                .WithColor(ErrorR, ErrorG, ErrorB)
                .WithDescription(description)
                .WithTitle(title)
                .Build();
        }

        /// <summary>
        /// Why a player could not be had, or null when it could. Lives here rather than in
        /// a module because more than one of them needs a player before it can do anything.
        /// </summary>
        public static Embed Retrieval(AudioPlayerRetrieveResult result)
        {
            switch (result.Status)
            {
                case AudioPlayerRetrieveStatus.Success:
                    return null;

                case AudioPlayerRetrieveStatus.UserNotInVoiceChannel:
                    return Error(
                        "Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci",
                        "(Neplatný kanál)");

                case AudioPlayerRetrieveStatus.VoiceChannelMismatch:
                    return Error(
                        "Pro komunikaci s jádrem musíš být naladěn na stejnou frekvenci",
                        "(Neplatný příkaz)");

                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.Paused or AudioPrecondition.NotPlaying:
                    return Warning(
                        "Stream audia již běží",
                        "(Neplatný příkaz)");

                case AudioPlayerRetrieveStatus.BotNotConnected:
                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.Playing:
                    return Warning(
                        "Právě teď není streamováno na serveru žádné audio",
                        "(Neplatný příkaz)");

                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.NotPaused:
                    return Warning(
                        "Stream audia již byl pozastaven",
                        "(Neplatný příkaz)");

                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.QueueNotEmpty:
                    return Warning(
                        "Právě teď se ve frontě nenachází žádná zvuková stopa",
                        "(Neplatný příkaz)");

                default:
                    return Error(
                        "Při komunikaci s jádrem nastala neznámá chyba",
                        "(Neznámá chyba)");
            }
        }

        public static Embed Warning(string title, string description)
        {
            return new EmbedBuilder()
                .WithColor(WarningR, WarningG, WarningB)
                .WithDescription(description)
                .WithTitle(title)
                .Build();
        }

        public static Embed TrackEnqueued(AudioTrackQueueItem item, int position)
        {
            var track = item.Track;

            return new EmbedBuilder()
                .WithColor(AccentR, AccentG, AccentB)
                .WithAuthor("Přidáno do fronty:")
                .WithTitle($"{track.Title}")
                .WithUrl(track.Uri?.AbsoluteUri)
                .WithThumbnailUrl(track.ArtworkUri?.AbsoluteUri)
                .AddField("Vyžádal:", Mention(item.RequestedBy))
                .AddField("Délka:", $"`{FormatDuration(track)}`", true)
                .AddField("Pořadí ve frontě:", $"`{position}.`", true)
                .Build();
        }

        public static Embed PlaylistEnqueued(AudioPlaylist playlist, AudioTrackQueueItem firstItem, int total, TimeSpan totalDuration)
        {
            var firstTrack = firstItem.Track;

            var uri = playlist?.Uri?.AbsoluteUri ?? firstTrack.Uri?.AbsoluteUri;
            var artworkUri = playlist?.ArtworkUri?.AbsoluteUri ?? firstTrack.ArtworkUri?.AbsoluteUri;

            return new EmbedBuilder()
                .WithColor(AccentR, AccentG, AccentB)
                .WithAuthor($"Přidáno do fronty ({total}):")
                .WithTitle($"{playlist?.Name ?? firstTrack.Title}")
                .WithUrl(uri)
                .WithThumbnailUrl(artworkUri)
                .AddField("Vyžádal:", Mention(firstItem.RequestedBy), true)
                .AddField("Délka:", $"`{totalDuration:hh\\:mm\\:ss}`", true)
                .Build();
        }

        public static Embed NowPlaying(AudioTrackQueueItem item, bool isPaused, float volume, int queueCount, TimeSpan? position = null)
        {
            var track = item.Track;
            var statusEmoji = isPaused ? PausedEmoji : PlayingEmoji;

            var duration = position != null
                ? $"`{position:hh\\:mm\\:ss}/{FormatDuration(track)}`"
                : $"`{FormatDuration(track)}`";

            var builder = new EmbedBuilder()
                .WithColor(AccentR, AccentG, AccentB)
                .WithAuthor("Právě přehrávám:")
                .WithTitle($"{track.Title}")
                .WithUrl(track.Uri?.AbsoluteUri)
                .WithThumbnailUrl(track.ArtworkUri?.AbsoluteUri)
                .AddField("Vyžádal:", Mention(item.RequestedBy))
                .AddField("Stav:", $"{statusEmoji}", true)
                .AddField("Hlasitost:", $"{volume * 100.0f}%", true)
                .AddField("Délka:", duration, true);

            // Only worth a field once something is waiting behind the track
            if (queueCount > 0)
                builder.AddField("Ve frontě:", $"`{queueCount}`", true);

            return builder.Build();
        }

        public static MessageComponent TrackControls(ulong interactionId, AudioTrack track, string webUrl = null)
        {
            var builder = new ComponentBuilder()
                .WithButton(customId: $"TrackControls_Remove,{interactionId}", emote: new Emoji("\u2716"), style: ButtonStyle.Danger)
                .WithButton(customId: $"TrackControls_Add,{track.Uri?.AbsoluteUri}", emote: new Emoji("\u2764"), style: ButtonStyle.Secondary)
                .WithButton(customId: "TrackControls_Add", emote: new Emoji("\u2795"), style: ButtonStyle.Success);

            if (webUrl != null)
                builder.WithButton(WebPlayerButton(webUrl));

            return builder.Build();
        }

        public static MessageComponent PlayerControls(string webUrl = null)
        {
            var builder = new ComponentBuilder()
                .WithButton(customId: "AudioControls_PlayPause", emote: new Emoji("\u23EF"))
                .WithButton(customId: "AudioControls_Stop", emote: new Emoji("\u23F9"))
                .WithButton(customId: "AudioControls_Skip", emote: new Emoji("\u23E9"))
                .WithButton(customId: "AudioControls_Repeat", emote: new Emoji("\uD83D\uDD01"))
                .WithButton(customId: "AudioControls_RepeatOnce", emote: new Emoji("\uD83D\uDD02"));

            // A row holds five buttons, so the link starts a second one
            if (webUrl != null)
                builder.WithButton(WebPlayerButton(webUrl), row: 1);

            return builder.Build();
        }

        /// <summary>
        /// A link button opening the guild's web player - the primary way of steering
        /// the playback, so it rides along on every control surface.
        /// </summary>
        public static ButtonBuilder WebPlayerButton(string webUrl)
        {
            return new ButtonBuilder()
                .WithLabel("Otev\u0159\u00EDt p\u0159ehr\u00E1va\u010D")
                .WithEmote(new Emoji("\uD83C\uDFA7"))
                .WithStyle(ButtonStyle.Link)
                .WithUrl(webUrl);
        }

        /// <summary>
        /// Tracks queued by the backend itself have no requester to mention.
        /// </summary>
        private static string Mention(IUser user)
        {
            return user?.Mention ?? "\u2014";
        }

        /// <summary>
        /// Live streams have no meaningful length, so they get a marker instead of zeroes.
        /// </summary>
        public static string FormatDuration(AudioTrack track)
        {
            return track.IsLiveStream || track.Duration <= TimeSpan.Zero
                ? "\u221E"
                : $"{track.Duration:hh\\:mm\\:ss}";
        }

        public static EmbedFieldBuilder QueueEntry(int position, AudioTrackQueueItem item)
        {
            var track = item.Track;

            return new EmbedFieldBuilder
            {
                Name = $"`{position}.` {track.Title}",
                Value = $"Vyžádal: {Mention(item.RequestedBy)} | Délka: `{FormatDuration(track)}` | [Odkaz]({track.Uri?.AbsoluteUri})\n"
            };
        }

        public static IEnumerable<EmbedFieldBuilder> QueueHeader(AudioTrackQueueItem currentItem, bool isPaused, int queuedCount)
        {
            if (currentItem != null)
            {
                var track = currentItem.Track;
                var statusEmoji = isPaused ? PausedEmoji : PlayingEmoji;

                yield return new EmbedFieldBuilder
                {
                    Name = $"{statusEmoji} **{track.Title}**",
                    Value = $"Vyžádal: {Mention(currentItem.RequestedBy)} | Délka: `{FormatDuration(track)}` | [Odkaz]({track.Uri?.AbsoluteUri})\n"
                };
            }

            yield return new EmbedFieldBuilder
            {
                Name = "\u200B",
                Value = $"**Stopy ve frontě ({queuedCount}):**"
            };
        }
    }
}
