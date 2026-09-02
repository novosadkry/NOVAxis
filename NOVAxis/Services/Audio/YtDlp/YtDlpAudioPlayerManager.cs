using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

using Discord;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Keeps one player per guild alive. Creation is serialised per guild so that two
    /// commands racing each other cannot open two voice connections.
    /// </summary>
    public sealed class YtDlpAudioPlayerManager : IAudioPlayerManager, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<ulong, YtDlpAudioPlayer> _players = new();
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new();

        private YtDlpClient Client { get; }
        private AudioNotifier Notifier { get; }
        private IOptions<AudioOptions> Options { get; }
        private ILoggerFactory LoggerFactory { get; }
        private ILogger<YtDlpAudioPlayerManager> Logger { get; }

        public YtDlpAudioPlayerManager(
            YtDlpClient client,
            AudioNotifier notifier,
            IOptions<AudioOptions> options,
            ILoggerFactory loggerFactory,
            ILogger<YtDlpAudioPlayerManager> logger)
        {
            Client = client;
            Notifier = notifier;
            Options = options;
            LoggerFactory = loggerFactory;
            Logger = logger;
        }

        /// <summary>
        /// Everything a command can spend before a track reaches the queue: reading the
        /// page of a host yt-dlp cannot stream from, the lookup itself, and room for the
        /// work around both. A player left idle for less than this would be disconnected
        /// while the command which just took it is still searching.
        /// </summary>
        private TimeSpan LookupWindow =>
            MetadataOnlyLinks.Timeout +
            Options.Value.YtDlp.ResolveTimeout +
            TimeSpan.FromSeconds(5);

        public IReadOnlyCollection<IAudioPlayer> Players => _players.Values.ToList();

        internal IReadOnlyCollection<YtDlpAudioPlayer> ActivePlayers => _players.Values.ToList();

        public bool TryGetPlayer(ulong guildId, out IAudioPlayer player)
        {
            var found = _players.TryGetValue(guildId, out var value);
            player = value;

            return found;
        }

        public ValueTask<AudioPlayerRetrieveResult> RetrieveAsync(
            IInteractionContext context,
            AudioPlayerRetrieveOptions options,
            CancellationToken cancellationToken = default)
        {
            return RetrieveAsync(
                context.User as IGuildUser,
                context.Channel as ITextChannel,
                options,
                cancellationToken);
        }

        public async ValueTask<AudioPlayerRetrieveResult> RetrieveAsync(
            IGuildUser user,
            ITextChannel textChannel,
            AudioPlayerRetrieveOptions options,
            CancellationToken cancellationToken = default)
        {
            var guild = user?.Guild;

            if (guild == null)
                return AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.UnknownError);

            var userChannel = user.VoiceChannel;
            var gate = _gates.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync(cancellationToken);

            try
            {
                if (!_players.TryGetValue(guild.Id, out var player))
                {
                    if (!options.JoinChannel)
                        return AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.BotNotConnected);

                    if (userChannel == null)
                        return AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.UserNotInVoiceChannel);

                    player = await CreateAsync(userChannel, textChannel, cancellationToken);

                    if (player == null)
                        return AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.UnknownError);
                }

                else if (options.RequireSameChannel)
                {
                    if (userChannel == null)
                        return AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.UserNotInVoiceChannel);

                    if (userChannel.Id != player.VoiceChannelId)
                        return AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.VoiceChannelMismatch);
                }

                foreach (var precondition in options.Preconditions)
                {
                    if (!precondition.IsSatisfiedBy(player))
                        return AudioPlayerRetrieveResult.Failed(precondition);
                }

                // Searching outlasts the idle timeout, so hold the sweep off
                player.Reserve(LookupWindow);

                return AudioPlayerRetrieveResult.Success(player);
            }
            finally
            {
                gate.Release();
            }
        }

        private async ValueTask<YtDlpAudioPlayer> CreateAsync(
            IVoiceChannel voiceChannel,
            ITextChannel textChannel,
            CancellationToken cancellationToken)
        {
            var player = new YtDlpAudioPlayer(
                voiceChannel,
                textChannel,
                Client,
                Notifier,
                Options,
                LoggerFactory.CreateLogger<YtDlpAudioPlayer>(),
                Destroy);

            // Published first, as the player subscribes to Disconnected during the
            // handshake and a drop in that window has to find an entry to remove
            _players[voiceChannel.GuildId] = player;

            try
            {
                await player.ConnectAsync(cancellationToken);
            }
            catch (Exception e)
            {
                Logger.Error($"Unable to join voice channel '{voiceChannel.Name}' of guild {voiceChannel.GuildId}", e);

                await player.DisposeAsync();
                return null;
            }

            // A drop while the handshake finishes disposes the player from under us
            if (player.State == AudioPlayerState.Destroyed)
                return null;

            return player;
        }

        private ValueTask Destroy(YtDlpAudioPlayer player)
        {
            _players.TryRemove(new KeyValuePair<ulong, YtDlpAudioPlayer>(player.GuildId, player));
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var player in _players.Values)
            {
                try { await player.DisposeAsync(); }
                catch (Exception e) { Logger.Warning($"Failed to shut down the player of guild {player.GuildId}", e); }
            }

            _players.Clear();
        }
    }
}
