using System;
using System.IO;
using System.Threading.Tasks;

using Discord;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace NOVAxis.Core
{
    public class ProgramConfig
    {
        public const string ConfigPath = @"config.json";

        public static ProgramConfig Default => new()
        {
            LoginToken = "INSERT_LOGINTOKEN_HERE",
            TotalShards = null,

            Interaction = new InteractionObject
            {
                DefaultPrefix = "~",
                Commands = new InteractionObject.CommandsObject
                {
                    RegisterGlobally = true,
                    RegisterToGuild = 0
                },
                Cache = new CacheObject
                {
                    AbsoluteExpiration = TimeSpan.FromHours(24),
                    RelativeExpiration = TimeSpan.FromHours(12)
                }
            },

            Activity = new ActivityObject
            {
                Online = "pohyb atomů",
                Afk = "ochlazování jádra",
                Offline = "repair/reboot jádra",
                ActivityType = ActivityType.Listening,
                UserStatus = UserStatus.Online
            },

            Log = new LogObject
            {
                Active = true,
                Level = LogLevel.Debug
            },

            Lavalink = new LavalinkObject
            {
                Host = "localhost",
                Port = 2333,
                Login = "123",
                SelfDeaf = true
            },

            Database = new DatabaseObject
            {
                Active = true,
                DbType = "sqlite",
                DbHost = "localhost",
                DbPort = 0,
                DbUsername = "novaxis",
                DbPassword = "123",
                DbName = "novaxis"
            },

            Audio = new AudioObject
            {
                Timeout = new AudioObject.TimeoutObject
                {
                    Idle = TimeSpan.FromSeconds(30),
                    Paused = TimeSpan.FromMinutes(2)
                },
                Cache = new CacheObject
                {
                    AbsoluteExpiration = null,
                    RelativeExpiration = TimeSpan.FromHours(12)
                }
            }
        };

        public struct InteractionObject
        {
            public struct CommandsObject
            {
                public bool RegisterGlobally { get; set; }
                public ulong RegisterToGuild { get; set; }
            }

            public string DefaultPrefix { get; set; }
            public CommandsObject Commands { get; set; }
            public CacheObject Cache { get; set; }
        }

        public struct ActivityObject
        {
            public string Online { get; set; }
            public string Afk { get; set; }
            public string Offline { get; set; }
            public ActivityType ActivityType { get; set; }
            public UserStatus UserStatus { get; set; }
        }

        public struct DatabaseObject
        {
            public bool Active { get; set; }
            public string DbType { get; set; }
            public string DbHost { get; set; }
            public ushort DbPort { get; set; }
            public string DbUsername { get; set; }
            public string DbPassword { get; set; }
            public string DbName { get; set; }
        }

        public struct LavalinkObject
        {
            public string Host { get; set; }
            public ushort Port { get; set; }
            public string Login { get; set; }
            public bool SelfDeaf { get; set; }
        }

        public struct LogObject
        {
            public bool Active { get; set; }
            public LogLevel Level { get; set; }
        }

        public struct AudioObject
        {
            public struct TimeoutObject
            {
                public TimeSpan Idle { get; set; }
                public TimeSpan Paused { get; set; }
            }

            public TimeoutObject Timeout { get; set; }
            public CacheObject Cache { get; set; }
        }

        public struct CacheObject
        {
            public TimeSpan? AbsoluteExpiration { get; set; }
            public TimeSpan? RelativeExpiration { get; set; }
        }

        public string            LoginToken  { get; set; }
        public int?              TotalShards { get; set; }
        public InteractionObject Interaction { get; set; }
        public ActivityObject    Activity    { get; set; }
        public LogObject         Log         { get; set; }
        public LavalinkObject    Lavalink    { get; set; }
        public DatabaseObject    Database    { get; set; }
        public AudioObject       Audio       { get; set; }

        public static async Task<ProgramConfig> LoadConfig()
        {
            try
            {
                return JsonConvert.DeserializeObject<ProgramConfig>(await File.ReadAllTextAsync(ConfigPath));
            }

            catch (FileNotFoundException)
            {
                await ResetConfig();
                return Default;
            }
        }

        public static async Task SaveConfig(ProgramConfig config)
        {
            await File.WriteAllTextAsync(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        public static async Task ResetConfig()
        {
            await SaveConfig(Default);
        }
    }
}
