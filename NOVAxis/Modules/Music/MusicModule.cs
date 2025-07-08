using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using NOVAxis.Services.Music;

namespace NOVAxis.Modules.Music
{
    [RequireContext(ContextType.Guild)]
    [Group("music", "Music related commands")]
    public class MusicModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public MusicService MusicService { get; set; }

        [SlashCommand("play", "Plays a song from a given URL")]
        public async Task PlayAsync(string input)
        {
            await DeferAsync();

            if (Context.User is IVoiceState { VoiceChannel: not null } vs)
            {
                await MusicService.JoinAsync(vs.VoiceChannel);
            }
            else
            {
                await FollowupAsync("You must be in a voice channel first.");
                return;
            }

            var song = await MusicService.SearchAsync(Context.User, input);

            if (song == null)
            {
                await FollowupAsync("Could not find a song with that input.");
                return;
            }

            MusicService.Enqueue(song);

            await FollowupAsync($"Queued: {song.Title}");
        }

        [SlashCommand("skip", "Skips the current song")]
        public async Task SkipAsync()
        {
            await MusicService.SkipAsync();
            await RespondAsync("Skipped.");
        }

        [SlashCommand("stop", "Stops playback and clears the queue")]
        public async Task StopAsync()
        {
            await MusicService.StopAsync();
            await RespondAsync("Stopped playback and cleared queue.");
        }
    }
}
