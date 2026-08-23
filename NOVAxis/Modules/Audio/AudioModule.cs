using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NOVAxis.Preconditions;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Utilities;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Modules.Audio
{
    [Cooldown(1)]
    [Group("audio", "Audio related commands")]
    [RequireContext(ContextType.Guild)]
    public class AudioModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public IAudioPlayerManager PlayerManager { get; set; }
        public IAudioSearchService SearchService { get; set; }
        public InteractionCache InteractionCache { get; set; }

        #region Functions

        /// <summary>
        /// Answers an interaction which may or may not have been deferred already.
        /// </summary>
        private async Task AnswerAsync(Embed embed)
        {
            if (Context.Interaction.HasResponded)
                await FollowupAsync(ephemeral: true, embed: embed);
            else
                await RespondAsync(ephemeral: true, embed: embed);
        }

        private async ValueTask<IAudioPlayer> GetPlayerAsync(
            bool joinChannel = true,
            bool sameChannel = false,
            params AudioPrecondition[] preconditions)
        {
            var options = new AudioPlayerRetrieveOptions
            {
                JoinChannel = joinChannel,
                RequireSameChannel = sameChannel,
                Preconditions = preconditions
            };

            var result = await PlayerManager.RetrieveAsync(Context, options);

            switch (result.Status)
            {
                case AudioPlayerRetrieveStatus.Success:
                    return result.Player;

                case AudioPlayerRetrieveStatus.UserNotInVoiceChannel:
                    await AnswerAsync(AudioEmbeds.Error(
                        "Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci",
                        "(Neplatný kanál)"));
                    break;

                case AudioPlayerRetrieveStatus.VoiceChannelMismatch:
                    await AnswerAsync(AudioEmbeds.Error(
                        "Pro komunikaci s jádrem musíš být naladěn na stejnou frekvenci",
                        "(Neplatný příkaz)"));
                    break;

                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.Paused or AudioPrecondition.NotPlaying:
                    await AnswerAsync(AudioEmbeds.Warning(
                        "Stream audia již běží",
                        "(Neplatný příkaz)"));
                    break;

                case AudioPlayerRetrieveStatus.BotNotConnected:
                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.Playing:
                    await AnswerAsync(AudioEmbeds.Warning(
                        "Právě teď není streamováno na serveru žádné audio",
                        "(Neplatný příkaz)"));
                    break;

                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.NotPaused:
                    await AnswerAsync(AudioEmbeds.Warning(
                        "Stream audia již byl pozastaven",
                        "(Neplatný příkaz)"));
                    break;

                case AudioPlayerRetrieveStatus.PreconditionFailed when result.Precondition is AudioPrecondition.QueueNotEmpty:
                    await AnswerAsync(AudioEmbeds.Warning(
                        "Právě teď se ve frontě nenachází žádná zvuková stopa",
                        "(Neplatný příkaz)"));
                    break;

                default:
                    await AnswerAsync(AudioEmbeds.Error(
                        "Při komunikaci s jádrem nastala neznámá chyba",
                        "(Neznámá chyba)"));
                    break;
            }

            return null;
        }

        private AudioTrackQueueItem CreateItem(AudioTrack track)
        {
            return new AudioTrackQueueItem
            {
                Track = track,
                RequestedBy = Context.User,
                RequestId = AudioTrackQueueItem.NextRequestId()
            };
        }

        /// <summary>
        /// Runs a search and hands the outcome over to <see cref="PlayAudio"/>, mapping every
        /// way the lookup can go wrong onto a message the user can act on.
        /// </summary>
        private async Task SearchAndPlay(IAudioPlayer player, string input)
        {
            try
            {
                var result = await SearchService.LoadAsync(input);

                if (result.IsFailed)
                {
                    await FollowupAsync(ephemeral: true, embed: AudioEmbeds.Error(
                        "Mému jádru se nepodařilo v databázi nalézt požadovanou stopu",
                        "(Neplatný argument)"));

                    return;
                }

                await PlayAudio(player, result);
            }

            // yt-dlp reports unavailable or geo blocked media through its exit code,
            // which is an everyday outcome rather than a fault worth rethrowing
            catch (ProcessException)
            {
                await FollowupAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Mé jádro pravě nemůže poskytnout stabilní stream audia",
                    "(Služba není dostupná)"));
            }
            catch (HttpRequestException)
            {
                await FollowupAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Mé jádro pravě nemůže poskytnout stabilní stream audia",
                    "(Služba není dostupná)"));
            }
            catch (Exception)
            {
                await FollowupAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Při komunikaci s jádrem nastala neznámá chyba",
                    "(Neznámá chyba)"));

                throw;
            }
        }

        /// <summary>
        /// Hands the loaded tracks to the player. Everything goes in through
        /// <see cref="IAudioPlayer.PlayAsync"/>, which starts a track when nothing is
        /// playing and enqueues it otherwise, so both backends behave the same way.
        /// An idle player announces the track on its own, so the reply is dropped
        /// there rather than repeating it.
        /// </summary>
        private async Task PlayAudio(IAudioPlayer player, AudioLoadResult result)
        {
            // Read before enqueueing, as an idle player dequeues at once
            var wasIdle = player.State == AudioPlayerState.NotPlaying;

            if (result.IsPlaylist)
            {
                var items = result.Tracks.Select(CreateItem).ToList();

                await player.PlayAsync(items[0]);

                if (items.Count > 1)
                    await player.Queue.AddRangeAsync(items.Skip(1).ToList());

                if (wasIdle)
                {
                    await DeleteOriginalResponseAsync();
                    return;
                }

                var totalDuration = result.Tracks.Aggregate(
                    TimeSpan.Zero, (total, track) => total + track.Duration);

                await FollowupAsync(embed: AudioEmbeds.PlaylistEnqueued(
                    result.Playlist, items[0], result.Playlist?.TotalTracks ?? items.Count, totalDuration));
            }

            else
            {
                var item = CreateItem(result.Track);
                var position = player.Queue.Count + 1;

                await player.PlayAsync(item);

                if (wasIdle)
                {
                    await DeleteOriginalResponseAsync();
                    return;
                }

                var id = InteractionCache.Store(item);

                await FollowupAsync(
                    embed: AudioEmbeds.TrackEnqueued(item, position),
                    components: AudioEmbeds.TrackControls(id, item.Track));
            }
        }

        #endregion

        #region Commands

        [SlashCommand("join", "Joins a voice channel")]
        public async Task CmdJoinChannel()
        {
            await DeferAsync();

            var player = await GetPlayerAsync(joinChannel: true);
            if (player == null) return;

            var voiceChannel = await player.GetVoiceChannel(Context.Client);

            await FollowupAsync(embed: AudioEmbeds.Info($"Připojuji se ke kanálu `{voiceChannel.Name}`"));
        }

        [SlashCommand("leave", "Leaves a voice channel")]
        public async Task CmdLeaveChannel()
        {
            var player = await GetPlayerAsync(joinChannel: false, sameChannel: true);
            if (player == null) return;

            var voiceChannel = await player.GetVoiceChannel(Context.Client);

            await player.DisconnectAsync();

            await RespondAsync(embed: AudioEmbeds.Info($"Odpojuji se od kanálu `{voiceChannel.Name}`"));
        }

        [Cooldown(5)]
        [SlashCommand("play", "Plays an audio transmission")]
        public async Task CmdPlayAudio(string input)
        {
            // Connecting to voice outlasts the acknowledgement window Discord allows
            await DeferAsync();

            var player = await GetPlayerAsync(joinChannel: true);
            if (player == null) return;

            await SearchAndPlay(player, input);
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
                    await (player.RepeatMode != AudioRepeatMode.None
                        ? CmdRepeatAudio(AudioRepeatMode.None)
                        : CmdRepeatAudio(AudioRepeatMode.Queue));
                    break;
                case "RepeatOnce":
                    await (player.RepeatMode != AudioRepeatMode.None
                        ? CmdRepeatAudio(AudioRepeatMode.None)
                        : CmdRepeatAudio(AudioRepeatMode.Track));
                    break;
                case "PlayPause":
                    await (player.State == AudioPlayerState.Playing
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
            await DeferAsync();

            var player = await GetPlayerAsync(joinChannel: true);
            if (player == null) return;

            await SearchAndPlay(player, trackUrl);
        }

        [ComponentInteraction("TrackControls_Remove,*", true)]
        public async Task TrackControls_Remove(ulong interactionId)
        {
            var player = await GetPlayerAsync(joinChannel: false, sameChannel: true);
            if (player == null) return;

            if (InteractionCache[interactionId] is not AudioTrackQueueItem cachedItem)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Mé jádro přerušilo čekání na lidský vstup",
                    "(Vypršel časový limit)"));

                return;
            }

            var currentItem = player.CurrentItem;

            if (currentItem != null && currentItem.RequestId == cachedItem.RequestId)
            {
                await CmdSkipAudio();
                return;
            }

            if (!player.Queue.Contains(cachedItem))
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Požadovaná stopa se ve frontě nenachází",
                    "(Neplatný příkaz)"));

                return;
            }

            await player.Queue.RemoveAsync(cachedItem);

            await RespondAsync(embed: AudioEmbeds.Info(
                "Požadovaná stopa byla úspěšně odebrána z fronty", Context.User));
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
                AudioPrecondition.Playing);

            if (player == null) return;

            await RespondAsync(embed: AudioEmbeds.Info(
                "Stream audia byl úspěšně přeskočen", Context.User));

            await player.SkipAsync(count);
        }

        [SlashCommand("stop", "Stops the audio transmission")]
        public async Task CmdStopAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.Playing);

            if (player == null) return;

            await RespondAsync(embed: AudioEmbeds.Info(
                "Stream audia byl úspěšně zastaven", Context.User));

            await player.StopAsync();
        }

        [SlashCommand("clear", "Clears the audio queue contents")]
        public async Task CmdClearAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.QueueNotEmpty);

            if (player == null) return;

            await RespondAsync(embed: AudioEmbeds.Info(
                "Fronta audia byla úspěšně promazána", Context.User));

            await player.Queue.ClearAsync();
        }

        [SlashCommand("pause", "Pauses the audio transmission")]
        public async Task CmdPauseAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.NotPaused);

            if (player == null) return;

            await player.PauseAsync();

            await RespondAsync(embed: AudioEmbeds.Info(
                "Stream audia byl úspěšně pozastaven", Context.User));
        }

        [SlashCommand("resume", "Resumes the audio transmission")]
        public async Task CmdResumeAudio()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.Paused);

            if (player == null) return;

            await player.ResumeAsync();

            await RespondAsync(embed: AudioEmbeds.Info(
                "Stream audia byl úspěšně obnoven", Context.User));
        }

        [SlashCommand("seek", "Seeks a position in the audio transmissions")]
        public async Task CmdSeekAudio(TimeSpan time)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.Playing);

            if (player == null) return;

            var currentTrack = player.CurrentTrack!;

            if (!IsSeekable(currentTrack))
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "V živém přenosu nelze měnit pozici",
                    "(Neplatný příkaz)"));

                return;
            }

            if (time > currentTrack.Duration)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Nelze nastavit hodnotu přesahující maximální délku stopy",
                    "(Neplatný argument)"));

                return;
            }

            if (time < TimeSpan.Zero)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Nelze nastavit zápornou hodnotu",
                    "(Neplatný argument)"));

                return;
            }

            await RespondAsync(embed: AudioEmbeds.Info(
                $"Pozice audia byla úspěšně nastavena na `{time:hh\\:mm\\:ss}`", Context.User));

            await player.SeekAsync(time);
        }

        [SlashCommand("forward", "Forwards to a position in the audio transmissions")]
        public async Task CmdForwardAudio(TimeSpan time)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.Playing);

            if (player == null) return;

            var currentTrack = player.CurrentTrack!;

            if (!IsSeekable(currentTrack))
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "V živém přenosu nelze měnit pozici",
                    "(Neplatný příkaz)"));

                return;
            }

            if (time <= TimeSpan.Zero)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Nelze posunout o zápornou nebo nulovou hodnotu",
                    "(Neplatný argument)"));

                return;
            }

            var newTime = player.Position + time;

            if (newTime > currentTrack.Duration)
                newTime = currentTrack.Duration;

            await RespondAsync(embed: AudioEmbeds.Info(
                $"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`", Context.User));

            await player.SeekAsync(newTime);
        }

        [SlashCommand("backward", "Backwards to a position in the audio transmissions")]
        public async Task CmdBackwardAudio(TimeSpan time)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.Playing);

            if (player == null) return;

            if (!IsSeekable(player.CurrentTrack))
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "V živém přenosu nelze měnit pozici",
                    "(Neplatný příkaz)"));

                return;
            }

            if (time <= TimeSpan.Zero)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Nelze posunout o zápornou nebo nulovou hodnotu",
                    "(Neplatný argument)"));

                return;
            }

            var newTime = player.Position - time;

            if (newTime < TimeSpan.Zero)
                newTime = TimeSpan.Zero;

            await RespondAsync(embed: AudioEmbeds.Info(
                $"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`", Context.User));

            await player.SeekAsync(newTime);
        }

        [SlashCommand("volume", "Sets a volume of the audio transmissions")]
        public async Task CmdAudioVolume(ushort percentage)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.Playing);

            if (player == null) return;

            if (percentage > 150)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Mé jádro nepodporuje hlasitost vyšší než 150%",
                    "(Neplatný argument)"));

                return;
            }

            await RespondAsync(embed: AudioEmbeds.Info(
                $"Hlasitost audia byla úspěšně nastavena na {percentage}%", Context.User));

            await player.SetVolumeAsync(percentage * 0.01f);
        }

        [SlashCommand("status", "Shows active audio transmissions")]
        public async Task CmdAudioStatus()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: false,
                AudioPrecondition.Playing);

            if (player == null) return;

            var item = player.CurrentItem!;

            await RespondAsync(
                embed: AudioEmbeds.NowPlaying(item, player.IsPaused, player.Volume, player.Position),
                components: AudioEmbeds.PlayerControls());
        }

        [SlashCommand("queue", "Shows enqueued audio transmissions")]
        public async Task CmdAudioQueue()
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: false,
                AudioPrecondition.QueueNotEmpty);

            if (player == null) return;

            var paginator = new AudioQueuePaginator(5);
            var currentItem = player.CurrentItem;
            var queue = player.Queue.ToList();

            var totalDuration = queue.Aggregate(
                currentItem?.Track.Duration ?? TimeSpan.Zero,
                (total, item) => total + item.Track.Duration);

            var header = AudioEmbeds
                .QueueHeader(currentItem, player.IsPaused, queue.Count)
                .ToList();

            var tracks = queue
                .Select((item, index) => AudioEmbeds.QueueEntry(index + 1, item))
                .ToList();

            var footer = new List<EmbedFieldBuilder>
            {
                new()
                {
                    Name = "\u200B",
                    Value = $"Celková doba poslechu: `{totalDuration:hh\\:mm\\:ss}`"
                }
            };

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
                var embed = AudioEmbeds.Error(
                    "Mé jádro přerušilo čekání na lidský vstup",
                    "(Vypršel časový limit)");

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
                AudioPrecondition.QueueNotEmpty);

            if (player == null) return;

            // The positions shown by /audio queue start at one
            if (index <= 0 || index > player.Queue.Count)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Požadovaná stopa se ve frontě nenachází",
                    "(Neplatná pozice)"));

                return;
            }

            await player.Queue.RemoveAtAsync(index - 1);

            await RespondAsync(embed: AudioEmbeds.Info(
                "Požadovaná stopa byla úspěšně odebrána z fronty", Context.User));
        }

        [SlashCommand("repeat", "Repeats enqueued audio transmission")]
        public async Task CmdRepeatAudio(AudioRepeatMode mode)
        {
            var player = await GetPlayerAsync(
                joinChannel: false, sameChannel: true,
                AudioPrecondition.Playing);

            if (player == null) return;

            if (player.RepeatMode != mode && mode != AudioRepeatMode.None)
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

                player.RepeatMode = AudioRepeatMode.None;
            }
        }

        private static bool IsSeekable(AudioTrack track)
        {
            return track is { IsLiveStream: false } && track.Duration > TimeSpan.Zero;
        }

        #endregion
    }
}
