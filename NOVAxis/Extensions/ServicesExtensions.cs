using System;
using Anthropic.SDK;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Core;
using NOVAxis.Modules;
using NOVAxis.Utilities;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.Lavalink;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Services.Polls;
using NOVAxis.Services.Discord;

using Discord;
using Discord.Rest;
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
            collection.AddSingleton<DiscordShardedClient>();
            collection.AddSingleton(p => p.GetService<DiscordShardedClient>() as IDiscordClient);
            collection.AddSingleton(p => p.GetService<DiscordShardedClient>().Rest as DiscordRestClient);
            collection.AddHostedService<DiscordHostService>();

            return collection;
        }

        /// <summary>
        /// Wires up the audio backend selected by configuration. Both backends expose the same
        /// abstractions, so nothing above this layer needs to know which one is running.
        /// </summary>
        public static IServiceCollection AddAudio(this IServiceCollection collection, IConfiguration config)
        {
            var options = new AudioOptions();
            config.GetSection(AudioOptions.Key).Bind(options);

            if (!options.Active)
                return collection;

            collection.AddSingleton<AudioNotifier>();

            return options.Backend == AudioBackend.Lavalink
                ? collection.AddLavalinkAudio()
                : collection.AddYtDlpAudio();
        }

        /// <summary>
        /// Streams audio in-process: yt-dlp resolves the media, ffmpeg decodes it and the
        /// bot itself pushes the samples into Discord.
        /// </summary>
        private static IServiceCollection AddYtDlpAudio(this IServiceCollection collection)
        {
            collection.AddSingleton<YtDlpClient>();
            collection.AddSingleton<IAudioSearchService, YtDlpAudioSearchService>();
            collection.AddSingleton<YtDlpAudioPlayerManager>();
            collection.AddSingleton<IAudioPlayerManager>(p => p.GetRequiredService<YtDlpAudioPlayerManager>());
            collection.AddHostedService<AudioInactivityTracker>();

            return collection;
        }

        /// <summary>
        /// Delegates playback to a Lavalink node.
        /// </summary>
        private static IServiceCollection AddLavalinkAudio(this IServiceCollection collection)
        {
            collection
                .AddOptions<AudioServiceOptions>()
                .Configure<IOptions<AudioOptions>>((s, l) =>
                {
                    var lavalink = l.Value.Lavalink;
                    s.BaseAddress = new Uri($"http://{lavalink.Host}:{lavalink.Port}");
                    s.Passphrase = lavalink.Login;
                });

            collection
                .AddOptions<IdleInactivityTrackerOptions>()
                .Configure<IOptions<AudioOptions>>((i, a) =>
                {
                    var timeout = a.Value.Timeout;
                    i.Timeout = timeout.IdleInactivity;
                });

            collection
                .AddOptions<UsersInactivityTrackerOptions>()
                .Configure<IOptions<AudioOptions>>((i, a) =>
                {
                    var timeout = a.Value.Timeout;
                    i.Timeout = timeout.UsersInactivity;
                });

            collection
                .ConfigureInactivityTracking(inactivityOptions =>
                {
                    inactivityOptions.DefaultTimeout = TimeSpan.Zero;
                    inactivityOptions.InactivityBehavior = PlayerInactivityBehavior.None;
                });

            collection.AddLavalink();
            collection.AddInactivityTracking();
            collection.AddInactivityTracker<IdleInactivityTracker>();
            collection.AddInactivityTracker<UsersInactivityTracker>();

            collection.AddSingleton<IAudioSearchService, LavalinkAudioSearchService>();
            collection.AddSingleton<IAudioPlayerManager, LavalinkAudioPlayerManager>();

            return collection;
        }

        public static IServiceCollection AddAnthropic(this IServiceCollection collection, IConfiguration config)
        {
            var options = new AnthropicOptions();
            config.GetSection(AnthropicOptions.Key).Bind(options);

            var auth = new APIAuthentication(options.ApiKey);
            collection.AddScoped(_ => new AnthropicClient(auth));

            return collection;
        }

        public static IServiceCollection AddPolls(this IServiceCollection collection, IConfiguration config)
        {
            collection.AddSingleton<PollService>();
            collection.AddHostedService<PollHostService>();

            return collection;
        }
    }
}
