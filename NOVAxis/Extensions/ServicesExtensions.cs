using System;
using System.IO;
using Anthropic.SDK;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NOVAxis.Core;
using NOVAxis.Modules;
using NOVAxis.Utilities;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.Lavalink;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Services.Polls;
using NOVAxis.Services.Discord;
using NOVAxis.Services.Download;
using NOVAxis.Services.Net;
using NOVAxis.Web;
using NOVAxis.Web.Auth;
using NOVAxis.Web.Hubs;

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
            collection.Configure<WebOptions>(config.GetSection(WebOptions.Key));
            collection.Configure<DownloadOptions>(config.GetSection(DownloadOptions.Key));

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
                LogGatewayIntentWarnings = false,
                EnableVoiceDaveEncryption = true
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
            collection.TryAddSingleton<GuardedProxy>();
            collection.TryAddSingleton<YtDlpClient>();
            collection.AddSingleton<AudioSearchCache>();
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

        /// <summary>
        /// Hosts the web player - the OAuth login, the REST api, the SignalR hub and the
        /// static frontend. Runs inside the bot's own process, over the same container.
        /// </summary>
        public static IServiceCollection AddWebApp(this IServiceCollection collection, IConfiguration config)
        {
            var options = new WebOptions();
            config.GetSection(WebOptions.Key).Bind(options);

            if (!options.Active)
                return collection;

            var audio = new AudioOptions();
            config.GetSection(AudioOptions.Key).Bind(audio);

            if (!audio.Active)
                throw new InvalidOperationException(
                    "The web player controls the audio playback, so it cannot run with 'Audio:Active' off");

            collection.Configure<ForwardedHeadersOptions>(o =>
            {
                // TLS ends at the reverse proxy, so scheme and caller arrive as headers
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownNetworks.Clear();
                o.KnownProxies.Clear();
            });

            // Cookies must outlive the process, so the key ring goes to disk
            collection.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(options.KeyPath))
                .SetApplicationName("NOVAxis");

            collection.AddDiscordAuthentication(options);
            collection.AddAuthorization();
            collection.AddWebRateLimits();
            collection.AddSignalR();

            collection.AddSingleton<GuildAccessService>();
            collection.AddSingleton<PlayerStateService>();
            collection.AddSingleton<PlayerHubTracker>();
            collection.AddSingleton<PlayerBroadcaster>();
            collection.AddSingleton<WebPlayerService>();
            collection.AddHostedService<PlayerBroadcastService>();

            return collection;
        }

        /// <summary>
        /// Prepares media files on request and hands them out over the web app. Registered
        /// whichever audio backend is in use - downloading has nothing to do with playback -
        /// and inert when 'Download:Active' is off, because the endpoints are mapped
        /// unconditionally and would otherwise resolve to nothing.
        /// </summary>
        public static IServiceCollection AddDownloads(this IServiceCollection collection, IConfiguration config)
        {
            // Both also registered by the yt-dlp audio backend, which only runs when chosen
            collection.TryAddSingleton<GuardedProxy>();
            collection.TryAddSingleton<YtDlpClient>();

            // Registered here alone, so the guard is torn down once at shutdown
            collection.AddHostedService(p => p.GetRequiredService<GuardedProxy>());

            collection.AddSingleton<DownloadStore>();
            collection.AddSingleton<YtDlpDownloader>();
            collection.AddSingleton<DownloadService>();
            collection.AddHostedService<DownloadSweeper>();

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
