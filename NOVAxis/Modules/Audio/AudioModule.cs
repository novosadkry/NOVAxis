﻿using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Utilities;
using NOVAxis.Preconditions;
using NOVAxis.Services.Audio;

using Discord;
using Discord.Interactions;

using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Players.Preconditions;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Integrations.Lavasrc;

namespace NOVAxis.Modules.Audio
{
    [Cooldown(1)]
    [Group("audio", "Audio related commands")]
    [RequireContext(ContextType.Guild)]
    public class AudioModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public IAudioService AudioService { get; set; }
        public IOptions<AudioOptions> Options { get; set; }
        public InteractionCache InteractionCache { get; set; }

        #region Functions

        private Task<TrackLoadResult> SearchAsync(string input)
        {
            var searchMode = TrackSearchMode.YouTube;
            return SearchAsync(input, searchMode);
        }

        private async Task<TrackLoadResult> SearchAsync(
            string input,
            TrackSearchMode searchMode)
        {
            var options = new TrackLoadOptions(
                searchMode,
                StrictSearchBehavior.Resolve);

            return await AudioService.Tracks
                .LoadTracksAsync(input, options);
        }

        private async ValueTask<AudioPlayer> GetPlayerAsync(
            bool joinChannel = true,
            bool sameChannel = false,
            params IPlayerPrecondition[] preconditions)
        {
            var textChannel = Context.Channel as ITextChannel;

            var playerOptions = new AudioPlayerOptions
            {
                TextChannel = textChannel,
                // SelfDeaf = Options.Value.SelfDeaf,
                InitialVolume = 1.0f,
                DisconnectOnDestroy = true
            };

            var retrieveOptions = new PlayerRetrieveOptions(
                joinChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
                sameChannel ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore,
                Preconditions: ImmutableArray.Create(preconditions));

            var result = await AudioService.Players.RetrieveAsync<AudioPlayer, AudioPlayerOptions>(
                Context, AudioPlayer.CreatePlayerAsync, playerOptions, retrieveOptions);

            switch (result.Status)
            {
                case PlayerRetrieveStatus.Success:
                    return result.Player;

                case PlayerRetrieveStatus.UserNotInVoiceChannel:
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný kanál)")
                        .WithTitle("Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci")
                        .Build());
                    break;

                case PlayerRetrieveStatus.VoiceChannelMismatch:
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Pro komunikaci s jádrem musíš být naladěn na stejnou frekvenci")
                        .Build());
                    break;

                case PlayerRetrieveStatus.PreconditionFailed when result.Precondition == PlayerPrecondition.Paused:
                case PlayerRetrieveStatus.PreconditionFailed when result.Precondition == PlayerPrecondition.NotPlaying:
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Stream audia již běží")
                        .Build());
                    break;

                case PlayerRetrieveStatus.BotNotConnected:
                case PlayerRetrieveStatus.PreconditionFailed when result.Precondition == PlayerPrecondition.Playing:
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio")
                        .Build());
                    break;

                case PlayerRetrieveStatus.PreconditionFailed when result.Precondition == PlayerPrecondition.NotPaused:
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Stream audia již byl pozastaven")
                        .Build());
                    break;

                case PlayerRetrieveStatus.PreconditionFailed when result.Precondition == PlayerPrecondition.QueueNotEmpty:
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď se ve frontě nenachází žádná zvuková stopa")
                        .Build());
                    break;

                default:
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neznámá chyba)")
                        .WithTitle("Při komunikaci s jádrem nastala neznámá chyba")
                        .Build());
                    break;
            }

            return null;
        }

        #endregion

        #region Commands

        [SlashCommand("join", "Joins a voice channel")]
        public async Task CmdJoinChannel()
        {
            var player = await GetPlayerAsync(joinChannel: true);
            if (player == null) return;

            var voiceChannel = await player.GetVoiceChannel(Context.Client);

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Připojuji se ke kanálu `{voiceChannel.Name}`")
                .Build());
        }

        [SlashCommand("leave", "Leaves a voice channel")]
        public async Task CmdLeaveChannel()
        {
            var player = await GetPlayerAsync(joinChannel: false, sameChannel: true);
            if (player == null) return;

            var voiceChannel = await player.GetVoiceChannel(Context.Client);

            await player.DisconnectAsync();

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Odpojuji se od kanálu `{voiceChannel.Name}`")
                .Build());
        }

        [Cooldown(5)]
        [SlashCommand("play", "Plays an audio transmission")]
        public async Task CmdPlayAudio(string input)
        {
            var player = await GetPlayerAsync(joinChannel: true);
            if (player == null) return;

            await DeferAsync();

            try
            {
                var result = await SearchAsync(input);
                await PlayAudio(player, result);
            }

            catch (HttpRequestException)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní stream audia")
                    .Build());
            }

            catch (ArgumentNullException)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Mému jádru se nepodařilo v databázi nalézt požadovanou stopu")
                    .Build());
            }

            catch (Exception)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neznámá chyba)")
                    .WithTitle("Při komunikaci s jádrem nastala neznámá chyba")
                    .Build());

                throw;
            }
        }

        private async Task PlayAudio(AudioPlayer player, TrackLoadResult result)
        {
            if (result.IsFailed)
                throw new ArgumentNullException();

            if (result.IsPlaylist)
            {
                var items = result.Tracks
                    .Select(t => new AudioTrackQueueItem(new TrackReference(t))
                    {
                        RequestedBy = Context.User,
                        RequestId = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now)
                    })
                    .ToList();

                var firstItem = items.First();
                var firstTrack = firstItem.Track!;
                await player.Queue.AddRangeAsync(items);

                var totalDuration = new TimeSpan();
                foreach (var track in result.Tracks)
                    totalDuration += track.Duration;

                var playlist = new ExtendedPlaylistInformation(result.Playlist!);

                // TODO: LavaSrc doesn't return playlist information for YouTube playlists
                var total = playlist.TotalTracks ?? result.Tracks.Length;
                var uri = playlist.Uri?.AbsoluteUri ?? firstTrack.Uri?.AbsoluteUri;
                var artworkUri = playlist.ArtworkUri?.AbsoluteUri ?? firstTrack.ArtworkUri?.AbsoluteUri;

                await FollowupAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor($"Přidáno do fronty ({total}):")
                    .WithTitle($"{playlist.Name}")
                    .WithUrl(uri)
                    .WithThumbnailUrl(artworkUri)
                    .AddField("Vyžádal:", firstItem.RequestedBy.Mention, true)
                    .AddField("Délka:", $"`{totalDuration:hh\\:mm\\:ss}`", true)
                    .Build());
            }

            else
            {
                var track = result.Track;
                ArgumentNullException.ThrowIfNull(track);

                var item = new AudioTrackQueueItem(new TrackReference(track))
                {
                    RequestedBy = Context.User,
                    RequestId = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now)
                };

                await player.Queue.AddAsync(item);

                if (player.State == PlayerState.NotPlaying)
                {
                    await FollowupAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithAuthor("Přidáno do fronty:")
                        .WithTitle($"{track.Title}")
                        .WithUrl(track.Uri?.AbsoluteUri)
                        .WithThumbnailUrl(track.ArtworkUri?.AbsoluteUri)
                        .AddField("Vyžádal:", item.RequestedBy.Mention)
                        .AddField("Délka:", $"`{track.Duration:hh\\:mm\\:ss}`", true)
                        .AddField("Pořadí ve frontě:", $"`{player.Queue.Count}.`", true)
                        .Build());
                }

                else
                {
                    var id = InteractionCache.Store(item);

                    var embed = new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithAuthor("Přidáno do fronty:")
                        .WithTitle($"{track.Title}")
                        .WithUrl(track.Uri?.AbsoluteUri)
                        .WithThumbnailUrl(track.ArtworkUri?.AbsoluteUri)
                        .AddField("Vyžádal:", item.RequestedBy.Mention)
                        .AddField("Délka:", $"`{track.Duration:hh\\:mm\\:ss}`", true)
                        .AddField("Pořadí ve frontě:", $"`{player.Queue.Count}.`", true)
                        .Build();

                    var components = new ComponentBuilder()
                        .WithButton(customId: $"TrackControls_Remove,{id}", emote: new Emoji("\u2716"), style: ButtonStyle.Danger)
                        .WithButton(customId: $"TrackControls_Add,{track.Uri?.AbsoluteUri}", emote: new Emoji("\u2764"), style: ButtonStyle.Secondary)
                        .WithButton(customId: "TrackControls_Add", emote: new Emoji("\u2795"), style: ButtonStyle.Success)
                        .Build();

                    await FollowupAsync(embed: embed, components: components);
                }
            }

            if (player.State == PlayerState.NotPlaying)
                await player.SkipAsync();
        }

        [ComponentInteraction("AudioControls_*", true)]
        public async Task AudioControls(string action)
        {
            var player = await GetPlayerAsync(joinChannel: false, sameChannel: true);
            if (player == null) return;

            switch (action)
            {
                case "Skip":
                    await CmdSkipAudio();
                    break;
                case "Stop":
                    await CmdStopAudio();
                    break;
                case "Repeat":
                    await (player.RepeatMode != TrackRepeatMode.None
                        ? CmdRepeatAudio(TrackRepeatMode.None)
                        : CmdRepeatAudio(TrackRepeatMode.Queue));
                    break;
                case "RepeatOnce":
                    await (player.RepeatMode != TrackRepeatMode.None
                        ? CmdRepeatAudio(TrackRepeatMode.None)
                        : CmdRepeatAudio(TrackRepeatMode.Track));
                    break;
                case "PlayPause":
                    await (player.State == PlayerState.Playing
                        ? CmdPauseAudio()
                        : CmdResumeAudio());
                    break;
            }
        }

        [ComponentInteraction("TrackControls_Add", true)]
        public async Task TrackControls_Add()
        {
            await RespondWithModalAsync<TrackControlsAddModal>(nameof(TrackControls_AddModal));
        }

        [ComponentInteraction("TrackControls_Add,*", true)]
        public async Task TrackControls_Add(string trackUrl)
        {
            var player = await GetPlayerAsync(joinChannel: true);
            if (player == null) return;

            await DeferAsync();

            try
            {
                var result = await SearchAsync(trackUrl);
                await PlayAudio(player, result);
            }

            catch (HttpRequestException)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní stream audia")
                    .Build());
            }

            catch (ArgumentNullException)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Mému jádru se nepodařilo v databázi nalézt požadovanou stopu")
                    .Build());
            }

            catch (Exception)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neznámá chyba)")
                    .WithTitle("Při komunikaci s jádrem nastala neznámá chyba")
                    .Build());

                throw;
            }
        }

        [ComponentInteraction("TrackControls_Remove,*", true)]
        public async Task TrackControls_Remove(ulong interactionId)
        {
            var player = await GetPlayerAsync(joinChannel: false, sameChannel: true);
            if (player == null) return;

            var currentItem = (AudioTrackQueueItem) player.CurrentItem;

            if (InteractionCache[interactionId] is not AudioTrackQueueItem cachedItem)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Vypršel časový limit)")
                    .WithTitle("Mé jádro přerušilo čekání na lidský vstup")
                    .Build());

                return;
            }

            if (currentItem != null && currentItem.RequestId == cachedItem.RequestId)
            {
                await CmdSkipAudio();
                return;
            }

            if (!player.Queue.Contains(cachedItem))
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Požadovaná stopa se ve frontě nenachází").Build());

                return;
            }

            await player.Queue.RemoveAsync(cachedItem);

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Požadovaná stopa byla úspěšně odebrána z fronty")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());
        }

        public class TrackControlsAddModal : IModal
        {
            public string Title => "Přidání skladby do fronty";

            [InputLabel("Zadejte název nebo URL adresu skladby")]
            [ModalTextInput("input", placeholder: "https://www.youtube.com/watch?v=...")]
            public string Input { get; set; }
        }

        [ModalInteraction(nameof(TrackControls_AddModal), true)]
        public async Task TrackControls_AddModal(TrackControlsAddModal modal)
        {
            if (!string.IsNullOrWhiteSpace(modal.Input))
                await CmdPlayAudio(modal.Input);
            else
                await RespondAsync($"{new Emoji("\uD83E\uDD13")}", ephemeral: true);
        }

        [SlashCommand("skip", "Skips to the next audio transmission")]
        public async Task CmdSkipAudio(int count = 1)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.Playing);

            if (player == null) return;

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Stream audia byl úspěšně přeskočen")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());

            if (count > 1)
                await player.Queue.RemoveRangeAsync(0, count - 1);

            await player.SkipAsync();
        }

        [SlashCommand("stop", "Stops the audio transmission")]
        public async Task CmdStopAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                PlayerPrecondition.Playing);

            if (player == null) return;

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Stream audia byl úspěšně zastaven")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());

            await player.StopAsync();
        }

        [SlashCommand("clear", "Clears the audio queue contents")]
        public async Task CmdClearAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                PlayerPrecondition.QueueNotEmpty);

            if (player == null) return;

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Fronta audia byla úspěšně promazána")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());

            await player.Queue.ClearAsync();
        }

        [SlashCommand("pause", "Pauses the audio transmission")]
        public async Task CmdPauseAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.NotPaused);

            if (player == null) return;

            await player.PauseAsync();

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Stream audia byl úspěšně pozastaven")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());
        }

        [SlashCommand("resume", "Resumes the audio transmission")]
        public async Task CmdResumeAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.Paused);

            if (player == null) return;

            await player.ResumeAsync();

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Stream audia byl úspěšně obnoven")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());
        }

        [SlashCommand("seek", "Seeks a position in the audio transmissions")]
        public async Task CmdSeekAudio(TimeSpan time)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.Playing);

            if (player == null) return;

            var currentTrack = player.CurrentTrack!;

            if (time > currentTrack.Duration)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Nelze nastavit hodnotu přesahující maximální délku stopy")
                    .Build());

                return;
            }

            if (time < TimeSpan.Zero)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Nelze nastavit zápornou hodnotu")
                    .Build());

                return;
            }

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Pozice audia byla úspěšně nastavena na `{time:hh\\:mm\\:ss}`")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());

            await player.SeekAsync(time);
        }

        [SlashCommand("forward", "Forwards to a position in the audio transmissions")]
        public async Task CmdForwardAudio(TimeSpan time)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.Playing);

            if (player == null) return;

            var currentTrack = player.CurrentTrack!;
            var trackPosition = player.Position!.Value.Position;
            
            if (time <= TimeSpan.Zero)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Nelze posunout o zápornou nebo nulovou hodnotu")
                    .Build());

                return;
            }

            var newTime = trackPosition + time;

            if (newTime > currentTrack.Duration)
                newTime = currentTrack.Duration;

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());

            await player.SeekAsync(newTime);
        }

        [SlashCommand("backward", "Backwards to a position in the audio transmissions")]
        public async Task CmdBackwardAudio(TimeSpan time)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.Playing);

            if (player == null) return;

            var trackPosition = player.Position!.Value.Position;
            
            if (time <= TimeSpan.Zero)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Nelze posunout o zápornou nebo nulovou hodnotu")
                    .Build());

                return;
            }

            var newTime = trackPosition - time;

            if (newTime < TimeSpan.Zero)
                newTime = TimeSpan.Zero;

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());

            await player.SeekAsync(newTime);
        }

        [SlashCommand("volume", "Sets a volume of the audio transmissions")]
        public async Task CmdAudioVolume(ushort percentage)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.Playing);

            if (player == null) return;

            if (percentage > 150)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Mé jádro nepodporuje hlasitost vyšší než 150%")
                    .Build());

                return;
            }

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Hlasitost audia byla úspěšně nastavena na {percentage}%")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());

            await player.SetVolumeAsync(percentage * 0.01f);
        }

        [SlashCommand("status", "Shows active audio transmissions")]
        public async Task CmdAudioStatus()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: false, 
                PlayerPrecondition.Playing);

            if (player == null) return;

            var item = (AudioTrackQueueItem) player.CurrentItem!;
            var track = item.Track!;
            
            var statusEmoji = !player.IsPaused
                ? new Emoji("\u25B6") // Playing
                : new Emoji("\u23F8"); // Paused

            var position = player.Position.GetValueOrDefault().Position;

            var embed = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithAuthor("Právě přehrávám:")
                .WithTitle($"{track.Title}")
                .WithUrl(track.Uri?.AbsoluteUri)
                .WithThumbnailUrl(track.ArtworkUri?.AbsoluteUri)
                .AddField("Vyžádal:", item.RequestedBy.Mention)
                .AddField("Stav:", $"{statusEmoji}", true)
                .AddField("Hlasitost:", $"{player.Volume * 100.0f}%", true)
                .AddField("Délka:", $"`{position:hh\\:mm\\:ss}/{track.Duration:hh\\:mm\\:ss}`", true)
                .Build();

            var components = new ComponentBuilder()
                .WithButton(customId: "AudioControls_PlayPause", emote: new Emoji("\u23EF"))
                .WithButton(customId: "AudioControls_Stop", emote: new Emoji("\u23F9"))
                .WithButton(customId: "AudioControls_Skip", emote: new Emoji("\u23E9"))
                .WithButton(customId: "AudioControls_Repeat", emote: new Emoji("\uD83D\uDD01"))
                .WithButton(customId: "AudioControls_RepeatOnce", emote: new Emoji("\uD83D\uDD02"))
                .Build();

            await RespondAsync(embed: embed, components: components);
        }

        [SlashCommand("queue", "Shows enqueued audio transmissions")]
        public async Task CmdAudioQueue()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: false, 
                PlayerPrecondition.QueueNotEmpty);

            if (player == null) return;

            var paginator = new AudioQueuePaginator(5);
            var totalDuration = TimeSpan.Zero;
            var statusEmoji = !player.IsPaused
                ? new Emoji("\u25B6") // Playing
                : new Emoji("\u23F8"); // Paused

            var header = new List<EmbedFieldBuilder>();
            var tracks = new List<EmbedFieldBuilder>();
            var footer = new List<EmbedFieldBuilder>();

            int position = 0;
            using var queueEnumerator = player.Queue.GetEnumerator();
            
            while (queueEnumerator.MoveNext())
            {
                position++;
                
                var current = queueEnumerator.Current;
                
                var item = (AudioTrackQueueItem) current!;
                var track = item.Track!;

                var mention = item.RequestedBy.Mention;
                var duration = track.Duration;
                var url = track.Uri?.AbsoluteUri;
                
                if (position == 0)
                {
                    header.Add(new EmbedFieldBuilder
                    {
                        Name = $"{statusEmoji} **{track.Title}**",
                        Value = $"Vyžádal: {mention} | Délka: `{duration:hh\\:mm\\:ss}` | [Odkaz]({url})\n"
                    });

                    header.Add(new EmbedFieldBuilder
                    {
                        Name = "\u200B",
                        Value = $"**Stopy ve frontě ({player.Queue.Count - 1}):**"
                    });
                }

                else
                {
                    tracks.Add(new EmbedFieldBuilder
                    {
                        Name = $"`{position}.` {track.Title}",
                        Value = $"Vyžádal: {mention} | Délka: `{duration:hh\\:mm\\:ss}` | [Odkaz]({url})\n"
                    });
                }

                totalDuration += track.Duration;
            }

            footer.Add(new EmbedFieldBuilder
            {
                Name = "\u200B",
                Value = $"Celková doba poslechu: `{totalDuration:hh\\:mm\\:ss}`"
            });

            var page = paginator
                .WithHeader(header)
                .WithTracks(tracks)
                .WithFooter(footer);

            var id = InteractionCache.Store(page);

            await DeferAsync();
            await CmdAudioQueue_Page(id, 0, "next");
        }

        [ComponentInteraction("CmdAudioQueue_Page_*,*,*", true)]
        public async Task CmdAudioQueue_Page(ulong id, int page, string action)
        {
            Action<MessageProperties> modifyAction;

            if (InteractionCache[id] is AudioQueuePaginator paginator)
            {
                if (page > paginator.MaxPageIndex || page < 0)
                    throw new InvalidOperationException("Invalid page index");

                modifyAction = m =>
                {
                    m.Embed = paginator.Build(page);
                    m.Components = new ComponentBuilder()
                        .WithButton(
                            customId: $"CmdAudioQueue_Page_{id},{0},min",
                            emote: new Emoji("\u23EE"),
                            style: page - 1 > 0
                                ? ButtonStyle.Primary
                                : ButtonStyle.Secondary,
                            disabled: page <= 0)
                        .WithButton(
                            customId: $"CmdAudioQueue_Page_{id},{page - 1},prev",
                            emote: new Emoji("\u25C0"),
                            style: page > 0
                                ? ButtonStyle.Primary
                                : ButtonStyle.Secondary,
                            disabled: page <= 0)
                        .WithButton(
                            customId: $"CmdAudioQueue_Page_{id},{page + 1},next",
                            emote: new Emoji("\u25B6"),
                            style: page < paginator.MaxPageIndex
                                ? ButtonStyle.Primary
                                : ButtonStyle.Secondary,
                            disabled: page >= paginator.MaxPageIndex)
                        .WithButton(
                            customId: $"CmdAudioQueue_Page_{id},{paginator.MaxPageIndex},max",
                            emote: new Emoji("\u23ED"),
                            style: page + 1 < paginator.MaxPageIndex
                                ? ButtonStyle.Primary
                                : ButtonStyle.Secondary,
                            disabled: page + 1 >= paginator.MaxPageIndex)
                        .Build();
                };
            }

            else
            {
                var embed = new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Vypršel časový limit)")
                    .WithTitle("Mé jádro přerušilo čekání na lidský vstup")
                    .Build();

                modifyAction = m =>
                {
                    m.Embed = embed;
                    m.Components = null;
                };
            }

            switch (Context.Interaction)
            {
                case IComponentInteraction component:
                    await component.UpdateAsync(modifyAction);
                    break;

                case IDiscordInteraction interaction:
                    await interaction.ModifyOriginalResponseAsync(modifyAction);
                    break;
            }
        }

        [SlashCommand("remove", "Removes an enqueued audio transmission")]
        public async Task CmdRemoveAudio(int index)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.QueueNotEmpty);

            if (player == null) return;

            if (index <= 0 || index >= player.Queue.Count)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatná pozice)")
                    .WithTitle("Požadovaná stopa se ve frontě nenachází")
                    .Build());

                return;
            }

            await player.Queue.RemoveAtAsync(index);

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Požadovaná stopa byla úspěšně odebrána z fronty")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());
        }

        [SlashCommand("repeat", "Repeats enqueued audio transmission")]
        public async Task CmdRepeatAudio(TrackRepeatMode mode)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true, 
                PlayerPrecondition.Playing);

            if (player == null) return;

            if (player.RepeatMode != mode && mode != TrackRepeatMode.None)
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Nadcházející stopy nyní porušují časové kontinuum")
                    .WithDescription("(Režim opakování byl zapnut)")
                    .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                    .Build());

                player.RepeatMode = mode;
            }

            else
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Nadcházející stopy nyní dodržují časové kontinuum")
                    .WithDescription("(Režim opakování byl vypnut)")
                    .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                    .Build());

                player.RepeatMode = TrackRepeatMode.None;
            }
        }

        [SlashCommand("tts", "Plays text to speech audio transmission")]
        public async Task CmdTextToSpeech(string text)
        {
            await DeferAsync();

            var player = await GetPlayerAsync(joinChannel: true);
            if (player == null) return;

            var input = Uri.EscapeDataString(text);
            var uri = $"ftts://{input}";

            var options = new TrackLoadOptions(
                SearchMode: TrackSearchMode.None,
                SearchBehavior: StrictSearchBehavior.Passthrough);

            var track = await AudioService.Tracks
                .LoadTrackAsync(uri, options);

            if (track == null)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Mému jádru se nepodařilo v databázi nalézt požadovanou stopu")
                    .Build());

                return;
            }

            var item = new AudioTrackQueueItem(new TrackReference(track))
            {
                RequestedBy = Context.User,
                RequestId = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now)
            };

            await player.PlayAsync(item, false);

            await FollowupAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Přehrávám text-to-speech")
                .WithAuthor($"{Context.User}", Context.User.GetAvatarUrl())
                .Build());
        }

        /*
        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("setdj", "Sets the guild's DJ role which is used to identify eligible users")]
        public async Task CmdSetDjRole(IRole newRole = null)
        {
            var guildInfo = await GuildDbContext.Get(Context) ??
                            await GuildDbContext.Create(Context.Guild);

            var currentRole = guildInfo.Roles.Find(x => x.Name == "DJ");

            if (newRole != null)
            {
                if (currentRole != null)
                {
                    GuildDbContext.GuildRoles.Remove(currentRole);
                    await GuildDbContext.SaveChangesAsync();
                }

                GuildDbContext.GuildRoles.Add(new GuildRole
                {
                    Name = "DJ",
                    Id = newRole.Id,
                    Guild = guildInfo,
                });

                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithDescription($"(Nastavena role {newRole.Mention})")
                    .WithTitle("Konfigurace mého jádra proběhla úspešně")
                    .Build());
            }

            else
            {
                if (currentRole != null)
                {
                    GuildDbContext.GuildRoles.Remove(currentRole);
                    await GuildDbContext.SaveChangesAsync();
                }

                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithDescription("(Nastavená role zrušena)")
                    .WithTitle("Konfigurace mého jádra proběhla úspešně")
                    .Build());
            }

            await GuildDbContext.SaveChangesAsync();
        }
        */

        #endregion
    }
}
