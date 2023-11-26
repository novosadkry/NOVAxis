using System;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Core;
using NOVAxis.Modules;
using NOVAxis.Utilities;

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Interactions;

using Lavalink4NET.Extensions;
using Lavalink4NET.InactivityTracking.Extensions;

using CommandRunMode = Discord.Commands.RunMode;
using InteractionRunMode = Discord.Interactions.RunMode;

namespace NOVAxis.Extensions
{
    public static class ServicesExtensions
    {
        public static IServiceCollection AddAudio(this IServiceCollection collection, ProgramConfig config)
        {
            collection.ConfigureLavalink(options =>
            {
                options.BaseAddress = new Uri($"http://{config.Lavalink.Host}:{config.Lavalink.Port}");
                options.Passphrase = config.Lavalink.Login;
            });

            collection.AddLavalink();
            collection.AddInactivityTracking();

            return collection;
        }

        public static IServiceCollection AddInteractions(this IServiceCollection collection, ProgramConfig config)
        {
            var interactionConfig = new InteractionServiceConfig
            {
                UseCompiledLambda = true,
                DefaultRunMode = InteractionRunMode.Async,
                LogLevel = config.Log.Level.ToSeverity()
            };

            var interactionCacheOptions = new CacheOptions
            {
                AbsoluteExpiration = config.Interaction.Cache.AbsoluteExpiration,
                RelativeExpiration = config.Interaction.Cache.RelativeExpiration
            };

            collection.AddSingleton(interactionConfig);
            collection.AddSingleton<InteractionService>();
            collection.AddInteractionCache(interactionCacheOptions);

            return collection;
        }

        public static IServiceCollection AddDiscord(this IServiceCollection collection, ProgramConfig config)
        {
            var clientConfig = new DiscordSocketConfig
            {
                LogLevel = config.Log.Level.ToSeverity(),
                TotalShards = config.TotalShards,
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

        public static IServiceCollection AddCommands(this IServiceCollection collection, ProgramConfig config)
        {
            var commandServiceConfig = new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = CommandRunMode.Async,
                LogLevel = config.Log.Level.ToSeverity()
            };

            collection.AddSingleton(commandServiceConfig);
            collection.AddSingleton<ModuleHandler>();
            collection.AddSingleton<CommandService>();

            return collection;
        }

        public static IServiceCollection AddCache<TKey, TValue>(this IServiceCollection collection, CacheOptions options)
        {
            return collection.AddSingleton(new Cache<TKey, TValue>(options));
        }

        public static IServiceCollection AddInteractionCache(this IServiceCollection collection, CacheOptions options)
        {
            return collection.AddSingleton(new InteractionCache(options));
        }
    }
}
