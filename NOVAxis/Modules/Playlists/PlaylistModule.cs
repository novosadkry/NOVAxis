using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using NOVAxis.Database.Playlists;
using NOVAxis.Preconditions;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Services.Playlists;
using NOVAxis.Utilities;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Modules.Playlists
{
    /// <summary>
    /// Offers the names the caller can actually open here - their own, plus whatever the
    /// guild has been given. Typing out a saved name from memory is the one thing
    /// playlists must not require.
    /// </summary>
    public class PlaylistNameHandler : AutocompleteHandler
    {
        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
            IInteractionContext context,
            IAutocompleteInteraction interaction,
            IParameterInfo parameter,
            IServiceProvider services)
        {
            var playlists = services.GetService(typeof(PlaylistService)) as PlaylistService;

            if (playlists is not { Active: true })
                return AutocompletionResult.FromSuccess();

            var typed = interaction.Data.Current.Value as string;

            var names = await playlists.SuggestAsync(
                typed, context.User.Id, context.Guild?.Id);

            return AutocompletionResult.FromSuccess(
                names.Select(x => new AutocompleteResult(x, x)));
        }
    }

    [Cooldown(1)]
    [Group("playlist", "Saved playlists")]
    [RequireContext(ContextType.Guild)]
    public class PlaylistModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public PlaylistService Playlists { get; set; }
        public IAudioPlayerManager PlayerManager { get; set; }
        public IAudioSearchService SearchService { get; set; }

        private const int Preview = 10;

        /// <summary>
        /// Answers and returns false where there is no store to talk to. Without it every
        /// command below would have to tell a refusal apart from a playlist that is simply
        /// not there - and would answer the interaction twice getting it wrong.
        /// </summary>
        private async ValueTask<bool> ReadyAsync()
        {
            if (Playlists.Active)
                return true;

            await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                "Playlisty jsou vypnuté",
                "(Chybí nastavení databáze)"));

            return false;
        }

        [SlashCommand("save", "Saves the current queue under a name")]
        public async Task CmdSave(
            string name,
            [Summary(description: "Offer it to everyone on this server")] bool share = false)
        {
            if (!await ReadyAsync()) return;

            var player = await GetPlayerAsync();

            if (player == null) return;

            // What is playing belongs at the front - reloading a queue which starts one
            // track in is not the queue that was saved
            var tracks = new List<AudioTrack>();

            if (player.CurrentTrack != null)
                tracks.Add(player.CurrentTrack);

            tracks.AddRange(player.Queue.Select(x => x.Track));

            var saved = await Run(() => Playlists.SaveAsync(
                Context.User.Id,
                (Context.User as IGuildUser)?.DisplayName ?? Context.User.Username,
                share ? Context.Guild.Id : null,
                name,
                tracks));

            if (saved == null) return;

            await RespondAsync(embed: Describe(saved,
                $"Playlist „{saved.Name}“ uložen",
                share ? "Sdílen s tímto serverem" : "Uložen jen pro tebe"));
        }

        [SlashCommand("load", "Queues a saved playlist")]
        public async Task CmdLoad(
            [Autocomplete(typeof(PlaylistNameHandler))] string name,
            [Summary(description: "Empty the queue first")] bool replace = false)
        {
            if (!await ReadyAsync()) return;

            var playlist = await Playlists.FindAsync(name, Context.User.Id, Context.Guild.Id);

            if (playlist == null)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Takový playlist neznám",
                    $"(„{name}“)"));

                return;
            }

            if (playlist.Tracks.Count == 0)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    $"Playlist „{playlist.Name}“ je prázdný",
                    "(Není co přehrát)"));

                return;
            }

            var player = await GetPlayerAsync(joinChannel: true);

            if (player == null) return;

            var items = playlist.Tracks
                .Select(x => new AudioTrackQueueItem
                {
                    Track = x.ToTrack(),
                    RequestedBy = Context.User,
                    RequestId = Snowflake.Next()
                })
                .ToList();

            if (replace)
                await player.Queue.ClearAsync();

            await RespondAsync(embed: Describe(playlist,
                $"Playlist „{playlist.Name}“ zařazen",
                $"{items.Count} {Tracks(items.Count)}"));

            await player.PlayAsync(items[0]);

            if (items.Count > 1)
                await player.Queue.AddRangeAsync(items.Skip(1));
        }

        [SlashCommand("new", "Starts an empty playlist to fill by searching")]
        public async Task CmdNew(string name)
        {
            if (!await ReadyAsync()) return;

            var created = await Run(() => Playlists.CreateAsync(
                Context.User.Id,
                (Context.User as IGuildUser)?.DisplayName ?? Context.User.Username,
                name));

            if (created == null) return;

            await RespondAsync(embed: AudioEmbeds.Info(
                $"Playlist „{created.Name}“ založen — plň ho přes /playlist add",
                Context.User));
        }

        /// <summary>
        /// Appends whatever the query resolves to, through the same lookup playback uses,
        /// so a phrase, a link or a Spotify page all land the same way they would in the
        /// queue. A playlist expands to its first track rather than all of it - adding
        /// a hundred at once is what /playlist save is for.
        /// </summary>
        [SlashCommand("add", "Searches for a track and appends it to a playlist")]
        public async Task CmdAdd(
            [Autocomplete(typeof(PlaylistNameHandler))] string playlist,
            string query)
        {
            if (!await ReadyAsync()) return;

            var found = await Playlists.FindAsync(playlist, Context.User.Id, Context.Guild.Id);

            if (found == null)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Takový playlist neznám", $"(„{playlist}“)"));

                return;
            }

            // The extractor is the slow half, and Discord stops waiting after three seconds
            await DeferAsync();

            AudioLoadResult result;

            try
            {
                result = await SearchService.LoadAsync(query);
            }
            catch (Exception e) when (e is ProcessException or HttpRequestException)
            {
                await FollowupAsync(ephemeral: true, embed: AudioEmbeds.Error(
                    "Mé jádro nedokázalo navázat spojení se serverem",
                    "(Neznámá chyba)"));

                return;
            }

            if (result.IsFailed || result.Track == null)
            {
                await FollowupAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Nic jsem nenašel", $"(„{query}“)"));

                return;
            }

            var updated = await Run(() => Playlists.AddTrackAsync(
                found.Id, Context.User.Id, result.Track), followup: true);

            if (updated == null) return;

            await FollowupAsync(embed: Describe(updated,
                $"Přidáno do „{updated.Name}“",
                result.Track.Title));
        }

        [SlashCommand("remove", "Removes one track from a playlist of yours")]
        public async Task CmdRemove(
            [Autocomplete(typeof(PlaylistNameHandler))] string playlist,
            [Summary(description: "Position in the playlist, from one")] int position)
        {
            if (!await ReadyAsync()) return;

            var found = await Playlists.FindAsync(playlist, Context.User.Id, Context.Guild.Id);

            if (found == null)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Takový playlist neznám", $"(„{playlist}“)"));

                return;
            }

            var track = found.Tracks.FirstOrDefault(x => x.Position == position - 1);

            if (track == null)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Na téhle pozici nic nemám",
                    $"(Playlist má {found.Tracks.Count} {Tracks(found.Tracks.Count)})"));

                return;
            }

            var updated = await Run(() => Playlists.RemoveTrackAsync(
                found.Id, Context.User.Id, track.Id));

            if (updated == null) return;

            await RespondAsync(embed: AudioEmbeds.Info(
                $"„{track.Title}“ odebráno z playlistu „{updated.Name}“", Context.User));
        }

        [SlashCommand("list", "Shows the playlists you can open here")]
        public async Task CmdList()
        {
            if (!await ReadyAsync()) return;

            var playlists = await Playlists.ListAsync(Context.User.Id, Context.Guild.Id);

            if (playlists.Count == 0)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Info(
                    "Zatím tu nemáš žádný playlist", Context.User));

                return;
            }

            var builder = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Uložené playlisty")
                .WithFooter($"{playlists.Count} {Lists(playlists.Count)}");

            foreach (var playlist in playlists.Take(25))
            {
                var mine = playlist.OwnerId == Context.User.Id;
                var shared = playlist.GuildId != null;

                var note = mine
                    ? shared ? "tvůj, sdílený se serverem" : "tvůj"
                    : $"od {playlist.OwnerName ?? "někoho"}";

                builder.AddField(playlist.Name, $"`{note}`");
            }

            await RespondAsync(embed: builder.Build());
        }

        [SlashCommand("show", "Shows what is in a playlist")]
        public async Task CmdShow([Autocomplete(typeof(PlaylistNameHandler))] string name)
        {
            if (!await ReadyAsync()) return;

            var playlist = await Playlists.FindAsync(name, Context.User.Id, Context.Guild.Id);

            if (playlist == null)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Takový playlist neznám", $"(„{name}“)"));

                return;
            }

            await RespondAsync(embed: Describe(playlist, playlist.Name, null, full: true));
        }

        [SlashCommand("delete", "Throws one of your playlists away")]
        public async Task CmdDelete([Autocomplete(typeof(PlaylistNameHandler))] string name)
        {
            if (!await ReadyAsync()) return;

            var playlist = await Playlists.FindAsync(name, Context.User.Id, Context.Guild.Id);

            if (playlist == null)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Takový playlist neznám", $"(„{name}“)"));

                return;
            }

            var deleted = await Run(() => Playlists.DeleteAsync(playlist.Id, Context.User.Id));

            if (deleted == null) return;

            await RespondAsync(embed: AudioEmbeds.Info(
                $"Playlist „{deleted.Name}“ smazán", Context.User));
        }

        [SlashCommand("share", "Offers one of your playlists to this server, or takes it back")]
        public async Task CmdShare(
            [Autocomplete(typeof(PlaylistNameHandler))] string name,
            bool shared = true)
        {
            if (!await ReadyAsync()) return;

            var playlist = await Playlists.FindAsync(name, Context.User.Id, Context.Guild.Id);

            if (playlist == null)
            {
                await RespondAsync(ephemeral: true, embed: AudioEmbeds.Warning(
                    "Takový playlist neznám", $"(„{name}“)"));

                return;
            }

            var updated = await Run(() => Playlists.ShareAsync(
                playlist.Id, Context.User.Id, shared ? Context.Guild.Id : null));

            if (updated == null) return;

            await RespondAsync(embed: AudioEmbeds.Info(
                shared
                    ? $"Playlist „{updated.Name}“ je teď k dispozici celému serveru"
                    : $"Playlist „{updated.Name}“ je zase jen tvůj",
                Context.User));
        }

        /// <summary>
        /// Runs a store call, turning its refusal into an answer. Every command here does
        /// the same thing with a failure, and none of them can carry on past one.
        /// </summary>
        private async Task<T> Run<T>(Func<Task<T>> action, bool followup = false) where T : class
        {
            try
            {
                return await action();
            }
            catch (PlaylistException e)
            {
                var embed = AudioEmbeds.Warning(e.Message, "(Playlist se nezměnil)");

                if (followup)
                    await FollowupAsync(ephemeral: true, embed: embed);
                else
                    await RespondAsync(ephemeral: true, embed: embed);

                return null;
            }
        }

        private async ValueTask<IAudioPlayer> GetPlayerAsync(bool joinChannel = false)
        {
            var result = await PlayerManager.RetrieveAsync(Context, new AudioPlayerRetrieveOptions
            {
                JoinChannel = joinChannel,
                RequireSameChannel = true
            });

            var refusal = AudioEmbeds.Retrieval(result);

            if (refusal == null)
                return result.Player;

            await RespondAsync(ephemeral: true, embed: refusal);

            return null;
        }

        private static Embed Describe(Playlist playlist, string title, string note, bool full = false)
        {
            var total = TimeSpan.FromMilliseconds(playlist.Tracks.Sum(x => x.DurationMs));

            var builder = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle(title);

            if (!string.IsNullOrEmpty(note))
                builder.WithDescription(note);

            var shown = full ? playlist.Tracks.Take(25) : playlist.Tracks.Take(Preview);
            var listed = 0;

            foreach (var track in shown)
            {
                listed++;

                builder.AddField(
                    $"{track.Position + 1}. {track.Title}",
                    $"`{track.Author ?? "neznámý autor"}`",
                    inline: false);
            }

            var rest = playlist.Tracks.Count - listed;

            builder.WithFooter(rest > 0
                ? $"{playlist.Tracks.Count} {Tracks(playlist.Tracks.Count)} · {Format(total)} · a další {rest}"
                : $"{playlist.Tracks.Count} {Tracks(playlist.Tracks.Count)} · {Format(total)}");

            return builder.Build();
        }

        private static string Format(TimeSpan span)
        {
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours} h {span.Minutes} min"
                : $"{span.Minutes} min";
        }

        private static string Tracks(int count)
        {
            return count == 1 ? "skladba" : count < 5 ? "skladby" : "skladeb";
        }

        private static string Lists(int count)
        {
            return count == 1 ? "playlist" : count < 5 ? "playlisty" : "playlistů";
        }
    }
}
