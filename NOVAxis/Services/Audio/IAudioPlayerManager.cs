using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Services.Audio
{
    public enum AudioPlayerRetrieveStatus
    {
        Success,
        UserNotInVoiceChannel,
        VoiceChannelMismatch,
        BotNotConnected,
        PreconditionFailed,
        UnknownError
    }

    /// <summary>
    /// A requirement a player has to satisfy before a command is allowed to run.
    /// </summary>
    public enum AudioPrecondition
    {
        Playing,
        NotPlaying,
        Paused,
        NotPaused,
        QueueEmpty,
        QueueNotEmpty
    }

    public readonly record struct AudioPlayerRetrieveResult(
        IAudioPlayer Player,
        AudioPlayerRetrieveStatus Status,
        AudioPrecondition? Precondition = null)
    {
        public static AudioPlayerRetrieveResult Success(IAudioPlayer player)
            => new(player, AudioPlayerRetrieveStatus.Success);

        public static AudioPlayerRetrieveResult Failed(AudioPlayerRetrieveStatus status)
            => new(null, status);

        public static AudioPlayerRetrieveResult Failed(AudioPrecondition precondition)
            => new(null, AudioPlayerRetrieveStatus.PreconditionFailed, precondition);
    }

    public record AudioPlayerRetrieveOptions
    {
        /// <summary>
        /// Whether the bot may join the caller's voice channel when it isn't connected yet.
        /// </summary>
        public bool JoinChannel { get; init; } = true;

        /// <summary>
        /// Whether the caller has to be in the very same voice channel as the bot.
        /// </summary>
        public bool RequireSameChannel { get; init; }

        public IReadOnlyList<AudioPrecondition> Preconditions { get; init; } = Array.Empty<AudioPrecondition>();
    }

    public interface IAudioPlayerManager
    {
        IReadOnlyCollection<IAudioPlayer> Players { get; }

        bool TryGetPlayer(ulong guildId, out IAudioPlayer player);

        ValueTask<AudioPlayerRetrieveResult> RetrieveAsync(
            IInteractionContext context,
            AudioPlayerRetrieveOptions options,
            CancellationToken cancellationToken = default);
    }

    public static class AudioPreconditionExtensions
    {
        public static bool IsSatisfiedBy(this AudioPrecondition precondition, IAudioPlayer player)
        {
            return precondition switch
            {
                AudioPrecondition.Playing => player.State is AudioPlayerState.Playing or AudioPlayerState.Paused,
                AudioPrecondition.NotPlaying => player.State is AudioPlayerState.NotPlaying,
                AudioPrecondition.Paused => player.IsPaused,
                AudioPrecondition.NotPaused => !player.IsPaused,
                AudioPrecondition.QueueEmpty => player.Queue.Count == 0,
                AudioPrecondition.QueueNotEmpty => player.Queue.Count > 0,
                _ => true
            };
        }
    }
}
