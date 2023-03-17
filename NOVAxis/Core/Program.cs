using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Modules;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Guild;

using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;

using Interactivity;
using Victoria;

using CommandRunMode = Discord.Commands.RunMode;
using InteractionRunMode = Discord.Interactions.RunMode;

namespace NOVAxis.Core
{
    public static class Program
    {
        public static short ShardsReady { get; private set; }
        public static ulong OwnerId => 269182357704015873L;

        public static string Version
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString()[..5];

        public static async Task Main()
        {
            var services = await SetupServices();

            var client = services.GetRequiredService<DiscordShardedClient>();
            var config = services.GetRequiredService<ProgramConfig>();
            var logger = services.GetRequiredService<ProgramLogger>();

            await logger.Log(new LogMessage(LogSeverity.Info, "Program", "NOVAxis v" + Version));

            try
            {
                await client.LoginAsync(TokenType.Bot, config.LoginToken);
                await client.StartAsync();

                await client.SetGameAsync(config.Activity.Online, type: config.Activity.ActivityType);
                await client.SetStatusAsync(config.Activity.UserStatus);
            }

            catch (Exception e)
            {
                await logger.Log(new LogMessage(LogSeverity.Error, "Program",
                    "The flow of execution has been halted due to an exception" +
                    $"\nReason: {e.Message}"));
                Console.Read();
                return;
            }

            if (config.Lavalink.Start)
            {
                await ProgramCommand.CommandList
                    .First(x => x.Name == "lavalink")
                    .Execute(services);
            }

            await ProgramCommand.AwaitCommands(services);
        }

        public static async Task<IServiceProvider> SetupServices()
        {
            var config = await ProgramConfig.LoadConfig();

            var clientConfig = new DiscordSocketConfig
            {
                LogLevel = config.Log.Severity,
                TotalShards = config.TotalShards,
                MessageCacheSize = 100,
                UseInteractionSnowflakeDate = false,
                GatewayIntents = GatewayIntents.All
            };

            var commandServiceConfig = new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = CommandRunMode.Async,
                LogLevel = config.Log.Severity
            };

            var interactionConfig = new InteractionServiceConfig
            {
                DefaultRunMode = InteractionRunMode.Async,
                LogLevel = config.Log.Severity
            };

            var lavaConfig = new LavaConfig
            {
                Hostname = config.Lavalink.Host,
                Port = config.Lavalink.Port,
                Authorization = config.Lavalink.Login,
                SelfDeaf = config.Lavalink.SelfDeaf,
                LogSeverity = config.Log.Severity
            };

            var services = new ServiceCollection()
                .AddSingleton(config)
                .AddSingleton(clientConfig)
                .AddSingleton(commandServiceConfig)
                .AddSingleton(interactionConfig)
                .AddSingleton(lavaConfig)
                .AddSingleton<DiscordShardedClient>()
                .AddSingleton<ProgramLogger>()
                .AddSingleton<ModuleHandler>()
                .AddSingleton<CommandService>()
                .AddSingleton<InteractionService>()
                .AddSingleton<InteractivityService>()
                .AddSingleton<LavaNode>()
                .AddSingleton<AudioModuleService>()
                .AddSingleton<GuildService>()
                .AddDbContext<GuildDbContext>()
                .BuildServiceProvider();

            var client = services.GetRequiredService<DiscordShardedClient>();
            var logger = services.GetRequiredService<ProgramLogger>();
            var lavaNode = services.GetService<LavaNode>();

            client.Log += logger.Log;
            client.ShardReady += shard => Client_Ready(shard, services);

            lavaNode.OnLog += logger.Log;

            return services;
        }

        public static async Task Exit(IServiceProvider services)
        {
            var client = services.GetRequiredService<DiscordShardedClient>();
            var lavaNode = services.GetService<LavaNode>();

            if (lavaNode is {IsConnected: true})
                await lavaNode.DisposeAsync();

            await client.LogoutAsync();
            await client.StopAsync();
        }

        private static async Task Client_Ready(DiscordSocketClient shard, IServiceProvider services)
        {
            var config = services.GetRequiredService<ProgramConfig>();
            var modules = services.GetRequiredService<ModuleHandler>();
            var logger = services.GetRequiredService<ProgramLogger>();
            var lavaNode = services.GetService<LavaNode>();

            await logger.Log(new LogMessage(LogSeverity.Info, "Shard #" + shard.ShardId, "Ready"));

            if (++ShardsReady == config.TotalShards)
            {
                await logger.Log(new LogMessage(LogSeverity.Info, "Victoria", "Connecting"));
                await Task.Run(() => lavaNode?.ConnectAsync());

                await modules.Setup();
            }
        }
    }
}
