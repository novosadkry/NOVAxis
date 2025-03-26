using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Interactions;

using NOVAxis.Utilities;
using NOVAxis.Preconditions;
using NOVAxis.Services.Download;
using NOVAxis.Services.WebServer;

using YoutubeDLSharp.Metadata;

namespace NOVAxis.Modules.Download
{
    [Cooldown(10)]
    [RequireContext(ContextType.Guild)]
    [Group("download", "Various commands for video and audio processing")]
    public class DownloadModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public DownloadService DownloadService { get; set; }
        public WebServerService WebServerService { get; set; }
        public InteractionCache InteractionCache { get; set; }

        [SlashCommand("video", "Downloads a given video")]
        public async Task Video(string url)
        {
            await DeferAsync(ephemeral: true);

            var metadata = await DownloadService.DownloadVideoMetadata(url);
            var id = InteractionCache.Store(metadata);

            var selectMenuOptions = metadata.Formats
                .Select(f => new SelectMenuOptionBuilder()
                    .WithLabel(f.ToString())
                    .WithValue(f.FormatId))
                .Reverse()
                .ToList();

            await FollowupAsync(
                ephemeral: true,
                components: new ComponentBuilder()
                    .WithSelectMenu(
                        $"download_format_select_{id}",
                        selectMenuOptions,
                        "Vyber formát, ve kterém chceš video stáhnout.")
                    .Build(),
                embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle(metadata.Title)
                    .WithUrl(metadata.WebpageUrl)
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());
        }

        [ComponentInteraction("download_format_select_*", true)]
        public async Task FormatSelect(ulong interactionId, string selectedFormat)
        {
            await DeferAsync(ephemeral: true);
            var interaction = (IComponentInteraction)Context.Interaction;

            if (InteractionCache[interactionId] is not VideoData metadata)
            {
                await FollowupAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Vypršel časový limit)")
                    .WithTitle("Mé jádro přerušilo čekání na lidský vstup")
                    .Build());

                return;
            }

            await interaction.FollowupAsync(
                ephemeral: true,
                embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle(metadata.Title)
                    .WithUrl(metadata.WebpageUrl)
                    .WithDescription("Až bude video připravené, pošlu ti odkaz ke stažení.")
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());

            var format = metadata.Formats.FirstOrDefault(f => f.FormatId == selectedFormat);
            var uuid = await DownloadService.DownloadVideo(Context.User, metadata.WebpageUrl, selectedFormat);
            var uri = await WebServerService.ServeVideoDownload(uuid);

            await Context.User.SendMessageAsync(
                embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle(metadata.Title)
                    .WithUrl(uri)
                    .WithAuthor("Tvoje video je připravené k stažení.")
                    .WithDescription(format?.ToString())
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());
        }
    }
}
