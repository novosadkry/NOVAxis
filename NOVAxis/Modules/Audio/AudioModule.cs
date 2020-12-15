using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Extensions;
using NOVAxis.Preconditions;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Guild;

using Discord;
using Discord.Commands;

using Interactivity;

using Victoria;
using Victoria.Enums;
using Victoria.Responses.Rest;

namespace NOVAxis.Modules.Audio
{
    [Group("audio"), Alias("a")]
    [RequireContext(ContextType.Guild)]
    [RequireOwner(Group = "Permission")]
    [RequireRole("DjRole", true, Group = "Permission")]
    public class AudioModule : ModuleBase<ShardedCommandContext>
    {
        public LavaNode LavaNode { get; set; }
        public InteractivityService InteractivityService { get; set; }
        public AudioModuleService AudioModuleService { get; set; }
        public AudioContext AudioContext { get; private set; }
        public GuildService GuildService { get; set; }

        #region Functions
        protected override void BeforeExecute(CommandInfo command)
        {
            AudioContext = AudioModuleService[Context.Guild.Id];

            if (!AudioContext.Timer.IsSet)
                AudioContext.Timer.Set(Program.Config.AudioTimeout, Timer_Elapsed);

            base.BeforeExecute(command);
        }

        protected override void AfterExecute(CommandInfo command)
        {
            if (AudioContext.Timer.IsSet)
                AudioContext.Timer.Reset();

            base.AfterExecute(command);
        }

        private async void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (LavaNode.IsConnected && LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (AudioContext.Queue.Count > 0 && player.PlayerState == PlayerState.Playing)
                    return;

                await LeaveChannel(player);
            }

            await LeaveChannel(null);
        }

        public async IAsyncEnumerable<AudioTrack> Search(string input)
        {
            SearchResponse response = Uri.IsWellFormedUriString(input, 0)
                ? await LavaNode.SearchAsync(input)
                : await LavaNode.SearchYouTubeAsync(input);

            switch (response.LoadStatus)
            {
                case LoadStatus.LoadFailed: throw new HttpRequestException();
                case LoadStatus.NoMatches: throw new ArgumentNullException();
            }

            // Check if search result is a playlist
            if (!string.IsNullOrEmpty(response.Playlist.Name))
            {
                foreach (LavaTrack track in response.Tracks)
                {
                    yield return new AudioTrack(track)
                    {
                        RequestedBy = Context.User
                    };
                }
            }

            else
            {
                var track = response.Tracks.First();

                yield return new AudioTrack(track)
                {
                    RequestedBy = Context.User
                };
            }
        }

        public static IReadOnlyList<Emoji> GetStatusEmoji(AudioContext audioContext, LavaPlayer player = null)
        {
            Emoji[] statusEmoji =
            {
                audioContext.Repeat switch
                {
                    RepeatMode.Once => new Emoji("\uD83D\uDD02"),
                    RepeatMode.First => new Emoji("\uD83D\uDD01"),
                    RepeatMode.Queue => new Emoji("\uD83D\uDD01"),
                    _ => null
                },

                player != null
                    ? player.PlayerState == PlayerState.Playing
                        ? new Emoji("\u25B6") // Playing
                        : new Emoji("\u23F8") // Paused
                    : null
            };

            return statusEmoji;
        }
        #endregion

        #region Commands
        [Command("join"), Alias("j"), Summary("Joins a voice channel")]
        public async Task JoinChannel()
        {
            IVoiceChannel voiceChannel = ((IGuildUser)Context.User).VoiceChannel;

            await JoinChannel(voiceChannel);
        }

        [Command("join"), Alias("j"), Summary("Joins a selected voice channel")]
        public async Task JoinChannel(string channelname)
        {
            IVoiceChannel voiceChannel = (from ch in Context.Guild.VoiceChannels
                                          where ch.Name.Contains(channelname)
                                          select ch).FirstOrDefault();

            await JoinChannel(voiceChannel);
        }

        private async Task JoinChannel(IVoiceChannel voiceChannel)
        {
            if (!LavaNode.IsConnected)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            if (voiceChannel == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle("Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci").Build());

                return;
            }

            LavaPlayer player;

            if (LavaNode.HasPlayer(Context.Guild))
            {
                player = LavaNode.GetPlayer(Context.Guild);

                if (player.VoiceChannel == voiceChannel)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Mé jádro už bylo naladěno na stejnou zvukovou frekvenci").Build());

                    return;
                }

                AudioContext.Queue.Clear();

                await LavaNode.LeaveAsync(voiceChannel);
                player = await LavaNode.JoinAsync(voiceChannel, Context.Channel as ITextChannel);
            }

            else
                player = await LavaNode.JoinAsync(voiceChannel, Context.Channel as ITextChannel);

            if (player.Volume == 0)
                await player.UpdateVolumeAsync(100);

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Připojuji se ke kanálu `{voiceChannel.Name}`").Build());
        }

        [Command("leave"), Alias("quit", "disconnect", "l"), Summary("Leaves a voice channel")]
        public async Task LeaveChannel()
        {
            if (!LavaNode.IsConnected)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            if (!LavaNode.HasPlayer(Context.Guild))
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Mé jádro musí být před odpojením naladěno na správnou frekvenci").Build());

                return;
            }

            LavaPlayer player = LavaNode.GetPlayer(Context.Guild);
            IVoiceChannel voiceChannel = ((IGuildUser)Context.User).VoiceChannel;

            if (player.VoiceChannel != voiceChannel && await player.VoiceChannel.GetHumanUsers().CountAsync() > 0)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Pro komunikaci s jádrem musíš být naladěn na stejnou frekvenci").Build());

                return;
            }

            await LeaveChannel(player);
        }

        private async Task LeaveChannel(LavaPlayer player)
        {
            if (player != null)
            {
                await player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Odpojuji se od kanálu `{player.VoiceChannel.Name}`").Build());

                await LavaNode.LeaveAsync(player.VoiceChannel);
            }

            AudioModuleService.Remove(Context.Guild.Id);
            AudioContext.Dispose();
        }

        [Command("play"), Alias("p"), Summary("Plays an audio transmission")]
        public async Task PlayAudio([Remainder]string input)
        {
            if (!LavaNode.IsConnected)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            IVoiceChannel voiceChannel = ((IGuildUser)Context.User).VoiceChannel;

            if (voiceChannel == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle("Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci").Build());

                return;
            }

            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(voiceChannel.Guild);

                if (player.VoiceChannel != voiceChannel)
                {
                    AudioContext.Queue.Clear();

                    await LavaNode.LeaveAsync(voiceChannel);
                    await JoinChannel(voiceChannel);

                    player = LavaNode.GetPlayer(voiceChannel.Guild);
                }

                await PlayAudio(player, input);
            }

            else
            {
                AudioContext.Queue.Clear();
                await JoinChannel(voiceChannel);

                LavaPlayer player = LavaNode.GetPlayer(voiceChannel.Guild);

                await PlayAudio(player, input);
            }
        }

        private async Task PlayAudio(LavaPlayer player, string input)
        {
            try
            {
                var tracks = await Search(input).ToListAsync();
                AudioContext.Queue.Enqueue(tracks);

                if (AudioContext.Queue.Count == 1)
                {
                    await player.PlayAsync(AudioContext.Queue.First());

                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithAuthor("Právě přehrávám:")
                        .WithTitle($"{player.Track.Title}")
                        .WithUrl(player.Track.Url)
                        .WithThumbnailUrl(player.Track.GetThumbnailUrl())
                        .WithFields(
                            new EmbedFieldBuilder
                            {
                                Name = "Autor:",
                                Value = player.Track.Author,
                                IsInline = true
                            },

                            new EmbedFieldBuilder
                            {
                                Name = "Délka:",
                                Value = $"`{player.Track.Duration}`",
                                IsInline = true
                            },

                            new EmbedFieldBuilder
                            {
                                Name = "Vyžádal:",
                                Value = AudioContext.Track.RequestedBy.Mention,
                                IsInline = true
                            },

                            new EmbedFieldBuilder
                            {
                                Name = "Hlasitost:",
                                Value = $"{player.Volume}%",
                                IsInline = true
                            },

                            new EmbedFieldBuilder
                            {
                                Name = "Stav:",
                                Value = new Emoji("\u25B6"),
                                IsInline = true
                            }
                        ).Build());
                }

                else
                {
                    if (tracks.Count > 1)
                    {
                        TimeSpan totalDuration = new TimeSpan();

                        foreach (var track in tracks)
                            totalDuration += track.Duration;

                        await ReplyAsync(embed: new EmbedBuilder()
                            .WithColor(52, 231, 231)
                            .WithAuthor($"Přidáno do fronty ({tracks.Count}):")
                            .WithTitle($"{AudioContext.LastTrack.Title}")
                            .WithUrl(AudioContext.LastTrack.Url)
                            .WithThumbnailUrl(AudioContext.LastTrack.ThumbnailUrl)
                            .WithFields(
                                new EmbedFieldBuilder
                                {
                                    Name = "Délka:",
                                    Value = $"`{totalDuration}`",
                                    IsInline = true
                                },

                                new EmbedFieldBuilder
                                {
                                    Name = "Vyžádal:",
                                    Value = AudioContext.LastTrack.RequestedBy.Mention,
                                    IsInline = true
                                }
                            ).Build());
                    }

                    else
                    {
                        await ReplyAsync(embed: new EmbedBuilder()
                            .WithColor(52, 231, 231)
                            .WithAuthor("Přidáno do fronty:")
                            .WithTitle($"{AudioContext.LastTrack.Title}")
                            .WithUrl(AudioContext.LastTrack.Url)
                            .WithThumbnailUrl(AudioContext.LastTrack.ThumbnailUrl)
                            .WithFields(
                                new EmbedFieldBuilder
                                {
                                    Name = "Autor:",
                                    Value = AudioContext.LastTrack.Author,
                                    IsInline = true
                                },

                                new EmbedFieldBuilder
                                {
                                    Name = "Délka:",
                                    Value = $"`{AudioContext.LastTrack.Duration}`",
                                    IsInline = true
                                },

                                new EmbedFieldBuilder
                                {
                                    Name = "Vyžádal:",
                                    Value = AudioContext.LastTrack.RequestedBy.Mention,
                                    IsInline = true
                                },

                                new EmbedFieldBuilder
                                {
                                    Name = "Pořadí ve frontě:",
                                    Value = $"`{AudioContext.Queue.Count - 1}.`",
                                    IsInline = true
                                }
                            ).Build());
                    }
                }
            }

            catch (HttpRequestException)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní stream audia").Build());
            }

            catch (ArgumentNullException)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Mému jádru se nepodařilo v databázi nalézt požadovanou stopu").Build());
            }
        }

        [Command("skip"), Alias("next", "n"), Summary("Skips to the next audio transmission")]
        public async Task SkipAudio()
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                await player.StopAsync();
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("stop"), Alias("s"), Summary("Stops the audio transmission")]
        public async Task StopAudio()
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                AudioContext.Queue.Clear();
                AudioContext.Repeat = RepeatMode.None;

                await player.StopAsync();

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně zastaven").Build());
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("pause"), Summary("Pauses the audio transmission")]
        public async Task PauseAudio()
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (player.PlayerState == PlayerState.Paused)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Stream audia byl dávno pozastaven (pro obnovení použíjte `~audio resume`)")
                        .Build());

                    return;
                }

                await player.PauseAsync();

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně pozastaven").Build());
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("resume"), Summary("Resumes the audio transmission")]
        public async Task ResumeAudio()
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (player.PlayerState == PlayerState.Playing)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Stream audia právě běží (pro pozastavení použíjte `~audio pause`)").Build());

                    return;
                }

                await player.ResumeAsync();

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně obnoven").Build());
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("seek"), Summary("Seeks a position in the audio transmissions")]
        public async Task SeekAudio(TimeSpan time)
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (time > player.Track.Duration)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze nastavit hodnotu přesahující maximální délku stopy").Build());

                    return;
                }

                if (time < TimeSpan.Zero)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze nastavit zápornou hodnotu").Build());

                    return;
                }

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Pozice audia byla úspěšně nastavena na `{time:hh\\:mm\\:ss}`").Build());

                await player.SeekAsync(time);
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("forward"), Summary("Forwards to a position in the audio transmissions")]
        public async Task ForwardAudio(TimeSpan time)
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (time <= TimeSpan.Zero)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze posunout o zápornou nebo nulovou hodnotu").Build());

                    return;
                }

                TimeSpan newTime = player.Track.Position + time;

                if (newTime > player.Track.Duration)
                    newTime = player.Track.Duration;

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`").Build());

                await player.SeekAsync(newTime);
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("backward"), Summary("Backwards to a position in the audio transmissions")]
        public async Task BackwardAudio(TimeSpan time)
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (time <= TimeSpan.Zero)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze posunout o zápornou nebo nulovou hodnotu").Build());

                    return;
                }

                TimeSpan newTime = player.Track.Position - time;

                if (newTime < TimeSpan.Zero)
                    newTime = TimeSpan.Zero;

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`").Build());

                await player.SeekAsync(newTime);
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("volume"), Summary("Sets a volume of the audio transmissions")]
        public async Task AudioVolume(ushort percentage)
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (percentage > 150)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Mé jádro nepodporuje hlasitost vyšší než 150%").Build());

                    return;
                }

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Hlasitost audia byla úspěšně nastavena na {percentage}%").Build());

                await player.UpdateVolumeAsync(percentage);
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("status"), Alias("np", "info"), Summary("Shows active audio transmissions")]
        public async Task AudioStatus()
        {
            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                var statusEmoji = GetStatusEmoji(AudioContext, player);

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{player.Track.Title}")
                    .WithUrl(player.Track.Url)
                    .WithThumbnailUrl(player.Track.GetThumbnailUrl())
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = player.Track.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Pozice:",
                            Value = $"`{player.Track.Position:hh\\:mm\\:ss} / {player.Track.Duration}`",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Vyžádal:",
                            Value = AudioContext.Track.RequestedBy.Mention,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Hlasitost:",
                            Value = $"{player.Volume}%",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Stav:",
                            Value = $"{string.Join(' ', statusEmoji)}",
                            IsInline = true
                        }
                    ).Build());
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("queue"), Alias("q"), Summary("Shows enqueued audio transmissions")]
        public async Task AudioQueue()
        {
            if (AudioContext.Queue.Count < 1 || !LavaNode.HasPlayer(Context.Guild))
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď se ve frontě nenachází žádná zvuková stopa").Build());

                return;
            }

            AudioQueuePaginator queue = new AudioQueuePaginator(5);
            TimeSpan totalDuration = TimeSpan.Zero;

            var statusEmoji = GetStatusEmoji(AudioContext);

            var currentNode = AudioContext.Queue.First;
            for (int i = 0; currentNode != null; i++, currentNode = currentNode.Next)
            {
                var track = currentNode.Value;

                if (i == 0)
                {
                    var emoji = statusEmoji[0];

                    queue.Header.Add(new EmbedFieldBuilder
                    {
                        Name = $"{emoji} **{track.Title}**",
                        Value = $"Vyžádal: {track.RequestedBy.Mention} | Délka: `{track.Duration}` | [Odkaz]({track.Url})\n"
                    });

                    queue.Header.Add(new EmbedFieldBuilder
                    {
                        Name = "\u200B", 
                        Value = $"**Stopy ve frontě ({AudioContext.Queue.Count - 1}):**"
                    });
                }

                else
                {
                    var emoji = AudioContext.Repeat == RepeatMode.Queue
                        ? statusEmoji[0]
                        : null;

                    queue.Tracks.Add(new EmbedFieldBuilder
                    {
                        Name = $"{emoji} `{i}.` {track.Title}",
                        Value = $"Vyžádal: {track.RequestedBy.Mention} | Délka: `{track.Duration}` | [Odkaz]({track.Url})"
                    });
                }

                totalDuration += track.Duration;
            }

            queue.Footer.Add(new EmbedFieldBuilder
            {
                Name = "\u200B",
                Value = $"Celková doba poslechu: `{totalDuration}`"
            });

            await InteractivityService.SendPaginatorAsync(
                queue.Build(), 
                Context.Channel, 
                TimeSpan.FromMinutes(2));
        }

        [Command("remove"), Summary("Removes an enqueued audio transmission")]
        public async Task RemoveAudio(int index)
        {
            if (AudioContext.Queue.Count <= 1)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď se ve frontě nenachází žádná zvuková stopa").Build());

                return;
            }

            if (index <= 0 || index >= AudioContext.Queue.Count)
            {
                await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatná pozice)")
                    .WithTitle("Požadovaná stopa se ve frontě nenachází").Build());

                return;
            }

            AudioContext.Queue.RemoveAt(index);

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Požadovaná stopa byla úspěšně odebrána z fronty").Build());
        }

        [Command("repeat"), Summary("Repeats enqueued audio transmission")]
        public async Task RepeatAudio(string mode = null)
        {
            RepeatMode repeatMode;

            try { repeatMode = Enum.Parse<RepeatMode>(mode ?? string.Empty, true); }
            catch (Exception) { repeatMode = RepeatMode.None; }

            if (LavaNode.HasPlayer(Context.Guild))
            {
                LavaPlayer player = LavaNode.GetPlayer(Context.Guild);

                if (player.Track == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (AudioContext.Repeat != repeatMode && repeatMode != RepeatMode.None)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle("Nadcházející stopy nyní porušují časové kontinuum")
                        .WithDescription("(Režim opakování byl zapnut)")
                        .Build());

                    AudioContext.Repeat = repeatMode;
                }

                else
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle("Nadcházející stopy nyní dodržují časové kontinuum")
                        .WithDescription("(Režim opakování byl vypnut)")
                        .Build());

                    AudioContext.Repeat = RepeatMode.None;
                }
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [Command("setrole"), Summary("Sets the guild's DJ role which is used to identify eligible users")]
        public async Task SetDjRole(IRole role)
        {
            var guildInfo = await GuildService.GetInfo(Context);
            guildInfo.DjRole = role.Id;

            await GuildService.SetInfo(Context, guildInfo);

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithDescription($"(Nastavena role {role.Mention})")
                .WithTitle("Konfigurace mého jádra proběhla úspešně").Build());
        }

        [Command("setrole"), Summary("Sets the guild's DJ role which is used to identify eligible users")]
        public async Task SetDjRole(ulong roleId = 0)
        {
            IRole role = Context.Guild.GetRole(roleId);

            if (role != null)
            {
                var guildInfo = await GuildService.GetInfo(Context);
                guildInfo.DjRole = role.Id;

                await GuildService.SetInfo(Context, guildInfo);

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithDescription($"(Nastavená role {role.Mention})")
                    .WithTitle("Konfigurace mého jádra proběhla úspešně").Build());
            }

            else if (roleId == 0)
            {
                var guildInfo = await GuildService.GetInfo(Context);
                guildInfo.DjRole = 0;

                await GuildService.SetInfo(Context, guildInfo);

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithDescription("(Nastavená role zrušena)")
                    .WithTitle("Konfigurace mého jádra proběhla úspešně").Build());
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Má databáze nebyla schopna rozpoznat daný prvek").Build());
            }
        }
        #endregion
    }
}
