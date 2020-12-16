﻿using System;
using System.IO;
using System.Threading.Tasks;

using Discord;
using Newtonsoft.Json;

namespace NOVAxis.Core
{
    public class ProgramConfig
    {
        public const string ConfigPath = @"config.json";

        public static ProgramConfig Default => new ProgramConfig
        {
            LoginToken = "INSERT_LOGINTOKEN_HERE",
            DefaultPrefix = "~",
            TotalShards = 1,

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
                Severity = LogSeverity.Debug
            },

            Lavalink = new LavalinkObject
            {
                Start = false,
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
                DbUsername = "novaxis",
                DbPassword = "123",
                DbName = "novaxis"
            },

            Audio = new AudioObject
            {
                Timeout = 30000,
                Cache = new CacheObject
                {
                    AbsoluteExpiration = TimeSpan.FromHours(12),
                    RelativeExpiration = TimeSpan.FromHours(6)
                }
            }
        };

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
            public bool Start { get; set; }
            public string Host { get; set; }
            public ushort Port { get; set; }
            public string Login { get; set; }
            public bool SelfDeaf { get; set; }
        }

        public struct LogObject
        {
            public bool Active { get; set; }
            public LogSeverity Severity { get; set; }
        }

        public struct AudioObject
        {
            public long Timeout { get; set; }
            public CacheObject Cache { get; set; }
        }

        public struct CacheObject
        {
            public TimeSpan? AbsoluteExpiration { get; set; }
            public TimeSpan? RelativeExpiration { get; set; }
        }

        public string LoginToken { get; set; }
        public string DefaultPrefix { get; set; }
        public int TotalShards { get; set; }
        public ActivityObject Activity { get; set; }
        public LogObject Log { get; set; }
        public LavalinkObject Lavalink { get; set; }
        public DatabaseObject Database { get; set; }
        public AudioObject Audio { get; set; }

        public static event Func<LogMessage, Task> LogEvent;

        public static async Task<ProgramConfig> LoadConfig()
        {
            try
            {
                return JsonConvert.DeserializeObject<ProgramConfig>(await File.ReadAllTextAsync(ConfigPath));
            }

            catch (FileNotFoundException)
            {
                await LogEvent(new LogMessage(LogSeverity.Error, "Program", $"Config file ({ConfigPath}) not found"));
                await LogEvent(new LogMessage(LogSeverity.Warning, "Program", "Forcing config reset"));
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
