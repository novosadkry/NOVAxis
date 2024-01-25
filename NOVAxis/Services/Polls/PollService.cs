using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NOVAxis.Services.Polls
{
    public interface IPollTracker
    {
        public Poll Poll { get; }
        public bool ShouldClose();
        public bool ShouldExpire();
    }

    public class PollService
    {
        public IEnumerable<IPollTracker> Trackers => _trackers.Values;

        private ConcurrentDictionary<ulong, IPollTracker> _trackers;

        public PollService()
        {
            _trackers = new ConcurrentDictionary<ulong, IPollTracker>();
        }

        public void Add(IPollTracker tracker)
        {
            _trackers.TryAdd(tracker.Poll.Id, tracker);
        }

        public IPollTracker Get(ulong id)
        {
            _trackers.TryGetValue(id, out var tracker);
            return tracker;
        }

        public void Remove(ulong id)
        {
            _trackers.TryRemove(id, out _);
        }
    }
}
