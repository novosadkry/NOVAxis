﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        private const string configPath = @"config.json";

        public string LoginToken { get; set; }

        public ActivityObject Activity { get; set; }

        public LogObject Log { get; set; }

        public LavalinkObject Lavalink { get; set; }

        public DatabaseObject Database { get; set; }

        public long AudioTimeout { get; set; }

        public ProgramConfig()
        {
            LoginToken = "INSERT_LOGINTOKEN_HERE";

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
                DbHost = "localhost",
                DbUsername = "novaxis",
                DbPassword = "123",
                DbName = "novaxis",
            };

            AudioTimeout = 30000;
        }

        public async static Task<ProgramConfig> LoadConfig(Func<LogMessage, Task> log)
        {
            try
            {
                return JsonConvert.DeserializeObject<ProgramConfig>(File.ReadAllText(configPath));
            }

            catch (FileNotFoundException)
            {
                await log(new LogMessage(LogSeverity.Warning, "Program", $"Config file ({configPath}) not found"));
                await log(new LogMessage(LogSeverity.Info, "Program", "Forcing config reset"));
                await ResetConfig();

                return await LoadConfig(log);
            }
        }

        public async static Task SaveConfig(ProgramConfig config)
        {
            await Task.Run(() => File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented)));
        }

        public async static Task ResetConfig()
        {
            await SaveConfig(new ProgramConfig());
        }
    }
}
