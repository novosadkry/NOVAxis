using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Modules;
using NOVAxis.Extensions;

using Discord;
using Discord.WebSocket;

namespace NOVAxis.Services.Discord
{
    public class DiscordHostService : IHostedService
    {
        private int _shardsReady;

        private DiscordShardedClient Client { get; set; }
        private ModuleHandler ModuleHandler { get; set; }
        private IOptions<DiscordOptions> Options { get; set; }
        private ILogger<DiscordHostService> Logger { get; set; }

        public DiscordHostService(
            DiscordShardedClient client,
            ModuleHandler moduleHandler,
            IOptions<DiscordOptions> options,
            ILogger<DiscordHostService> logger)
        {
            Logger = logger;
            Client = client;
            Options = options;
            ModuleHandler = moduleHandler;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Client.Log += Logger.Log;
            Client.ShardReady += Client_Ready;

            Logger.Debug("Discord starting");

            try
            {
                var options = Options.Value;

                await Client.LoginAsync(TokenType.Bot, options.LoginToken);
                await Client.StartAsync();

                await Client.SetGameAsync(options.Activity.Online, type: options.Activity.ActivityType);
                await Client.SetStatusAsync(options.Activity.UserStatus);
            }

            catch (Exception e)
            {
                Logger.Error("The flow of execution has been halted due to an exception", e);
                throw;
            }

            Logger.Debug("Discord started");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Client.LogoutAsync();
            await Client.StopAsync();
        }

        private async Task Client_Ready(DiscordSocketClient shard)
        {
            // Execute after all shards are ready
            if (++_shardsReady == Client.Shards.Count)
                await ModuleHandler.Setup();
        }
    }
}
