using System;
using System.Collections.Generic;
using Discord;

namespace NOVAxis.Core
{
    public class DiscordOptions
    {
        public const string Key = "Discord";

        public int? TotalShards { get; set; }
        public string LoginToken { get; set; }
        public DiscordActivityOptions Activity { get; set; } = new();
        public DiscordInteractionOptions Interactions { get; set; } = new();
    }

    public class DiscordInteractionOptions
    {
        public const string Key = "Discord:Interactions";

        public bool RegisterGlobally { get; set; }
        public ulong RegisterToGuild { get; set; }
    }

    public class DiscordActivityOptions
    {
        public const string Key = "Discord:Activity";

        public string Online { get; set; } = "pohyb atomů";
        public string Afk { get; set; } = "ochlazování jádra";
        public string Offline { get; set; } = "repair/reboot jádra";
        public ActivityType ActivityType { get; set; } = ActivityType.Listening;
        public UserStatus UserStatus { get; set; } = UserStatus.Online;
    }

    public class CacheOptions
    {
        public const string Key = "Cache";

        public TimeSpan? AbsoluteExpiration { get; set; }
        public TimeSpan? RelativeExpiration { get; set; }
    }

    public class DatabaseOptions
    {
        public const string Key = "Database";

        public bool Active { get; set; }
        public string DbType { get; set; }
        public string DbHost { get; set; }
        public ushort DbPort { get; set; }
        public string DbUsername { get; set; }
        public string DbPassword { get; set; }
        public string DbName { get; set; }
    }

    public enum AudioBackend
    {
        YtDlp,
        Lavalink
    }

    public class AudioOptions
    {
        public const string Key = "Audio";

        public bool Active { get; set; } = true;
        public bool SelfDeaf { get; set; } = true;
        public AudioBackend Backend { get; set; } = AudioBackend.YtDlp;
        public AudioTimeoutOptions Timeout { get; set; } = new();
        public AudioLavalinkOptions Lavalink { get; set; } = new();
        public AudioYtDlpOptions YtDlp { get; set; } = new();
    }

    public class AudioTimeoutOptions
    {
        public const string Key = "Audio:Timeout";

        public TimeSpan IdleInactivity { get; set; }
        public TimeSpan UsersInactivity { get; set; }
    }

    public class AudioLavalinkOptions
    {
        public const string Key = "Audio:Lavalink";

        public string Host { get; set; } = "localhost";
        public ushort Port { get; set; } = 2333;
        public string Login { get; set; } = "youshallnotpass";
    }

    public class AudioYtDlpOptions
    {
        public const string Key = "Audio:YtDlp";

        public string ExecutablePath { get; set; } = "yt-dlp";
        public string FfmpegPath { get; set; } = "ffmpeg";
        public string Format { get; set; } = "bestaudio[abr<=?137]/bestaudio/best";
        public string CookiesFile { get; set; }
        public string UserAgent { get; set; }
        public List<string> ExtraArguments { get; set; } = [];
        public int MaxPlaylistSize { get; set; } = 500;
        public bool Prefetch { get; set; } = true;
        public TimeSpan ResolveTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxConcurrentLookups { get; set; } = 4;

        /// <summary>
        /// Routes every yt-dlp fetch through a loopback proxy which refuses addresses on the
        /// host's own network. Turn it off only where the media genuinely lives on the LAN -
        /// with it off, a link anyone can paste can reach anything the bot can.
        /// </summary>
        public bool RestrictNetwork { get; set; } = true;
    }

    public class AnthropicOptions
    {
        public const string Key = "Anthropic";

        public string ApiKey { get; set; }
    }

    public class WebOptions
    {
        public const string Key = "Web";

        public bool Active { get; set; }
        public string ListenAddress { get; set; } = "http://0.0.0.0:5000";

        /// <summary>
        /// The address users reach the app at, e.g. "https://novaxis.example.com".
        /// The OAuth redirect and every link handed out point here, not at the
        /// address the server listens on.
        /// </summary>
        public string PublicUrl { get; set; }

        /// <summary>
        /// Where the data-protection key ring lives. Sessions survive a restart
        /// only as long as these keys do.
        /// </summary>
        public string KeyPath { get; set; } = "keys";

        public WebOAuthOptions OAuth { get; set; } = new();

        /// <summary>
        /// The address of a guild's web player, or null while the web app is off
        /// or unreachable from outside - the links lead nowhere then.
        /// </summary>
        public string GetPlayerUrl(ulong guildId)
        {
            return Active && !string.IsNullOrEmpty(PublicUrl)
                ? $"{PublicUrl.TrimEnd('/')}/g/{guildId}"
                : null;
        }

        /// <summary>
        /// The page a prepared download is picked up from, or null while the web app
        /// is off or unreachable from outside. It points at the frontend rather than
        /// at the file itself: fetching the file needs a session, and a bare link
        /// would greet anyone not signed in with a 401 and no way forward.
        /// </summary>
        public string GetDownloadUrl(ulong downloadId)
        {
            return Active && !string.IsNullOrEmpty(PublicUrl)
                ? $"{PublicUrl.TrimEnd('/')}/downloads?d={downloadId}"
                : null;
        }
    }

    public class WebOAuthOptions
    {
        public const string Key = "Web:OAuth";

        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
    }

    public class DownloadOptions
    {
        public const string Key = "Download";

        public bool Active { get; set; } = true;

        /// <summary>
        /// Where prepared files are kept. Emptied on every start: the records live in
        /// memory, so anything already on disk belongs to a process which is gone.
        /// </summary>
        public string OutputFolder { get; set; } = "downloads";

        /// <summary>How long a prepared link stays valid.</summary>
        public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(1);
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>The ceiling for a single download.</summary>
        public long MaxFileSize { get; set; } = 104857600;

        /// <summary>
        /// How far past <see cref="MaxFileSize"/> the directory may grow before the
        /// runtime watchdog kills yt-dlp. A merge holds the video stream, the audio
        /// stream and the muxed result at once, so the headroom is not optional.
        /// </summary>
        public double SizeWatchdogFactor { get; set; } = 2.5;

        /// <summary>How many downloads one user may take per <see cref="QuotaWindow"/>.</summary>
        public int MaxPerWindow { get; set; } = 10;
        public TimeSpan QuotaWindow { get; set; } = TimeSpan.FromHours(1);

        /// <summary>The ceiling across every user. Keep it under the real disk.</summary>
        public long OutputFolderLimit { get; set; } = 3221225472;
        public int MaxConcurrentDownloads { get; set; } = 2;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>How long the output may stop growing before the download is given up on.</summary>
        public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>The selector used when the caller did not name a format.</summary>
        public string VideoFormat { get; set; } = "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/b";
        public string MergeOutputFormat { get; set; } = "mp4";

        /// <summary>
        /// Deliberately no wav or flac: --max-filesize weighs the source stream, and
        /// extracting to PCM can turn a few megabytes into a few hundred.
        /// </summary>
        public List<string> AudioFormats { get; set; } = ["mp3", "m4a", "opus"];

        /// <summary>Discord allows no more than 25 options in a select menu.</summary>
        public int MaxFormatChoices { get; set; } = 25;
    }
}
