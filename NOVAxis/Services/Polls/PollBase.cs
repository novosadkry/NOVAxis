using System;
using System.Collections.Generic;

using Discord;

namespace NOVAxis.Services.Polls
{
    public enum PollState
    {
        Opened,
        Closed,
        Expired
    }

    public class PollBase
    {
        public ulong Id { get; }
        public string Subject { get; }
        public string[] Options { get; }
        public IGuildUser Owner { get; }

        public DateTime StartTime { get; }
        public PollState State { get; private set; }

        public Dictionary<IGuildUser, int> Votes { get; }

        public PollBase(IGuildUser owner, string subject, string[] options)
        {
            Owner = owner;
            Subject = subject;
            Options = options;
            State = PollState.Opened;
            StartTime = DateTime.Now;
            Id = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now);
            Votes = new Dictionary<IGuildUser, int>();
        }

        public bool AddVote(IGuildUser user, int optionIndex)
        {
            if (State != PollState.Opened)
                throw new InvalidOperationException("Poll is in closed state");

            if (Votes.TryGetValue(user, out var value) && value == optionIndex)
                return false;

            Votes[user] = optionIndex;
            return true;
        }

        public void Close()
        {
            State = PollState.Closed;
        }

        public void Expire()
        {
            State = PollState.Expired;
        }
    }
}
