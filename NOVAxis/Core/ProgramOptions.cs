using System;
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
        public string Format { get; set; } = "bestaudio[abr<=?128]/bestaudio/best";
        public string CookiesFile { get; set; }
        public string UserAgent { get; set; }
        public int MaxPlaylistSize { get; set; } = 500;
        public bool Prefetch { get; set; } = true;
        public TimeSpan ResolveTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    public class AnthropicOptions
    {
        public const string Key = "Anthropic";

        public string ApiKey { get; set; }
    }
}
