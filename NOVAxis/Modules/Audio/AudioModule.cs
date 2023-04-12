using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Utilities;
using NOVAxis.Extensions;
using NOVAxis.Preconditions;
using NOVAxis.Database.Guild;
using NOVAxis.Services.Audio;

using Discord;
using Discord.Interactions;

using Victoria.Player;
using Victoria.Responses.Search;

namespace NOVAxis.Modules.Audio
{
    [Cooldown(1)]
    [Group("audio", "Audio related commands")]
    [RequireContext(ContextType.Guild)]
    [RequireGuildRole("DJ")]
    public class AudioModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public AudioNode AudioNode { get; set; }
        public ProgramConfig Config { get; set; }
        public AudioService AudioService { get; set; }
        public AudioContext AudioContext { get; private set; }
        public GuildDbContext GuildDbContext { get; set; }
        public Cache<ulong, object> InteractionCache { get; set; }

        #region Functions

        public override void BeforeExecute(ICommandInfo command)
        {
            AudioContext = AudioService[Context.Guild.Id];
            base.BeforeExecute(command);
        }

        public async IAsyncEnumerable<AudioTrack> Search(string input)
        {
            SearchResponse response = Uri.IsWellFormedUriString(input, 0)
                ? await AudioNode.SearchAsync(SearchType.Direct, input)
                : await AudioNode.SearchAsync(SearchType.YouTube, input);

            switch (response.Status)
            {
                case SearchStatus.LoadFailed: throw new HttpRequestException();
                case SearchStatus.NoMatches: throw new ArgumentNullException();
            }

            // Check if search result is a playlist
            if (!string.IsNullOrEmpty(response.Playlist.Name))
            {
                foreach (var track in response.Tracks)
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

        public static IReadOnlyList<Emoji> GetStatusEmoji(AudioContext audioContext, AudioPlayer player = null)
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

        [SlashCommand("join", "Joins a selected voice channel")]
        public async Task CmdJoinChannel(IVoiceChannel voiceChannel = null)
        {
            var guildUser = Context.User as IGuildUser;
            voiceChannel ??= guildUser?.VoiceChannel;

            if (!AudioNode.IsConnected)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            if (voiceChannel == null)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle("Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci").Build());

                return;
            }

            await JoinChannel(voiceChannel);
        }

        private async Task<AudioPlayer> JoinChannel(IVoiceChannel voiceChannel)
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.VoiceChannel == voiceChannel)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Mé jádro už bylo naladěno na stejnou zvukovou frekvenci").Build());

                    return null;
                }

                AudioContext.Queue.Clear();

                await AudioNode.LeaveAsync(voiceChannel);
                player = await AudioNode.JoinAsync(voiceChannel, Context.Channel as ITextChannel);
            }

            else
                player = await AudioNode.JoinAsync(voiceChannel, Context.Channel as ITextChannel);

            if (player.Volume == 0)
                await player.SetVolumeAsync(100);

            await AudioContext.InitiateDisconnectAsync(player, Config.Audio.Timeout.Idle);
            
            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Připojuji se ke kanálu `{voiceChannel.Name}`").Build());

            return player;
        }

        [SlashCommand("leave", "Leaves a voice channel")]
        public async Task CmdLeaveChannel()
        {
            if (!AudioNode.IsConnected)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            if (!AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Mé jádro musí být před odpojením naladěno na správnou frekvenci").Build());

                return;
            }

            var guildUser = Context.User as IGuildUser;
            var voiceChannel = guildUser?.VoiceChannel;

            var usersInChannel = await player.VoiceChannel.GetHumanUsers().CountAsync();

            if (player.VoiceChannel != voiceChannel && usersInChannel > 0)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Pro komunikaci s jádrem musíš být naladěn na stejnou frekvenci").Build());

                return;
            }

            await LeaveChannel(player);
        }

        private async Task LeaveChannel(AudioPlayer player)
        {
            if (player != null)
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Odpojuji se od kanálu `{player.VoiceChannel.Name}`").Build());

                await AudioNode.LeaveAsync(player.VoiceChannel);
            }

            AudioService.Remove(Context.Guild.Id);
        }

        [Cooldown(5)]
        [SlashCommand("play", "Plays an audio transmission")]
        public async Task CmdPlayAudio(string input)
        {
            try
            {
                var tracks = await Search(input).ToListAsync();
                await PlayAudio(tracks);
            }

            catch (HttpRequestException)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní stream audia").Build());
            }

            catch (ArgumentNullException)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Mému jádru se nepodařilo v databázi nalézt požadovanou stopu").Build());
            }
        }

        private async Task PlayAudio(List<AudioTrack> tracks)
        {
            if (!AudioNode.IsConnected)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Služba není dostupná)")
                    .WithTitle("Mé jádro pravě nemůže poskytnout stabilní modul audia").Build());

                return;
            }

            var guildUser = Context.User as IGuildUser;
            var voiceChannel = guildUser?.VoiceChannel;

            if (voiceChannel == null)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle("Mému jádru se nepodařilo naladit na stejnou zvukovou frekvenci").Build());

                return;
            }

            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.VoiceChannel != voiceChannel)
                {
                    AudioContext.Queue.Clear();

                    await AudioNode.LeaveAsync(voiceChannel);
                    player = await JoinChannel(voiceChannel);
                }

                await PlayAudio(player, tracks);
            }

            else
            {
                AudioContext.Queue.Clear();
                player = await JoinChannel(voiceChannel);

                await PlayAudio(player, tracks);
            }
        }

        private async Task PlayAudio(AudioPlayer player, List<AudioTrack> tracks)
        {
            AudioContext.Queue.Enqueue(tracks);

            if (AudioContext.Queue.Count == 1)
            {
                var track = AudioContext.Track;
                await player.PlayAsync(AudioContext.Queue.First());

                var embed = new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{track.Title}")
                    .WithUrl(track.Url)
                    .WithThumbnailUrl(track.GetThumbnailUrl())
                    .AddField("Autor:", track.Author, true)
                    .AddField("Délka:", $"`{track.Duration}`", true)
                    .AddField("Vyžádal:", track.RequestedBy.Mention, true)
                    .AddField("Hlasitost:", $"{player.Volume}%", true)
                    .Build();

                if (Context.Interaction.HasResponded)
                    await FollowupAsync(embed: embed);
                else 
                    await RespondAsync(embed: embed);
            }

            else
            {
                if (tracks.Count > 1)
                {
                    var totalDuration = new TimeSpan();

                    foreach (var track in tracks)
                        totalDuration += track.Duration;

                    await RespondAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithAuthor($"Přidáno do fronty ({tracks.Count}):")
                        .WithTitle($"{AudioContext.LastTrack.Title}")
                        .WithUrl(AudioContext.LastTrack.Url)
                        .WithThumbnailUrl(AudioContext.LastTrack.ThumbnailUrl)
                        .AddField("Délka:", $"`{totalDuration}`", true)
                        .AddField("Vyžádal:", AudioContext.LastTrack.RequestedBy.Mention, true)
                        .Build());
                }

                else
                {
                    var track = AudioContext.LastTrack;

                    var embed = new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithAuthor("Přidáno do fronty:")
                        .WithTitle($"{track.Title}")
                        .WithUrl(track.Url)
                        .WithThumbnailUrl(track.ThumbnailUrl)
                        .AddField("Autor:", track.Author, true)
                        .AddField("Délka:", $"`{track.Duration}`", true)
                        .AddField("Vyžádal:", track.RequestedBy.Mention, true)
                        .AddField("Pořadí ve frontě:", $"`{AudioContext.Queue.Count - 1}.`", true)
                        .Build();

                    await RespondAsync(embed: embed);
                }
            }
        }

        [ComponentInteraction("AudioControls_*", true)]
        public async Task AudioControls(string action)
        {
            switch (action)
            {
                case "Skip":
                    await CmdSkipAudio();
                    break;
                case "Stop":
                    await CmdStopAudio();
                    break;
                case "Repeat":
                    await (AudioContext.Repeat != RepeatMode.None
                        ? CmdRepeatAudio()
                        : CmdRepeatAudio(RepeatMode.First));
                    break;
                case "RepeatOnce":
                    await (AudioContext.Repeat != RepeatMode.None
                        ? CmdRepeatAudio()
                        : CmdRepeatAudio(RepeatMode.Once));
                    break;
                case "PlayPause":
                    AudioNode.TryGetPlayer(Context.Guild, out var player);
                    await (player == null || player.PlayerState == PlayerState.Playing
                        ? CmdPauseAudio()
                        : CmdResumeAudio());
                    break;
            }
        }

        [SlashCommand("skip", "Skips to the next audio transmission")]
        public async Task CmdSkipAudio()
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně přeskočen").Build());

                await player.StopAsync();
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("stop", "Stops the audio transmission")]
        public async Task CmdStopAudio()
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                AudioContext.Queue.Clear();
                AudioContext.Repeat = RepeatMode.None;

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně zastaven").Build());

                await player.StopAsync();
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("pause", "Pauses the audio transmission")]
        public async Task CmdPauseAudio()
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (player.PlayerState == PlayerState.Paused)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Stream audia byl dávno pozastaven (pro obnovení použíjte `~audio resume`)")
                        .Build());

                    return;
                }

                await player.PauseAsync();
                await AudioContext.InitiateDisconnectAsync(player, Config.Audio.Timeout.Paused);

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně pozastaven").Build());
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("resume", "Resumes the audio transmission")]
        public async Task CmdResumeAudio()
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (player.PlayerState == PlayerState.Playing)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Stream audia právě běží (pro pozastavení použíjte `~audio pause`)").Build());

                    return;
                }

                await player.ResumeAsync();
                await AudioContext.CancelDisconnectAsync();

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně obnoven").Build());
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("seek", "Seeks a position in the audio transmissions")]
        public async Task CmdSeekAudio(TimeSpan time)
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (time > player.Track.Duration)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze nastavit hodnotu přesahující maximální délku stopy").Build());

                    return;
                }

                if (time < TimeSpan.Zero)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze nastavit zápornou hodnotu").Build());

                    return;
                }

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Pozice audia byla úspěšně nastavena na `{time:hh\\:mm\\:ss}`").Build());

                await player.SeekAsync(time);
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("forward", "Forwards to a position in the audio transmissions")]
        public async Task CmdForwardAudio(TimeSpan time)
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (time <= TimeSpan.Zero)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze posunout o zápornou nebo nulovou hodnotu").Build());

                    return;
                }

                var newTime = player.Track.Position + time;

                if (newTime > player.Track.Duration)
                    newTime = player.Track.Duration;

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`").Build());

                await player.SeekAsync(newTime);
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("backward", "Backwards to a position in the audio transmissions")]
        public async Task CmdBackwardAudio(TimeSpan time)
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (time <= TimeSpan.Zero)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Nelze posunout o zápornou nebo nulovou hodnotu").Build());

                    return;
                }

                var newTime = player.Track.Position - time;

                if (newTime < TimeSpan.Zero)
                    newTime = TimeSpan.Zero;

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Pozice audia byla úspěšně nastavena na `{newTime:hh\\:mm\\:ss}`").Build());

                await player.SeekAsync(newTime);
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("volume", "Sets a volume of the audio transmissions")]
        public async Task CmdAudioVolume(ushort percentage)
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (percentage > 150)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný argument)")
                        .WithTitle("Mé jádro nepodporuje hlasitost vyšší než 150%").Build());

                    return;
                }

                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Hlasitost audia byla úspěšně nastavena na {percentage}%").Build());

                await player.SetVolumeAsync(percentage);
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("status", "Shows active audio transmissions")]
        public async Task CmdAudioStatus()
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                var statusEmoji = GetStatusEmoji(AudioContext, player);

                var embed = new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{player.Track.Title}")
                    .WithUrl(player.Track.Url)
                    .WithThumbnailUrl(player.Track.GetThumbnailUrl())
                    .AddField("Autor:", player.Track.Author, true)
                    .AddField("Pozice:", $"`{player.Track.Position:hh\\:mm\\:ss} / {player.Track.Duration}`", true)
                    .AddField("Vyžádal:", AudioContext.Track.RequestedBy.Mention, true)
                    .AddField("Hlasitost:", $"{player.Volume}%", true)
                    .AddField("Stav:", $"{string.Join(' ', statusEmoji)}", true)
                    .Build();

                await RespondAsync(embed: embed);
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

        [SlashCommand("queue", "Shows enqueued audio transmissions")]
        public async Task CmdAudioQueue()
        {
            if (AudioContext.Queue.Count < 1 || !AudioNode.HasPlayer(Context.Guild))
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď se ve frontě nenachází žádná zvuková stopa").Build());

                return;
            }

            var paginator = new AudioQueuePaginator(5);
            var statusEmoji = GetStatusEmoji(AudioContext);
            var totalDuration = TimeSpan.Zero;

            var header = new List<EmbedFieldBuilder>();
            var tracks = new List<EmbedFieldBuilder>();
            var footer = new List<EmbedFieldBuilder>();

            var currentNode = AudioContext.Queue.First;
            for (int i = 0; currentNode != null; i++, currentNode = currentNode.Next)
            {
                var track = currentNode.Value;

                if (i == 0)
                {
                    var emoji = statusEmoji[0];

                    header.Add(new EmbedFieldBuilder
                    {
                        Name = $"{emoji} **{track.Title}**",
                        Value = $"Vyžádal: {track.RequestedBy.Mention} | Délka: `{track.Duration}` | [Odkaz]({track.Url})\n"
                    });

                    header.Add(new EmbedFieldBuilder
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

                    tracks.Add(new EmbedFieldBuilder
                    {
                        Name = $"{emoji} `{i}.` {track.Title}",
                        Value = $"Vyžádal: {track.RequestedBy.Mention} | Délka: `{track.Duration}` | [Odkaz]({track.Url})"
                    });
                }

                totalDuration += track.Duration;
            }

            footer.Add(new EmbedFieldBuilder
            {
                Name = "\u200B",
                Value = $"Celková doba poslechu: `{totalDuration}`"
            });

            var id = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now);

            InteractionCache[id] = paginator
                .WithHeader(header)
                .WithTracks(tracks)
                .WithFooter(footer);

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
            if (AudioContext.Queue.Count <= 1)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď se ve frontě nenachází žádná zvuková stopa").Build());

                return;
            }

            if (index <= 0 || index >= AudioContext.Queue.Count)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatná pozice)")
                    .WithTitle("Požadovaná stopa se ve frontě nenachází").Build());

                return;
            }

            AudioContext.Queue.RemoveAt(index);

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Požadovaná stopa byla úspěšně odebrána z fronty").Build());
        }

        [SlashCommand("repeat", "Repeats enqueued audio transmission")]
        public async Task CmdRepeatAudio(RepeatMode mode = RepeatMode.None)
        {
            if (AudioNode.TryGetPlayer(Context.Guild, out var player))
            {
                if (player.Track == null)
                {
                    await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Neplatný příkaz)")
                        .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());

                    return;
                }

                if (AudioContext.Repeat != mode && mode != RepeatMode.None)
                {
                    await RespondAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle("Nadcházející stopy nyní porušují časové kontinuum")
                        .WithDescription("(Režim opakování byl zapnut)")
                        .Build());

                    AudioContext.Repeat = mode;
                }

                else
                {
                    await RespondAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle("Nadcházející stopy nyní dodržují časové kontinuum")
                        .WithDescription("(Režim opakování byl vypnut)")
                        .Build());

                    AudioContext.Repeat = RepeatMode.None;
                }
            }

            else
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(255, 150, 0)
                    .WithDescription("(Neplatný příkaz)")
                    .WithTitle("Právě teď není streamováno na serveru žádné audio").Build());
            }
        }

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
                    .WithTitle("Konfigurace mého jádra proběhla úspešně").Build());
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
                    .WithTitle("Konfigurace mého jádra proběhla úspešně").Build());
            }

            await GuildDbContext.SaveChangesAsync();
        }

        #endregion
    }
}
