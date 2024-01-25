using System;
using System.Collections.Generic;

using Discord;

namespace NOVAxis.Services.Polls
{
    public delegate void PollEventHandler();

    public enum PollState
    {
        Opened,
        Closed,
        Expired
    }

    public class Poll
    {
        public ulong Id { get; }
        public string Question { get; }
        public string[] Options { get; }
        public IGuildUser Owner { get; }

        public DateTime StartTime { get; }
        public PollState State { get; private set; }

        public Dictionary<IGuildUser, int> Votes { get; }
        public IUserMessage InteractionMessage { get; set; }

        public event PollEventHandler OnClosed;
        public event PollEventHandler OnExpired;

        public Poll(IGuildUser owner, string question, string[] options)
        {
            Owner = owner;
            Question = question;
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
            OnClosed?.Invoke();
        }

        public void Expire()
        {
            State = PollState.Expired;
            OnExpired?.Invoke();
        }
    }
}
