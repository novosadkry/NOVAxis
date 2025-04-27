using System;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Audio;
using Discord.Interactions;

using NOVAxis.Utilities;
using NOVAxis.Preconditions;
using NOVAxis.Services.Download;
using NOVAxis.Services.WebServer;

using YoutubeDLSharp.Options;
using YoutubeDLSharp.Metadata;

using AudioStream = NOVAxis.Services.Audio.AudioStream;

namespace NOVAxis.Modules.Download
{
    [Cooldown(10)]
    [RequireContext(ContextType.Guild)]
    [Group("download", "Various commands for video and audio processing")]
    public class DownloadModule : InteractionModuleBase<ShardedInteractionContext>
    {
        private enum DownloadType
        {
            Video,
            Audio
        }

        public DownloadService DownloadService { get; set; }
        public WebServerService WebServerService { get; set; }
        public InteractionCache InteractionCache { get; set; }

        [SlashCommand("video", "Downloads a given video")]
        public async Task CmdVideo(string url)
        {
            await HandleDownloadRequest(url, DownloadType.Video);
        }

        [SlashCommand("audio", "Downloads a given audio")]
        public async Task CmdAudio(string url)
        {
            await HandleDownloadRequest(url, DownloadType.Audio);
        }

        private async Task HandleDownloadRequest(string url, DownloadType downloadType)
        {
            await DeferAsync(ephemeral: true);

            try
            {
                var metadata = await DownloadService.DownloadVideoMetadata(url);
                var id = InteractionCache.Store(metadata);
                var downloadTypeString = downloadType.ToString().ToLower();

                var selectMenuOptions = downloadType switch
                {
                    DownloadType.Video => metadata.Formats
                        .Select(f => new SelectMenuOptionBuilder()
                            .WithLabel(f.ToString())
                            .WithValue(f.FormatId))
                        .Reverse()
                        .ToList(),

                    DownloadType.Audio => Enum.GetValues<AudioConversionFormat>()
                        .Select(f => new SelectMenuOptionBuilder()
                            .WithLabel(f.ToString())
                            .WithValue(((int)f).ToString()))
                        .ToList(),

                    _ => throw new ArgumentException("Invalid download type")
                };

                await FollowupAsync(
                    ephemeral: true,
                    components: new ComponentBuilder()
                        .WithSelectMenu(
                            $"download_format_select_{id},{downloadTypeString}",
                            selectMenuOptions,
                            $"Vyber formát, ve kterém chceš {downloadTypeString} stáhnout.")
                        .Build(),
                    embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle(metadata.Title)
                        .WithUrl(metadata.WebpageUrl)
                        .WithThumbnailUrl(metadata.Thumbnail)
                        .Build());
            }
            catch (DownloadException e)
            {
                await FollowupAsync(
                    embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithAuthor("Při načítání došlo k chybě.")
                        .WithDescription($"```{string.Join('\n', e.ErrorOutput)}```")
                        .Build());
            }
        }

        [ComponentInteraction("download_format_select_*,*", true)]
        public async Task VideoFormatSelect(ulong interactionId, string downloadTypeString, string selectedFormat)
        {
            var downloadType = downloadTypeString switch
            {
                "video" => DownloadType.Video,
                "audio" => DownloadType.Audio,
                _ => throw new ArgumentException("Invalid download type")
            };

            await HandleFormatSelect(interactionId, downloadType, selectedFormat);
        }

        private async Task HandleFormatSelect(ulong interactionId, DownloadType downloadType, string selectedFormat)
        {
            await DeferAsync(ephemeral: true);

            var interaction = (IComponentInteraction)Context.Interaction;
            var downloadTypeString = downloadType.ToString().ToLower();

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
                    .WithDescription($"Až bude {downloadTypeString} připravené, pošlu ti odkaz ke stažení.")
                    .WithThumbnailUrl(metadata.Thumbnail)
                    .Build());

            try
            {
                Guid uuid;
                string description;

                if (downloadType == DownloadType.Video)
                {
                    var format = metadata.Formats.FirstOrDefault(f => f.FormatId == selectedFormat);
                    uuid = await DownloadService.DownloadVideo(Context.User, metadata.WebpageUrl, selectedFormat);
                    description = format?.ToString();
                }
                else
                {
                    var format = Enum.Parse<AudioConversionFormat>(selectedFormat);
                    uuid = await DownloadService.DownloadAudio(Context.User, metadata.WebpageUrl, format);
                    description = format.ToString();
                }

                var uri = await WebServerService.ServeDownload(uuid);

                await Context.User.SendMessageAsync(
                    embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle(metadata.Title)
                        .WithUrl(uri)
                        .WithAuthor($"Tvoje {downloadTypeString} je připravené ke stažení.")
                        .WithDescription(description)
                        .WithThumbnailUrl(metadata.Thumbnail)
                        .Build());
            }
            catch (DownloadException e)
            {
                await Context.User.SendMessageAsync(
                    embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithTitle(metadata.Title)
                        .WithUrl(metadata.WebpageUrl)
                        .WithAuthor("Při stahování došlo k chybě.")
                        .WithDescription($"```{string.Join('\n', e.ErrorOutput)}```")
                        .WithThumbnailUrl(metadata.Thumbnail)
                        .Build());
            }
        }

        [SlashCommand("play", "Streams a given audio or video in voice channel")]
        public async Task CmdPlay(string url)
        {
            var voiceChannel = ((IGuildUser)Context.User).VoiceChannel;
            var audioClient = await voiceChannel.ConnectAsync();

            var stream = new AudioStream(url);
            stream.Start();
            stream.PipeTo(audioClient.CreatePCMStream(AudioApplication.Mixed));

            audioClient.Disconnected += _ =>
            {
                stream.Dispose();
                audioClient.Dispose();
                return Task.CompletedTask;
            };
        }
    }
}
