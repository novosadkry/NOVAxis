using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using NOVAxis.Core;

using Discord;

using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Preconditions;

namespace NOVAxis.Services.Audio.Lavalink
{
    public class LavalinkAudioPlayerManager : IAudioPlayerManager
    {
        private static readonly IReadOnlyDictionary<AudioPrecondition, IPlayerPrecondition> Preconditions =
            new Dictionary<AudioPrecondition, IPlayerPrecondition>
            {
                [AudioPrecondition.Playing] = PlayerPrecondition.Playing,
                [AudioPrecondition.NotPlaying] = PlayerPrecondition.NotPlaying,
                [AudioPrecondition.Paused] = PlayerPrecondition.Paused,
                [AudioPrecondition.NotPaused] = PlayerPrecondition.NotPaused,
                [AudioPrecondition.QueueEmpty] = PlayerPrecondition.QueueEmpty,
                [AudioPrecondition.QueueNotEmpty] = PlayerPrecondition.QueueNotEmpty
            };

        private IAudioService AudioService { get; }
        private IOptions<AudioOptions> Options { get; }

        public LavalinkAudioPlayerManager(IAudioService audioService, IOptions<AudioOptions> options)
        {
            AudioService = audioService;
            Options = options;
        }

        public IReadOnlyCollection<IAudioPlayer> Players
            => AudioService.Players.GetPlayers<LavalinkAudioPlayer>().ToList();

        public bool TryGetPlayer(ulong guildId, out IAudioPlayer player)
        {
            player = AudioService.Players
                .GetPlayers<LavalinkAudioPlayer>()
                .FirstOrDefault(x => x.GuildId == guildId);

            return player != null;
        }

        public async ValueTask<AudioPlayerRetrieveResult> RetrieveAsync(
            IInteractionContext context,
            AudioPlayerRetrieveOptions options,
            CancellationToken cancellationToken = default)
        {
            var playerOptions = new LavalinkAudioPlayerOptions
            {
                TextChannel = context.Channel as ITextChannel,
                InitialVolume = 1.0f,
                DisconnectOnDestroy = true
            };

            var retrieveOptions = new PlayerRetrieveOptions(
                options.JoinChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
                options.RequireSameChannel ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore,
                Preconditions: options.Preconditions.Select(x => Preconditions[x]).ToImmutableArray());

            var result = await AudioService.Players.RetrieveAsync<LavalinkAudioPlayer, LavalinkAudioPlayerOptions>(
                context, LavalinkAudioPlayer.CreatePlayerAsync, playerOptions, retrieveOptions, cancellationToken);

            return result.Status switch
            {
                PlayerRetrieveStatus.Success
                    => AudioPlayerRetrieveResult.Success(result.Player),

                PlayerRetrieveStatus.UserNotInVoiceChannel
                    => AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.UserNotInVoiceChannel),

                PlayerRetrieveStatus.VoiceChannelMismatch
                    => AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.VoiceChannelMismatch),

                PlayerRetrieveStatus.BotNotConnected
                    => AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.BotNotConnected),

                PlayerRetrieveStatus.PreconditionFailed
                    => Unmap(result.Precondition),

                _ => AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.UnknownError)
            };
        }

        private static AudioPlayerRetrieveResult Unmap(IPlayerPrecondition precondition)
        {
            foreach (var (key, value) in Preconditions)
            {
                if (ReferenceEquals(value, precondition))
                    return AudioPlayerRetrieveResult.Failed(key);
            }

            return AudioPlayerRetrieveResult.Failed(AudioPlayerRetrieveStatus.UnknownError);
        }
    }
}
