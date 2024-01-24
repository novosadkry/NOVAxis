using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NOVAxis.Services.Polls
{
    public class PollService
    {
        public ICollection<Poll> Polls => _inner.Values;
        private ConcurrentDictionary<ulong, Poll> _inner;

        public PollService()
        {
            _inner = new ConcurrentDictionary<ulong, Poll>();
        }

        public void Add(Poll poll)
        {
            _inner.TryAdd(poll.Id, poll);
        }

        public Poll Get(ulong id)
        {
            _inner.TryGetValue(id, out var poll);
            return poll;
        }

        public void Remove(ulong id)
        {
            _inner.TryRemove(id, out _);
        }
    }
}
