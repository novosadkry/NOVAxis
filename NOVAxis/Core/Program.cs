using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Modules;
using NOVAxis.Extensions;

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

            var config = await SetupConfig();
            var services = await SetupServices(config);

            var client = services.GetRequiredService<DiscordShardedClient>();
            var audio = services.GetRequiredService<IAudioService>();
            var logger = services.GetRequiredService<ILogger<Program>>();
            var options = services.GetRequiredService<IOptions<ProgramOptions>>();

            logger.LogInformation("NOVAxis v" + Version);

            try
            {
                await audio.StartAsync();

                await client.LoginAsync(TokenType.Bot, options.Value.LoginToken);
                await client.StartAsync();

                await client.SetGameAsync(options.Value.Activity.Online, type: options.Value.Activity.ActivityType);
                await client.SetStatusAsync(options.Value.Activity.UserStatus);
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

        private static async Task<IConfiguration> SetupConfig()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("config.json", true)
                .AddEnvironmentVariables()
                .Build();

            return await Task.FromResult<IConfiguration>(config);
        }

        private static async Task<IServiceProvider> SetupServices(IConfiguration config)
        {
            var services = new ServiceCollection()
                .AddSingleton<ProgramLogger>()
                .AddConfiguration(config)
                .AddDiscord(config)
                .AddAudio()
                .AddInteractions(config)
                .AddMemoryCache()
                .AddLogging(builder => builder.AddProgramLogger())
                .BuildServiceProvider(true);

            var client = services.GetRequiredService<DiscordShardedClient>();
            var logger = services.GetService<ILogger<DiscordShardedClient>>();

            client.Log += logger.Log;
            client.ShardReady += shard => Client_Ready(shard, services);

            return await Task.FromResult(services);
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
