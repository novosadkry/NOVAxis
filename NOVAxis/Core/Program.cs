﻿using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Modules;
using NOVAxis.Extensions;
using NOVAxis.Database.Guild;
using NOVAxis.Services.Audio;

using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;

using Interactivity;
using Victoria.Node;

using CommandRunMode = Discord.Commands.RunMode;
using InteractionRunMode = Discord.Interactions.RunMode;

namespace NOVAxis.Core
{
    public class Program
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
            var logger = services.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("NOVAxis v" + Version);

            try
            {
                await client.LoginAsync(TokenType.Bot, config.LoginToken);
                await client.StartAsync();

                await client.SetGameAsync(config.Activity.Online, type: config.Activity.ActivityType);
                await client.SetStatusAsync(config.Activity.UserStatus);
            }

            catch (Exception e)
            {
                logger.LogError("The flow of execution has been halted due to an exception" + 
                                $"\nReason: {e.Message}");
                Console.Read();
                return;
            }

            await ProgramCommand.AwaitCommands(services);
        }

        public static async Task<IServiceProvider> SetupServices()
        {
            var config = await ProgramConfig.LoadConfig();

            var clientConfig = new DiscordSocketConfig
            {
                LogLevel = config.Log.Level.ToSeverity(),
                TotalShards = config.TotalShards,
                MessageCacheSize = 100,
                UseInteractionSnowflakeDate = false,
                GatewayIntents = GatewayIntents.All,
                LogGatewayIntentWarnings = false
            };

            var commandServiceConfig = new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = CommandRunMode.Async,
                LogLevel = config.Log.Level.ToSeverity()
            };

            var interactionConfig = new InteractionServiceConfig
            {
                DefaultRunMode = InteractionRunMode.Async,
                LogLevel = config.Log.Level.ToSeverity()
            };

            var audioNodeConfig = new NodeConfiguration
            {
                Hostname = config.Lavalink.Host,
                Port = config.Lavalink.Port,
                Authorization = config.Lavalink.Login,
                SelfDeaf = config.Lavalink.SelfDeaf
            };

            var services = new ServiceCollection()
                .AddSingleton(config)
                .AddSingleton(clientConfig)
                .AddSingleton(commandServiceConfig)
                .AddSingleton(interactionConfig)
                .AddSingleton(audioNodeConfig)
                .AddSingleton<DiscordShardedClient>()
                .AddSingleton<ProgramLogger>()
                .AddSingleton<ModuleHandler>()
                .AddSingleton<CommandService>()
                .AddSingleton<InteractionService>()
                .AddSingleton<InteractivityService>()
                .AddSingleton<AudioNode>()
                .AddSingleton<AudioService>()
                .AddDbContext<GuildDbContext>()
                .AddLogging(builder => builder.AddProgramLogger())
                .BuildServiceProvider(true);

            var client = services.GetRequiredService<DiscordShardedClient>();
            var logger = services.GetService<ILogger<DiscordShardedClient>>();

            client.Log += logger.Log;
            client.ShardReady += shard => Client_Ready(shard, services);

            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetService<GuildDbContext>()
                .Database.EnsureCreatedAsync();

            return services;
        }

        public static async Task Exit(IServiceProvider services)
        {
            var client = services.GetRequiredService<DiscordShardedClient>();
            var audioNode = services.GetService<AudioNode>();

            if (audioNode is {IsConnected: true})
                await audioNode.DisposeAsync();

            await client.LogoutAsync();
            await client.StopAsync();
        }

        private static async Task Client_Ready(DiscordSocketClient shard, IServiceProvider services)
        {
            var client = services.GetRequiredService<DiscordShardedClient>();
            var modules = services.GetRequiredService<ModuleHandler>();
            var logger = services.GetRequiredService<ILogger<Program>>();
            var audioNode = services.GetService<AudioNode>();

            // Execute after all shards are ready
            if (++ShardsReady == client.Shards.Count)
            {
                await logger.Log(new LogMessage(LogSeverity.Info, "Victoria", "Connecting"));
                _ = Task.Run(() => audioNode?.ConnectAsync()); // this way it doesn't block the main thread

                await modules.Setup();
            }
        }
    }
}
