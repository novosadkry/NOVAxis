using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using Discord;
using Discord.Interactions;

using NOVAxis.Core;
using NOVAxis.Preconditions;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Services.Download;
using NOVAxis.Utilities;

namespace NOVAxis.Modules.Download
{
    [Cooldown(10)]
    [RequireContext(ContextType.Guild)]
    [Group("download", "Various commands for video and audio processing")]
    public class DownloadModule : InteractionModuleBase<ShardedInteractionContext>
    {
        /// <summary>What was found, held between showing the menu and a choice being made.</summary>
        private sealed record Pending(ulong OwnerId, AudioTrack Media);

        public DownloadService DownloadService { get; set; }
        public InteractionCache InteractionCache { get; set; }
        public IOptions<WebOptions> WebOptions { get; set; }

        [SlashCommand("video", "Downloads a given video")]
        public Task CmdDownloadVideo(string url)
        {
            return HandleRequest(url, DownloadKind.Video);
        }

        [SlashCommand("audio", "Downloads a given audio")]
        public Task CmdDownloadAudio(string url)
        {
            return HandleRequest(url, DownloadKind.Audio);
        }

        private async Task HandleRequest(string url, DownloadKind kind)
        {
            await DeferAsync(ephemeral: true);

            if (!Available())
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Warning(
                    "Stahování není na tomto jádru zapnuto",
                    "(Služba není dostupná)"));

                return;
            }

            var quota = DownloadService.QuotaFor(Context.User.Id);

            if (quota.Remaining <= 0)
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Warning(
                    "Vyčerpal jsi svůj limit stahování",
                    quota.ResetsAt.HasValue
                        ? $"(Další slot se uvolní <t:{quota.ResetsAt.Value.ToUnixTimeSeconds()}:R>)"
                        : "(Zkus to prosím později)"));

                return;
            }

            AudioTrack media;

            try
            {
                media = await DownloadService.ProbeAsync(url);
            }
            catch (DownloadException e)
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Error(
                    "Tenhle odkaz načíst nedokážu", $"({e.Message})"));

                return;
            }
            catch (Exception e) when (e is ProcessException or HttpRequestException)
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Error(
                    "Mé jádro nedokázalo navázat spojení", "(Služba není dostupná)"));

                return;
            }

            var choices = DownloadService.ChoicesFor(media, kind);
            var settings = DownloadService.Settings;

            if (!choices.Any(c => c.WithinLimit))
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Warning(
                    "Žádný z nabízených formátů se nevejde do limitu",
                    $"(Maximum je {settings.MaxFileSize / 1024 / 1024} MB)"));

                return;
            }

            var id = InteractionCache.Store(new Pending(Context.User.Id, media));

            await FollowupAsync(
                ephemeral: true,
                embed: DownloadEmbeds.Found(
                    media.Title,
                    media.Uri?.AbsoluteUri,
                    media.ArtworkUri?.AbsoluteUri,
                    media.Duration,
                    quota),
                components: DownloadEmbeds.FormatMenu(id, kind, choices, settings.MaxFormatChoices));
        }

        [ComponentInteraction("download_format_*,*", true)]
        public async Task OnFormatSelected(ulong interactionId, string kindName, string formatId)
        {
            await DeferAsync(ephemeral: true);

            if (!Enum.TryParse<DownloadKind>(kindName, ignoreCase: true, out var kind))
                return;

            // Scoped to whoever asked: the id travels in the component's own identifier, and
            // a menu is only ever shown to one person
            if (InteractionCache[interactionId] is not Pending pending || pending.OwnerId != Context.User.Id)
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Error(
                    "Mé jádro přerušilo čekání na lidský vstup", "(Vypršel časový limit)"));

                return;
            }

            var media = pending.Media;

            DownloadRecord record;

            try
            {
                record = await DownloadService.RequestAsync(
                    Context.User.Id, media.Uri?.AbsoluteUri ?? media.Title, kind, formatId, media);
            }
            catch (DownloadException e)
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Error(
                    "Stahování jsem nespustil", $"({e.Message})"));

                return;
            }
            catch (Exception e) when (e is ProcessException or HttpRequestException)
            {
                await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Error(
                    "Mé jádro nedokázalo navázat spojení", "(Služba není dostupná)"));

                return;
            }

            var progress = await FollowupAsync(ephemeral: true, embed: DownloadEmbeds.Preparing(record));

            try
            {
                await record.Worker;
            }
            catch (Exception) { /* the record carries the outcome */ }

            await ReportAsync(progress, record);
        }

        /// <summary>
        /// Updates the message the person is already looking at. An ephemeral one only stays
        /// editable while its interaction token lives, which outlasts the download timeout -
        /// but not by so much that the fallback is pointless.
        /// </summary>
        private async Task ReportAsync(IUserMessage progress, DownloadRecord record)
        {
            var url = WebOptions.Value.GetDownloadUrl(record.Id);

            var embed = record.State == DownloadState.Ready
                ? DownloadEmbeds.Ready(record, record.Freed)
                : DownloadEmbeds.Failed(record.Title, $"({record.Error ?? "Zkus to prosím znovu"})");

            var components = record.State == DownloadState.Ready && url != null
                ? new ComponentBuilder().WithButton(DownloadEmbeds.DownloadButton(url)).Build()
                : null;

            try
            {
                await progress.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = components;
                });
            }
            catch (Exception)
            {
                // The token expired, so the only way left to reach them is a direct message
                try
                {
                    await Context.User.SendMessageAsync(embed: embed, components: components);
                }
                catch (Exception) { /* their messages are closed; the web page still has it */ }
            }
        }

        private bool Available()
        {
            return DownloadService.Active && WebOptions.Value.GetDownloadUrl(0) != null;
        }

    }
}
