using System;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Interactions;

using NOVAxis.Utilities;
using NOVAxis.Preconditions;
using NOVAxis.Services.Download;
using NOVAxis.Services.WebServer;

using YoutubeDLSharp.Options;
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
                        $"download_video_format_select_{id}",
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

        [SlashCommand("audio", "Downloads a given audio")]
        public async Task Audio(string url)
        {
            await DeferAsync(ephemeral: true);

            var metadata = await DownloadService.DownloadVideoMetadata(url);
            var id = InteractionCache.Store(metadata);

            var selectMenuOptions = Enum.GetValues<AudioConversionFormat>()
                .Select(f => new SelectMenuOptionBuilder()
                    .WithLabel(f.ToString())
                    .WithValue(((int)f).ToString()))
                .ToList();

            await FollowupAsync(
                ephemeral: true,
                components: new ComponentBuilder()
                    .WithSelectMenu(
                        $"download_audio_format_select_{id}",
                        selectMenuOptions,
                        "Vyber formát, ve kterém chceš audio stáhnout.")
                    .Build(),
                embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle(metadata.Title)
                    .WithUrl(metadata.WebpageUrl)
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());
        }

        [ComponentInteraction("download_video_format_select_*", true)]
        public async Task VideoFormatSelect(ulong interactionId, string selectedFormat)
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
            var uri = await WebServerService.ServeDownload(uuid);

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

        [ComponentInteraction("download_audio_format_select_*", true)]
        public async Task AudioFormatSelect(ulong interactionId, string selectedFormat)
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
                    .WithDescription("Až bude audio připravené, pošlu ti odkaz ke stažení.")
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());

            var format = Enum.Parse<AudioConversionFormat>(selectedFormat);
            var uuid = await DownloadService.DownloadAudio(Context.User, metadata.WebpageUrl, format);
            var uri = await WebServerService.ServeDownload(uuid);

            await Context.User.SendMessageAsync(
                embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle(metadata.Title)
                    .WithUrl(uri)
                    .WithAuthor("Tvoje audio je připravené k stažení.")
                    .WithDescription(format.ToString())
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());
        }
    }
}
