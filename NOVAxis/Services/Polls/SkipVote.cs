using System;
using System.Linq;
using System.Threading.Tasks;

using NOVAxis.Services.Audio;

using Discord;

namespace NOVAxis.Services.Polls
{
    /// <summary>
    /// A vote on whether to drop the track being played. Yes and no both count: a skip
    /// which can no longer reach its threshold is settled the moment that becomes true,
    /// rather than leaving everyone waiting out a timer for an answer already known.
    /// </summary>
    public class SkipVote : PollBase
    {
        public const int Yes = 0;
        public const int No = 1;

        public ulong GuildId { get; }

        /// <summary>
        /// The queue entry this vote is about. A vote outlives its usefulness the moment
        /// the track moves on, whatever the reason, so what it was opened against is kept
        /// rather than assumed.
        /// </summary>
        public ulong ItemId { get; }

        public AudioTrack Track { get; }

        /// <summary>Listeners in the channel when it opened.</summary>
        public int Listeners { get; }

        /// <summary>How many yes votes carry it.</summary>
        public int Needed { get; }

        public SkipVote(
            ulong guildId,
            IGuildUser owner,
            AudioTrackQueueItem item,
            int listeners,
            int needed)
            : base(owner, $"Přeskočit „{item.Track.Title}“?", ["Přeskočit", "Nechat hrát"])
        {
            GuildId = guildId;
            ItemId = item.RequestId;
            Track = item.Track;
            Listeners = listeners;
            Needed = needed;
        }

        public int InFavour => Votes.Count(x => x.Value == Yes);
        public int Against => Votes.Count(x => x.Value == No);

        public bool Passed => InFavour >= Needed;

        /// <summary>
        /// True once enough listeners have said no that the rest of them saying yes could
        /// not carry it. Counted against the listeners present when it opened, so someone
        /// leaving the channel cannot decide it on their way out.
        /// </summary>
        public bool Rejected => Listeners - Against < Needed;

        public bool Settled => Passed || Rejected;
    }

    /// <summary>
    /// Closes a skip vote as soon as its answer is known. Pair it with a
    /// <see cref="TimeoutPollTracker"/> through an <see cref="AggregatePollTracker"/> for
    /// the case where too few people ever answer at all.
    /// </summary>
    public class SkipVoteTracker : IPollTracker
    {
        public SkipVote Vote { get; }

        public SkipVoteTracker(SkipVote vote)
        {
            Vote = vote;
        }

        public ValueTask<bool> ShouldClose()
        {
            return new ValueTask<bool>(Vote.State == PollState.Opened && Vote.Settled);
        }

        public ValueTask<bool> ShouldExpire()
        {
            return new ValueTask<bool>(Vote.State == PollState.Closed);
        }
    }
}
