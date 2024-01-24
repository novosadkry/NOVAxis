using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;

namespace NOVAxis.Services.Polls
{
    public enum PollState
    {
        Opened,
        Closed,
        Ended
    }

    public class PollBuilder
    {
        private Poll Poll { get; }

        public PollBuilder(IGuildUser owner, string subject, string[] options)
        {
            Poll = new Poll
            {
                Owner = owner,
                Subject = subject,
                Options = options,
                State = PollState.Opened,
                Votes = new Dictionary<IGuildUser, int>()
            };
        }

        public PollBuilder CloseWhen(Func<bool> cond)
        {
            Poll.CloseCondition = () => ValueTask.FromResult(cond());
            return this;
        }

        public PollBuilder CloseWhen(Func<ValueTask<bool>> cond)
        {
            Poll.CloseCondition = cond;
            return this;
        }

        public PollBuilder EndWhen(Func<bool> cond)
        {
            Poll.EndCondition = () => ValueTask.FromResult(cond());
            return this;
        }

        public PollBuilder EndWhen(Func<ValueTask<bool>> cond)
        {
            Poll.EndCondition = cond;
            return this;
        }

        public PollBuilder OnClosed(Func<Poll, ValueTask> action)
        {
            Poll.OnClosed = action;
            return this;
        }

        public PollBuilder OnEnded(Func<Poll, ValueTask> action)
        {
            Poll.OnEnded = action;
            return this;
        }

        public Poll Build()
        {
            return Poll;
        }
    }

    public class Poll
    {
        public ulong Id { get; }
        public string Subject { get; set; }
        public string[] Options { get; set; }
        public PollState State { get; set; }
        public IGuildUser Owner { get; set; }
        public Dictionary<IGuildUser, int> Votes { get; set; }

        public Func<ValueTask<bool>> EndCondition { get; set; }
        public Func<ValueTask<bool>> CloseCondition { get; set; }

        public Func<Poll, ValueTask> OnClosed { get; set; }
        public Func<Poll, ValueTask> OnEnded { get; set; }

        public IUserMessage Message { get; set; }

        public Poll()
        {
            Id = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now);
        }

        public async Task Close()
        {
            State = PollState.Closed;
            await OnClosed(this);
        }

        public async Task End()
        {
            State = PollState.Ended;
            await OnEnded(this);
        }
    }
}
