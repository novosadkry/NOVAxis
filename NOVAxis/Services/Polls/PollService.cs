using System.Collections.Generic;
using System.Collections.Concurrent;

namespace NOVAxis.Services.Polls
{
    public class PollService
    {
        public IEnumerable<PollInteraction> Interactions => _interactions.Values;
        private readonly ConcurrentDictionary<ulong, PollInteraction> _interactions;

        public PollService()
        {
            _interactions = new ConcurrentDictionary<ulong, PollInteraction>();
        }

        public void Add(PollInteraction interaction)
        {
            _interactions.TryAdd(interaction.Poll.Id, interaction);
        }

        public PollInteraction Get(ulong id)
        {
            _interactions.TryGetValue(id, out var interaction);
            return interaction;
        }

        public void Remove(ulong id)
        {
            _interactions.TryRemove(id, out _);
        }
    }
}
