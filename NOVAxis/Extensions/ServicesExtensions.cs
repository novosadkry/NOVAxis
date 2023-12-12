using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Core;
using NOVAxis.Modules;
using NOVAxis.Utilities;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.InactivityTracking.Extensions;

namespace NOVAxis.Extensions
{
    public static class ServicesExtensions
    {
        public static IServiceCollection AddAudio(this IServiceCollection collection)
        {
            collection
                .AddOptions<AudioServiceOptions>()
                .Configure<IOptions<LavalinkOptions>>((s, l) =>
                {
                    s.BaseAddress = new Uri($"http://{l.Value.Host}:{l.Value.Port}");
                    s.Passphrase = l.Value.Login;
                });

            collection.AddLavalink();
            collection.AddInactivityTracking();

            return collection;
        }

        public static IServiceCollection AddConfiguration(this IServiceCollection collection, IConfiguration config)
        {
            collection.AddOptions();
            collection.AddSingleton(config);
            collection.Configure<ProgramOptions>(config);
            collection.Configure<LogOptions>(config.GetSection(LogOptions.Log));
            collection.Configure<AudioOptions>(config.GetSection(AudioOptions.Audio));
            collection.Configure<CacheOptions>(config.GetSection(CacheOptions.Cache));
            collection.Configure<ActivityOptions>(config.GetSection(ActivityOptions.Activity));
            collection.Configure<DatabaseOptions>(config.GetSection(DatabaseOptions.Database));
            collection.Configure<LavalinkOptions>(config.GetSection(LavalinkOptions.Lavalink));
            collection.Configure<InteractionOptions>(config.GetSection(InteractionOptions.Interaction));

            return collection;
        }

        public static IServiceCollection AddInteractions(this IServiceCollection collection, IConfiguration config)
        {
            var options = new ProgramOptions();
            config.Bind(options);

            var interactionConfig = new InteractionServiceConfig
            {
                UseCompiledLambda = true,
                DefaultRunMode = RunMode.Async,
                LogLevel = options.Log.Level.ToSeverity()
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
            var options = new ProgramOptions();
            config.Bind(options);

            var clientConfig = new DiscordSocketConfig
            {
                LogLevel = options.Log.Level.ToSeverity(),
                TotalShards = options.TotalShards,
                MessageCacheSize = 100,
                UseInteractionSnowflakeDate = false,
                GatewayIntents = GatewayIntents.All,
                LogGatewayIntentWarnings = false
            };

            collection.AddSingleton(clientConfig);
            collection.AddSingleton<IDiscordClient, DiscordShardedClient>();
            collection.AddSingleton(p => (DiscordShardedClient) p.GetService<IDiscordClient>());

            return collection;
        }
    }
}
