using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Modules;
using NOVAxis.Extensions;
using NOVAxis.Database.Guild;

using Discord;
using Discord.WebSocket;

using Lavalink4NET;

namespace NOVAxis.Core
{
    public class Program
    {
        public static bool IsRunning { get; private set; }
        public static short ShardsReady { get; private set; }
        public static ulong OwnerId => 269182357704015873L;

        public static string Version
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString()[..5];

        public static async Task Main()
        {
            IsRunning = true;

            var services = await SetupServices();

            var client = services.GetRequiredService<DiscordShardedClient>();
            var config = services.GetRequiredService<ProgramConfig>();
            var logger = services.GetRequiredService<ILogger<Program>>();
            var audio = services.GetRequiredService<IAudioService>();

            logger.LogInformation("NOVAxis v" + Version);

            try
            {
                await audio.StartAsync();

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

            var services = new ServiceCollection()
                .AddSingleton(config)
                .AddSingleton<ProgramLogger>()
                .AddDbContext<GuildDbContext>()
                .AddDiscord(config)
                .AddAudio(config)
                .AddCommands(config)
                .AddInteractions(config)
                .AddMemoryCache()
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
            var audio = services.GetService<IAudioService>();

            await audio.DisposeAsync();
            await client.LogoutAsync();

            IsRunning = false;
        }

        private static async Task Client_Ready(DiscordSocketClient shard, IServiceProvider services)
        {
            var client = services.GetRequiredService<DiscordShardedClient>();
            var modules = services.GetRequiredService<ModuleHandler>();

            // Execute after all shards are ready
            if (++ShardsReady == client.Shards.Count)
                await modules.Setup();
        }
    }
}
