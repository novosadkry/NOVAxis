using System;
using System.IO;
using System.Threading.Tasks;

using Discord;

using Newtonsoft.Json;

namespace NOVAxis
{
    public class ProgramConfig
    {
        public class ActivityObject
        {
            public string Online { get; set; }
            public string Afk { get; set; }
            public string Offline { get; set; }
            public ActivityType ActivityType { get; set; }
            public UserStatus UserStatus { get; set; }
        }

        public class DatabaseObject
        {
            public bool Active { get; set; }
            public string DbType { get; set; }
            public string DbHost { get; set; }
            public ushort DbPort { get; set; }
            public string DbUsername { get; set; }
            public string DbPassword { get; set; }
            public string DbName { get; set; }
        }

        public class LavalinkObject
        {
            public bool Start { get; set; }
            public string Host { get; set; }
            public string Login { get; set; }
        }

        public class LogObject
        {
            public bool Active { get; set; }
            public LogSeverity Severity { get; set; }
        }

        private const string ConfigPath = @"config.json";

        public string LoginToken { get; set; }
        public string DefaultPrefix { get; set; }
        public int TotalShards { get; set; }
        public ActivityObject Activity { get; set; }
        public LogObject Log { get; set; }
        public LavalinkObject Lavalink { get; set; }
        public DatabaseObject Database { get; set; }
        public long AudioTimeout { get; set; }

        public ProgramConfig()
        {
            LoginToken = "INSERT_LOGINTOKEN_HERE";
            DefaultPrefix = "~";
            TotalShards = 1;

            Activity = new ActivityObject
            {
                Online = "pohyb atomů",
                Afk = "ochlazování jádra",
                Offline = "repair/reboot jádra",
                ActivityType = ActivityType.Listening,
                UserStatus = UserStatus.Online,
            };

            Log = new LogObject
            {
                Active = true,
                Severity = LogSeverity.Debug,
            };

            Lavalink = new LavalinkObject
            {
                Start = false,
                Host = "localhost",
                Login = "123",
            };

            Database = new DatabaseObject
            {
                Active = true,
                DbType = "mysql",
                DbHost = "localhost",
                DbUsername = "novaxis",
                DbPassword = "123",
                DbName = "novaxis",
            };

            AudioTimeout = 30000;
        }

        public static event Func<LogMessage, Task> LogEvent;

        public static async Task<ProgramConfig> LoadConfig()
        {
            try
            {
                return JsonConvert.DeserializeObject<ProgramConfig>(await File.ReadAllTextAsync(ConfigPath));
            }

            catch (FileNotFoundException)
            {
                await LogEvent(new LogMessage(LogSeverity.Warning, "Program", $"Config file ({ConfigPath}) not found"));
                await LogEvent(new LogMessage(LogSeverity.Info, "Program", "Forcing config reset"));
                await ResetConfig();

                return await LoadConfig();
            }
        }

        public static async Task SaveConfig(ProgramConfig config)
        {
            await Task.Run(() => File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(config, Formatting.Indented)));
        }

        public static async Task ResetConfig()
        {
            await SaveConfig(new ProgramConfig());
        }
    }
}
