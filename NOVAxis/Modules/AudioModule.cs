using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NOVAxis.Services;
using NOVAxis.Extensions;
using NOVAxis.Preconditions;

using Discord;
using Discord.Commands;

using SharpLink;

namespace NOVAxis.Modules
{
    [Group("audio")]
    [RequireContext(ContextType.Guild)]
    [RequireOwner(Group = "Permission")]
    [RequireRole("DjRole", true, Group = "Permission")]
    public class AudioModule : ModuleBase<SocketCommandContext>
    {
        public AudioModuleService AudioModuleService { get; set; }
        public GuildService GuildService { get; set; }

        protected override void BeforeExecute(CommandInfo command)
        {
            var service = AudioModuleService[Context.Guild.Id];

            service.BoundChannel = Context.Channel;

            if (service.Timer.IsSet)
                service.Timer.Reset();

            base.BeforeExecute(command);
        }

        [Command("join"), Summary("Joins a voice channel")]
        public async Task JoinChannel()
        {
            IVoiceChannel voiceChannel = ((IGuildUser)Context.User).VoiceChannel;

            await JoinChannel(voiceChannel);
        }

        [Command("join"), Summary("Joins a selected voice channel")]
        public async Task JoinChannel(string channelname)
        {
            IVoiceChannel voiceChannel = (from ch in Context.Guild.VoiceChannels
                                          where ch.Name.Contains(channelname)
                                          select ch).Single();

            await JoinChannel(voiceChannel);
        }

        private async Task JoinChannel(IVoiceChannel voiceChannel)
        {
            var service = AudioModuleService[Context.Guild.Id];

            if (!LavalinkService.IsConnected)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle($"Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            if (voiceChannel == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle($"Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci").Build());

                return;
            }

            LavalinkPlayer player = service.GetPlayer();

            if (player != null)
            {
                if (player.VoiceChannel == voiceChannel)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle($"Mé jádro už bylo naladěno na stejnou zvukovou frekvenci").Build());

                    return;
                }

                AudioModuleService[Context.Guild.Id].Queue.Clear();

                await LavalinkService.Manager.LeaveAsync(voiceChannel.GuildId);
                await LavalinkService.Manager.JoinAsync(voiceChannel);
            }

            else
            {
                await LavalinkService.Manager.JoinAsync(voiceChannel);
            }

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Připojuji se ke kanálu `{voiceChannel.Name}`").Build());

            if (!service.Timer.IsSet)
                service.Timer.Set(AudioModuleService.AudioTimeout, Timer_Elapsed);

            service.Timer.Reset();
        }

        [Command("leave"), Alias("quit", "disconnect"), Summary("Leaves a voice channel")]
        public async Task LeaveChannel()
        {
            var service = AudioModuleService[Context.Guild.Id];

            if (!LavalinkService.IsConnected)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle($"Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);
            IVoiceChannel voiceChannel = ((IGuildUser)Context.User).VoiceChannel;

            if (player == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Mé jádro musí být před odpojením naladěno na správnou frekvenci").Build());

                return;
            }

            if (player.VoiceChannel != voiceChannel && await player.VoiceChannel.GetHumanUsers().Count() > 0)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Pro komunikaci s jádrem musíš být naladěn na stejnou frekvenci").Build());

                return;
            }

            await LeaveChannel(service, player);
        }

        private async Task LeaveChannel(AudioModuleService.Context service, LavalinkPlayer player)
        {
            await service.BoundChannel.SendMessageAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Odpojuji se od kanálu `{player.VoiceChannel.Name}`").Build());

            service.Queue.Clear();
            service.Timer.Stop();
            service.Timer.Dispose();

            await player.StopAsync();
            await player.DisconnectAsync();
        }

        [Command("play"), Summary("Plays an audio transmission")]
        public async Task PlayAudio([Remainder]string input)
        {
            if (!LavalinkService.IsConnected)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle($"Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            IVoiceChannel voiceChannel = ((IGuildUser)Context.User).VoiceChannel;

            if (voiceChannel == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle($"Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci").Build());

                return;
            }

            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(voiceChannel.GuildId);

            if (player == null)
            {
                await JoinChannel(voiceChannel);
                player = LavalinkService.Manager.GetPlayer(voiceChannel.GuildId);
            }

            if (player.VoiceChannel != voiceChannel)
            {
                AudioModuleService[Context.Guild.Id].Queue.Clear();

                await LavalinkService.Manager.LeaveAsync(voiceChannel.GuildId);
                await JoinChannel(voiceChannel);

                player = LavalinkService.Manager.GetPlayer(voiceChannel.GuildId);
            }

            await PlayAudio(player, input);
        }

        private async Task PlayAudio(LavalinkPlayer player, string input)
        {
            var service = AudioModuleService[Context.Guild.Id];

            try
            {
                string search = (Uri.IsWellFormedUriString(input, 0) ? "" : "ytsearch:") + input;

                LoadTracksResponse tracks = await LavalinkService.Manager.GetTracksAsync(search);
                LavalinkTrack track = tracks.Tracks.First();

                service.Queue.Add(new AudioModuleService.Context.ContextTrack
                {
                    Value = track,
                    RequestedBy = Context.User
                });

                if (service.Queue.Count == 1)
                    await player.PlayAsync(track);
            }

            catch (HttpRequestException)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle($"Mé jádro pravě nemůže poskytnout stabilní stream audia").Build());

                return;
            }

            catch (ArgumentNullException)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle($"Mému jádru se nepodařilo v databázi nalézt požadovanou stopu").Build());

                return;
            }    

            if (service.Queue.Count == 1)
            {             
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{new Emoji("\u25B6")} {service.LastTrack.Value.Title}")
                    .WithUrl(player.CurrentTrack.Url)
                    .WithThumbnailUrl(service.CurrentTrack.ThumbnailUrl)
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = player.CurrentTrack.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Délka:",
                            Value = $"`{player.CurrentTrack.Length}`",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Vyžádal:",
                            Value = service.CurrentTrack.RequestedBy.Mention,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Hlasitost:",
                            Value = $"{service.Volume}%",
                            IsInline = true
                        }
                    ).Build());
            }

            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Přidáno do fronty:")
                    .WithTitle($"{new Emoji("\u23ED")} {service.LastTrack.Value.Title}")
                    .WithUrl(service.LastTrack.Value.Url)
                    .WithThumbnailUrl(service.LastTrack.ThumbnailUrl)
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = service.LastTrack.Value.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Délka:",
                            Value = $"`{service.LastTrack.Value.Length}`",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Vyžádal:",
                            Value = service.LastTrack.RequestedBy.Mention,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Pořadí ve frontě:",
                            Value = $"`{service.Queue.Count - 1}.`",
                            IsInline = true
                        }
                    ).Build());
            }
        }

        [Command("skip"), Alias("next"), Summary("Skips to the next audio transmission")]
        public async Task SkipAudio()
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            await player.SeekAsync((int)player.CurrentTrack.Length.TotalMilliseconds);
        }

        [Command("stop"), Summary("Stops the audio transmission")]
        public async Task StopAudio()
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            AudioModuleService[Context.Guild.Id].Queue.Clear();
            await player.StopAsync();

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Stream audia byl úspěšně zastaven").Build());
        }

        [Command("pause"), Summary("Pauses the audio transmission")]
        public async Task PauseAudio()
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            if (!player.Playing)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Stream audia byl dávno pozastaven (pro obnovení použíjte `~audio resume`)").Build());

                return;
            }

            await player.PauseAsync();

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Stream audia byl úspěšně pozastaven").Build());
        }

        [Command("resume"), Summary("Resumes the audio transmission")]
        public async Task ResumeAudio()
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            if (player.Playing)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Stream audia právě běží (pro pozastavení použíjte `~audio pause`)").Build());

                return;
            }

            await player.ResumeAsync();

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Stream audia byl úspěšně obnoven").Build());
        }

        [Command("seek"), Summary("Seeks a position in the audio transmissions")]
        public async Task SeekAudio(TimeSpan time)
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            if (player.CurrentTrack.Length < time)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle($"Nelze nastavit hodnotu přesahující maximální délku stopy").Build());

                return;
            }

            if (time < TimeSpan.Zero)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle($"Nelze nastavit zápornou hodnotu").Build());

                return;
            }

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Pozice audia byla úspěšně nastavena na `{time}`").Build());

            await player.SeekAsync((int)time.TotalMilliseconds);
        }

        [Command("forward"), Summary("Forwards to a position in the audio transmissions")]
        public async Task ForwardAudio(TimeSpan time)
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            if (time <= TimeSpan.Zero)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle($"Nelze posunout o zápornou nebo nulovou hodnotu").Build());

                return;
            }

            TimeSpan newTime = TimeSpan.FromSeconds(player.CurrentPosition / 1000d) + time;

            if (newTime > player.CurrentTrack.Length)
                newTime = player.CurrentTrack.Length;

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime}`").Build());

            await player.SeekAsync((int)newTime.TotalMilliseconds);
        }

        [Command("backward"), Summary("Backwards to a position in the audio transmissions")]
        public async Task BackwardAudio(TimeSpan time)
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            if (time <= TimeSpan.Zero)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle($"Nelze posunout o zápornou nebo nulovou hodnotu").Build());

                return;
            }

            TimeSpan newTime = TimeSpan.FromSeconds(player.CurrentPosition / 1000d) - time;

            if (newTime < TimeSpan.Zero)
                newTime = TimeSpan.Zero;

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime}`").Build());

            await player.SeekAsync((int)newTime.TotalMilliseconds);
        }

        [Command("volume"), Summary("Sets a volume of the audio transmissions")]
        public async Task AudioVolume(uint percentage)
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);
            var service = AudioModuleService[Context.Guild.Id];

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            if (percentage > 150)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle($"Mé jádro nepodporuje hlasitost vyšší než 150%").Build());

                return;
            }

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Hlasitost audia byla úspěšně nastavena na {percentage}%").Build());

            await player.SetVolumeAsync(percentage);
            service.Volume = percentage;
        }

        [Command("status"), Alias("np", "info"), Summary("Shows active audio transmissions")]
        public async Task AudioStatus()
        {
            LavalinkPlayer player = LavalinkService.Manager.GetPlayer(Context.Guild.Id);
            var service = AudioModuleService[Context.Guild.Id];

            if (player?.CurrentTrack == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď není streamováno na serveru žádné audio").Build());

                return;
            }

            await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{(player.Playing ? new Emoji("\u25B6") : new Emoji("\u23F8"))} {player.CurrentTrack.Title}")
                    .WithUrl(player.CurrentTrack.Url)
                    .WithThumbnailUrl(service.CurrentTrack.ThumbnailUrl)
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = player.CurrentTrack.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Pozice:",
                            Value = $"`{TimeSpan.FromSeconds(player.CurrentPosition / 1000d)} / {player.CurrentTrack.Length}`",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Vyžádal:",
                            Value = service.CurrentTrack.RequestedBy.Mention,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Hlasitost:",
                            Value = $"{service.Volume}%",
                            IsInline = true
                        }
                    ).Build());
        }

        [Command("queue"), Summary("Shows enqueued audio transmissions")]
        public async Task AudioQueue()
        {
            var service = AudioModuleService[Context.Guild.Id];

            if (service.Queue.Count <= 0)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď se ve frontě nenachází žádná zvuková stopa").Build());

                return;
            }

            EmbedFieldBuilder[] embedFields = new EmbedFieldBuilder[service.Queue.Count];

            for (int i = 0; i < service.Queue.Count; i++)
            {
                embedFields[i] = new EmbedFieldBuilder
                {
                    Name = $"{(i == 0 ? "**" : "")}`{i}.` " +
                    $"{(i == 0 ? (service.GetPlayer().Playing ? new Emoji("\u25B6") : new Emoji("\u23F8")) : (i == 1 ? new Emoji("\u23ED") : null))} {service.Queue[i].Value.Title}" +
                    $"{(i == 0 ? "**" : "")}",

                    Value = $"Vyžádal: {service.Queue[i].RequestedBy.Mention} | Délka: `{service.Queue[i].Value.Length}` | [Odkaz]({service.Queue[i].Value.Url})"
                };
            }

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Stopy ve frontě ({service.Queue.Count - 1}):")
                .WithFields(embedFields)
                .Build());
        }

        [Command("remove"), Summary("Removes an enqueued audio transmission")]
        public async Task RemoveAudio(int index)
        {
            var service = AudioModuleService[Context.Guild.Id];

            if (service.Queue.Count <= 1)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle($"Právě teď se ve frontě nenachází žádná zvuková stopa").Build());

                return;
            }

            if (index <= 0 || index >= service.Queue.Count)
            {
                await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatná pozice)")
                    .WithTitle($"Požadovaná stopa se ve frontě nenachází").Build());

                return;
            }

            service.Queue.RemoveAt(index);

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Požadovaná stopa byla úspěšně odebrána z fronty").Build());
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

        public async void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var service = AudioModuleService[Context.Guild.Id];

            if (service.Queue.Count > 0 && service.GetPlayer().Playing)
                return;

            if (LavalinkService.IsConnected)
                await LeaveChannel(service, LavalinkService.Manager.GetPlayer(Context.Guild.Id));
            else
                await ((IGuildUser)Context.User).VoiceChannel.DisconnectAsync();
        }
    }
}
