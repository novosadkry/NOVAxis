using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Core;
using NOVAxis.Modules;
using NOVAxis.Utilities;
using NOVAxis.Services.Vote;
using NOVAxis.Services.Discord;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.InactivityTracking;
using Lavalink4NET.InactivityTracking.Extensions;
using Lavalink4NET.InactivityTracking.Trackers.Idle;
using Lavalink4NET.InactivityTracking.Trackers.Users;

namespace NOVAxis.Extensions
{
    public static class ServicesExtensions
    {
        public static IServiceCollection AddConfiguration(this IServiceCollection collection, IConfiguration config)
        {
            collection.AddOptions();
            collection.AddSingleton(config);
            collection.Configure<DiscordOptions>(config.GetSection(DiscordOptions.Key));
            collection.Configure<AudioOptions>(config.GetSection(AudioOptions.Key));
            collection.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.Key));
            collection.Configure<CacheOptions>(config.GetSection(CacheOptions.Key));

            return collection;
        }

        public static IServiceCollection AddInteractions(this IServiceCollection collection, IConfiguration config)
        {
            var interactionConfig = new InteractionServiceConfig
            {
                UseCompiledLambda = true,
                DefaultRunMode = RunMode.Async,
                LogLevel = LogSeverity.Debug
            };

            collection.AddSingleton<ModuleHandler>();
            collection.AddSingleton(interactionConfig);
            collection.AddSingleton<InteractionService>();
            collection.AddSingleton<InteractionCache>();
            collection.AddSingleton<CooldownCache>();

            return collection;
        }

        public static IServiceCollection AddDiscord(this IServiceCollection collection, IConfiguration config)
        {
            var options = new DiscordOptions();
            config.GetSection(DiscordOptions.Key).Bind(options);

            var clientConfig = new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Debug,
                TotalShards = options.TotalShards,
                MessageCacheSize = 100,
                UseInteractionSnowflakeDate = false,
                GatewayIntents = GatewayIntents.All,
                LogGatewayIntentWarnings = false
            };

            collection.AddSingleton(clientConfig);
            collection.AddSingleton<IDiscordClient, DiscordShardedClient>();
            collection.AddSingleton(p => (DiscordShardedClient) p.GetService<IDiscordClient>());
            collection.AddHostedService<DiscordHostService>();

            return collection;
        }

        public static IServiceCollection AddAudio(this IServiceCollection collection, IConfiguration config)
        {
            var options = new AudioOptions();
            config.GetSection(AudioOptions.Key).Bind(options);

            if (!options.Active)
                return collection;

            collection
                .AddOptions<AudioServiceOptions>()
                .Configure<IOptions<AudioOptions>>((s, l) =>
                {
                    var options = l.Value.Lavalink;
                    s.BaseAddress = new Uri($"http://{options.Host}:{options.Port}");
                    s.Passphrase = options.Login;
                });

            collection
                .AddOptions<IdleInactivityTrackerOptions>()
                .Configure<IOptions<AudioOptions>>((i, a) =>
                {
                    var options = a.Value.Timeout;
                    i.Timeout = options.IdleInactivity;
                });

            collection
                .AddOptions<UsersInactivityTrackerOptions>()
                .Configure<IOptions<AudioOptions>>((i, a) =>
                {
                    var options = a.Value.Timeout;
                    i.Timeout = options.UsersInactivity;
                });

            collection
                .ConfigureInactivityTracking(options =>
                {
                    options.DefaultTimeout = TimeSpan.Zero;
                    options.InactivityBehavior = PlayerInactivityBehavior.None;
                });

            collection.AddLavalink();
            collection.AddInactivityTracking();
            collection.AddInactivityTracker<IdleInactivityTracker>();
            collection.AddInactivityTracker<UsersInactivityTracker>();

            return collection;
        }

        public static IServiceCollection AddVote(this IServiceCollection collection, IConfiguration config)
        {
            collection.AddHostedService<VoteHostService>();

            return collection;
        }
    }
}
