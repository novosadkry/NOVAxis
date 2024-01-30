using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using Discord;

namespace NOVAxis.Services.Polls
{
    public interface IPollTracker
    {
        public ValueTask<bool> ShouldClose();
        public ValueTask<bool> ShouldExpire();
    }

    public interface IPollEmbedBuilder
    {
        public Embed BuildEmbed();
        public MessageComponent BuildComponents();
    }

    public class TimeoutPollTracker : IPollTracker
    {
        public Poll Poll { get; }
        public TimeSpan Timeout { get; }

        public TimeoutPollTracker(Poll poll, TimeSpan timeout)
        {
            Poll = poll;
            Timeout = timeout;
        }

        public ValueTask<bool> ShouldClose()
        {
            var result = Poll.State == PollState.Opened &&
                         Poll.StartTime + Timeout > DateTime.Now;

            return new ValueTask<bool>(result);
        }

        public async ValueTask<bool> ShouldExpire()
        {
            return Poll.State == PollState.Closed || await ShouldClose();
        }
    }

    public class AggregatePollTracker : IPollTracker
    {
        public Poll Poll { get; }
        public List<IPollTracker> Trackers { get; }

        public AggregatePollTracker(Poll poll)
        {
            Poll = poll;
            Trackers = [];
        }

        public AggregatePollTracker(Poll poll, IEnumerable<IPollTracker> trackers)
        {
            Poll = poll;
            Trackers = trackers.ToList();
        }

        public AggregatePollTracker AddTracker(IPollTracker tracker)
        {
            Trackers.Add(tracker);
            return this;
        }

        public async ValueTask<bool> ShouldClose()
        {
            var tasks = Trackers.Select(t => t.ShouldClose().AsTask());
            var results = await Task.WhenAll(tasks);
            return results.Aggregate(false, (current, result) => current || result);
        }

        public async ValueTask<bool> ShouldExpire()
        {
            var tasks = Trackers.Select(t => t.ShouldExpire().AsTask());
            var results = await Task.WhenAll(tasks);
            return results.Aggregate(false, (current, result) => current || result);
        }
    }
}
