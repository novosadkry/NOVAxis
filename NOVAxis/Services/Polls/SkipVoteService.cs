using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using NOVAxis.Core;

using Discord;

namespace NOVAxis.Services.Polls
{
    /// <summary>
    /// Decides when skipping stops being one person's call, and keeps the one open vote
    /// each guild is allowed.
    /// </summary>
    public class SkipVoteService
    {
        private readonly ConcurrentDictionary<ulong, PollInteraction> _open = new();

        private PollService Polls { get; }
        private IOptions<AudioOptions> Options { get; }

        public SkipVoteService(PollService polls, IOptions<AudioOptions> options)
        {
            Polls = polls;
            Options = options;
        }

        private AudioVoteOptions Settings => Options.Value.Vote;

        public TimeSpan Timeout => Settings.Timeout;

        /// <summary>
        /// Whether a channel of this size has to be asked. A room small enough to ask out
        /// loud in is left to sort itself out.
        /// </summary>
        public bool Required(int listeners)
        {
            return Settings.Active && listeners > Math.Max(Settings.MinListeners, 1);
        }

        /// <summary>
        /// Yes votes needed, never fewer than two - a vote one person can carry alone is
        /// the thing this exists to stop.
        /// </summary>
        public int Needed(int listeners)
        {
            var share = (int)Math.Ceiling(listeners * Math.Clamp(Settings.Ratio, 0.1, 1.0));
            return Math.Clamp(share, 2, Math.Max(listeners, 2));
        }

        /// <summary>
        /// How many people are actually listening: everyone in the channel who is neither
        /// a bot nor deafened, because someone who cannot hear it has no stake in it.
        /// </summary>
        public static async ValueTask<int> ListenersAsync(IVoiceChannel channel)
        {
            if (channel == null)
                return 0;

            var users = await channel.GetUsersAsync().FlattenAsync();

            return users.Count(x => !x.IsBot && x.IsDeafened != true && x.IsSelfDeafened != true);
        }

        /// <summary>
        /// The vote this guild has open, or null. A vote about a track which is no longer
        /// the one playing is not an open vote - it is one nobody got round to answering,
        /// and it is dropped here rather than left to confuse the next one.
        /// </summary>
        public PollInteraction Current(ulong guildId, ulong? currentItemId)
        {
            if (!_open.TryGetValue(guildId, out var interaction))
                return null;

            var vote = (SkipVote)interaction.Poll;

            if (vote.State == PollState.Opened && vote.ItemId == currentItemId)
                return interaction;

            Forget(guildId);

            return null;
        }

        public void Add(ulong guildId, PollInteraction interaction)
        {
            _open[guildId] = interaction;
            Polls.Add(interaction);
        }

        /// <summary>
        /// Drops the guild's vote from the open set. The interaction stays with
        /// <see cref="PollService"/>, which retires it on its own next sweep.
        /// </summary>
        public void Forget(ulong guildId)
        {
            _open.TryRemove(guildId, out _);
        }
    }
}
