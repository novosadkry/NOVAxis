using System;
using Microsoft.Extensions.Logging;

using Discord;

namespace NOVAxis.Core
{
    public class InteractionOptions
    {
        public const string Interaction = "Interaction";

        public class CommandsOptions
        {
            public const string Commands = "Interaction:Commands";

            public bool RegisterGlobally { get; set; }
            public ulong RegisterToGuild { get; set; }
        }

        public CommandsOptions Commands { get; set; } = new();
    }

    public class CacheOptions
    {
        public const string Cache = "Cache";

        public TimeSpan? AbsoluteExpiration { get; set; }
        public TimeSpan? RelativeExpiration { get; set; }
    }

    public class ActivityOptions
    {
        public const string Activity = "Activity";

        public string Online { get; set; } = "pohyb atomů";
        public string Afk { get; set; } = "ochlazování jádra";
        public string Offline { get; set; } = "repair/reboot jádra";
        public ActivityType ActivityType { get; set; } = ActivityType.Listening;
        public UserStatus UserStatus { get; set; } = UserStatus.Online;
    }

    public class DatabaseOptions
    {
        public const string Database = "Database";

        public bool Active { get; set; }
        public string DbType { get; set; }
        public string DbHost { get; set; }
        public ushort DbPort { get; set; }
        public string DbUsername { get; set; }
        public string DbPassword { get; set; }
        public string DbName { get; set; }
    }

    public class LavalinkOptions
    {
        public const string Lavalink = "Lavalink";

        public string Host { get; set; } = "localhost";
        public ushort Port { get; set; } = 2333;
        public string Login { get; set; } = "youshallnotpass";
    }

    public class LogOptions
    {
        public const string Log = "Log";

        public bool Active { get; set; } = true;
        public LogLevel Level { get; set; } = LogLevel.Debug;
    }

    public class AudioOptions
    {
        public const string Audio = "Audio";

        public class TimeoutOptions
        {
            public const string Timeout = "Audio:Timeout";

            public TimeSpan Idle { get; set; }
            public TimeSpan Paused { get; set; }
        }

        public bool SelfDeaf { get; set; } = true;
        public TimeoutOptions Timeout { get; set; } = new();
    }

    public class ProgramOptions
    {
        public int? TotalShards { get; set; }
        public string LoginToken { get; set; }
        public LogOptions Log { get; set; } = new();
        public CacheOptions Cache { get; set; } = new();
        public AudioOptions Audio { get; set; } = new();
        public ActivityOptions Activity { get; set; } = new();
        public LavalinkOptions Lavalink { get; set; } = new();
        public DatabaseOptions Database { get; set; } = new();
        public InteractionOptions Interaction { get; set; } = new();
    }
}
