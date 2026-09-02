using System;
using System.Collections.Generic;
using System.Linq;

using Discord;

namespace NOVAxis.Services.Download
{
    /// <summary>
    /// Everything the download commands render. Mirrors AudioEmbeds so the two features
    /// look like one bot.
    /// </summary>
    public static class DownloadEmbeds
    {
        /// <summary>Discord refuses an embed past these, and a title comes from the source.</summary>
        private const int MaxTitle = 256;
        private const int MaxUrl = 512;

        private const int AccentR = 52, AccentG = 231, AccentB = 231;
        private const int ErrorR = 220, ErrorG = 20, ErrorB = 60;
        private const int WarningR = 255, WarningG = 150, WarningB = 0;

        public static Embed Error(string title, string description)
        {
            return new EmbedBuilder()
                .WithColor(ErrorR, ErrorG, ErrorB)
                .WithDescription(description)
                .WithTitle(title)
                .Build();
        }

        public static Embed Warning(string title, string description)
        {
            return new EmbedBuilder()
                .WithColor(WarningR, WarningG, WarningB)
                .WithDescription(description)
                .WithTitle(title)
                .Build();
        }

        /// <summary>What was found, while the person picks a format.</summary>
        public static Embed Found(string title, string url, string thumbnail, TimeSpan duration, DownloadQuota quota)
        {
            var builder = new EmbedBuilder()
                .WithColor(AccentR, AccentG, AccentB)
                .WithAuthor("Nalezeno:")
                .WithTitle(Trim(title, MaxTitle))
                .WithUrl(Link(url))
                .WithThumbnailUrl(thumbnail);

            if (duration > TimeSpan.Zero)
                builder.AddField("Délka:", $"`{FormatDuration(duration)}`", true);

            builder.AddField("Zbývá stažení:", $"`{quota.Remaining}/{quota.Limit}`", true);

            return builder.Build();
        }

        public static Embed Preparing(DownloadRecord record)
        {
            return new EmbedBuilder()
                .WithColor(AccentR, AccentG, AccentB)
                .WithAuthor("Stahuji...")
                .WithTitle(Trim(record.Title, MaxTitle))
                .WithUrl(Link(record.SourceUrl))
                .WithDescription("Až bude soubor připravený, upravím tuhle zprávu.")
                .AddField("Formát:", $"`{record.FormatLabel}`", true)
                .Build();
        }

        public static Embed Ready(DownloadRecord record, IReadOnlyList<string> freed = null)
        {
            var builder = new EmbedBuilder()
                .WithColor(AccentR, AccentG, AccentB)
                .WithAuthor("Připraveno ke stažení:")
                .WithTitle(Trim(record.Title, MaxTitle))
                .WithUrl(Link(record.SourceUrl))
                .AddField("Formát:", $"`{record.FormatLabel}`", true)
                .AddField("Velikost:", $"`{FormatSize(record.Size)}`", true)
                .AddField("Odkaz platí do:", $"<t:{record.ExpiresAt.ToUnixTimeSeconds()}:t>", true)
                .WithFooter("Odkaz je jen pro tebe - na webu musíš být přihlášený.");

            // Room was made out of their own older links, so say which rather than
            // leaving them to find a link they were given has quietly stopped working
            if (freed is { Count: > 0 })
            {
                builder.AddField($"Uvolnil jsem místo, vypršelo:",
                    string.Join('\n', freed.Select(t => $"· {Trim(t, MaxFreedTitle)}")));
            }

            return builder.Build();
        }

        /// <summary>A retired title has to leave room for several of them in one field.</summary>
        private const int MaxFreedTitle = 80;

        public static Embed Failed(string title, string reason)
        {
            return new EmbedBuilder()
                .WithColor(ErrorR, ErrorG, ErrorB)
                .WithAuthor("Stahování se nezdařilo.")
                .WithTitle(Trim(title, MaxTitle))
                .WithDescription(reason)
                .Build();
        }

        /// <summary>
        /// The pickup page, never the file itself: fetching the file needs a session, and a
        /// direct link would meet anyone not signed in with a bare 401.
        /// </summary>
        public static ButtonBuilder DownloadButton(string url)
        {
            return new ButtonBuilder()
                .WithLabel("Stáhnout")
                .WithEmote(new Emoji("📥"))
                .WithStyle(ButtonStyle.Link)
                .WithUrl(url);
        }

        /// <summary>
        /// The format menu. Anything over the limit is left out rather than shown greyed
        /// out - a select option cannot be disabled, and picking a doomed one would cost
        /// the person a slot for nothing.
        /// </summary>
        public static MessageComponent FormatMenu(
            ulong interactionId, DownloadKind kind, IReadOnlyList<DownloadChoice> choices, int max)
        {
            var options = choices
                .Where(c => c.WithinLimit)
                .Take(max)
                .Select(c => new SelectMenuOptionBuilder()
                    .WithLabel(Trim(c.Label, 100))
                    .WithDescription(c.Size.HasValue ? FormatSize(c.Size.Value) : "velikost neznámá")
                    .WithValue(c.Id))
                .ToList();

            var placeholder = kind == DownloadKind.Video
                ? "Vyber kvalitu videa"
                : "Vyber formát zvuku";

            return new ComponentBuilder()
                .WithSelectMenu($"download_format_{interactionId},{kind}", options, placeholder)
                .Build();
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "?";

            return bytes >= 1024 * 1024
                ? $"{bytes / 1024d / 1024d:0.#} MB"
                : $"{bytes / 1024d:0} kB";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration >= TimeSpan.FromHours(1)
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");
        }

        /// <summary>A url Discord will accept, or none at all rather than a rejected embed.</summary>
        private static string Link(string url)
        {
            return !string.IsNullOrEmpty(url) && url.Length <= MaxUrl ? url : null;
        }

        private static string Trim(string value, int length)
        {
            if (string.IsNullOrEmpty(value))
                return "?";

            return value.Length <= length ? value : value[..(length - 1)] + "…";
        }
    }
}
