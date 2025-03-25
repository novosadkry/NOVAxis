using System.Threading.Tasks;

using Discord;
using Discord.Interactions;

using NOVAxis.Preconditions;
using NOVAxis.Services.Download;
using NOVAxis.Services.WebServer;

namespace NOVAxis.Modules.Download
{
    [Cooldown(10)]
    [RequireContext(ContextType.Guild)]
    [Group("download", "Various commands for video and audio processing")]
    public class DownloadModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public DownloadService DownloadService { get; set; }
        public WebServerService WebServerService { get; set; }

        [SlashCommand("video", "Downloads a given video")]
        public async Task Video(string url)
        {
            await DeferAsync(ephemeral: true);

            var metadata = await DownloadService.DownloadVideoMetadata(url);

            await FollowupAsync(
                ephemeral: true,
                embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle(metadata.Title)
                    .WithUrl(metadata.WebpageUrl)
                    .WithDescription("Až bude video připravené, pošlu ti odkaz ke stažení.")
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());

            var uuid = await DownloadService.DownloadVideo(Context.User, url);
            var uri = await WebServerService.ServeVideoDownload(uuid);

            await Context.User.SendMessageAsync(
                embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle(metadata.Title)
                    .WithUrl(uri)
                    .WithDescription("Tvoje video je připravené k stažení.")
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());
        }
    }
}
