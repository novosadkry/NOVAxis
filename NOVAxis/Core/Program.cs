using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Modules;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Guild;
using NOVAxis.Services.Database;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

using Interactivity;
using Victoria;

namespace NOVAxis.Core
{
    public class Program
    {
        public static DiscordShardedClient Client { get; private set; }
        public static ProgramConfig Config { get; private set; }

        public static ModuleHandler Modules { get; private set; }
        public static IServiceProvider Services { get; private set; }

        public static short ShardsReady { get; private set; }
        public static ulong OwnerId => 269182357704015873L;

        public static string Version 
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString().Substring(0, 5);

        public static async Task Main(string[] args)
        {
            ProgramConfig.LogEvent += Client_Log;
            Config = await ProgramConfig.LoadConfig();

            await Client_Log(new LogMessage(LogSeverity.Info, "Program", "NOVAxis v" + Version));

            Client = new DiscordShardedClient(new DiscordSocketConfig
            {
                LogLevel = Config.Log.Severity,
                TotalShards = Config.TotalShards,
                ExclusiveBulkDelete = true,
                MessageCacheSize = 100
            });

            var commandService = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async,
                LogLevel = Config.Log.Severity
            });

            var lavaConfig = new LavaConfig
            {
                Hostname = Config.Lavalink.Host,
                Port = Config.Lavalink.Port,
                Authorization = Config.Lavalink.Login,
                SelfDeaf = Config.Lavalink.SelfDeaf,
                LogSeverity = Config.Log.Severity
            };

            var lavaNode = new LavaNode(Client, lavaConfig);
            lavaNode.OnLog += Client_Log;

            var databaseService = DatabaseService.GetService(Config.Database);
            databaseService.LogEvent += Client_Log;
            if (Config.Database.Active) await databaseService.Setup();

            var guildService = new GuildService(databaseService);
            if (Config.Database.Active) await guildService.LoadFromDatabase();

            Services = new ServiceCollection()
                .AddSingleton(lavaNode)
                .AddSingleton(databaseService)
                .AddSingleton(guildService)
                .AddSingleton(new InteractivityService(Client))
                .AddSingleton(new AudioModuleService(lavaNode))
                .BuildServiceProvider();

            Modules = new ModuleHandler(Client, Services, commandService);
            Modules.LogEvent += Client_Log;
            await Modules.Setup();

            Client.ShardReady += Client_Ready;
            Client.Log += Client_Log;

            if (Config.Lavalink.Start)
            {
                await ProgramCommand.ProgramCommandList
                    .First(x => x.Name == "lavalink")
                    .Execute();
            }

            try
            {
                await Client.LoginAsync(TokenType.Bot, Config.LoginToken);
                await Client.StartAsync();

                await Client.SetGameAsync(Config.Activity.Online, type: Config.Activity.ActivityType);
                await Client.SetStatusAsync(Config.Activity.UserStatus);
            }

            catch (Exception e)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "Program",
                    "The flow of execution has been halted due to an exception" +
                    $"\nReason: {e.Message}"));
                Console.Read();
                return;
            }

            await ProgramCommand.AwaitCommands(Client, Client_Log);
        }

        public static async Task Exit()
        {
            var lavaNodeInstance = Services.GetService<LavaNode>();

            if (lavaNodeInstance.IsConnected)
                await lavaNodeInstance.DisposeAsync();

            await Client.LogoutAsync();
            await Client.StopAsync();
        }

        public static async Task Client_Log(LogMessage arg)
        {
            ProgramConfig config =
                Config ?? ProgramConfig.Default;

            if (arg.Severity > config.Log.Severity)
                return;

            await ProgramLog.ToConsole(arg);

            if (config.Log.Active)
                await ProgramLog.ToFile(arg);
        }

        private static async Task Client_Ready(DiscordSocketClient shard)
        {
            await Client_Log(new LogMessage(LogSeverity.Info, "Shard #" + shard.ShardId, "Ready"));
            var lavaNodeInstance = Services.GetService<LavaNode>();

            if (++ShardsReady == Config.TotalShards)
            {
                await Client_Log(new LogMessage(LogSeverity.Info, "Victoria", "Connecting"));
                _ = lavaNodeInstance.ConnectAsync();
            }
        }
    }
}
