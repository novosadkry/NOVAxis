using System;
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
        }

        private const string configPath = @"config.json";

        public string LoginToken { get; set; }

        public ActivityObject Activity { get; set; }
        public ActivityType ActivityType { get; set; }
        public UserStatus UserStatus { get; set; }

        public bool Log { get; set; }
        public LogSeverity LogSeverity { get; set; }

        public bool StartLavalink { get; set; }
        public string LavalinkHost { get; set; }
        public string LavalinkLogin { get; set; }

        public long AudioTimeout { get; set; }

        public ProgramConfig()
        {
            LoginToken = "INSERT_LOGINTOKEN_HERE";
            Activity = new ActivityObject
            {
                Online = "pohyb atomů",
                Afk = "ochlazování jádra",
                Offline = "repair/reboot jádra"
            };
            ActivityType = ActivityType.Listening;
            UserStatus = UserStatus.Online;
            Log = true;
            LogSeverity = LogSeverity.Debug;
            StartLavalink = false;
            LavalinkHost = "localhost";
            LavalinkLogin = "123";
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
