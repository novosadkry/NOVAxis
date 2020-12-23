using System;
using System.Threading.Tasks;

using NOVAxis.Utilities;

using Discord;
using Discord.Commands;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class CooldownAttribute : PreconditionAttribute
    {
        private struct CooldownInfo
        {
            public IUserMessage Message { get; set; }
            public int Timestamp { get; set; }
        }

        private readonly TimeSpan _cooldown;
        private readonly Cache<IUser, CooldownInfo> _users;

        public CooldownAttribute(long millis)
        {
            _users = new Cache<IUser, CooldownInfo>();
            _cooldown = TimeSpan.FromMilliseconds(millis);
        }

        public CooldownAttribute(int seconds) : this(seconds * 1000L) {}

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var cooldownInfo = _users[context.User];

            /*
                We can assume that if the cooldownInfo contains the same message as the current one,
                there was already a check on this message which passed successfully
            
                If the check doesn't pass, no message's set and this statement is thus skipped
            */
            if (cooldownInfo.Message == context.Message)
                return Task.FromResult(PreconditionResult.FromSuccess());

            if (Environment.TickCount - cooldownInfo.Timestamp < _cooldown.TotalMilliseconds)
                return Task.FromResult(PreconditionResult.FromError("User has command on cooldown"));

            var newInfo = new CooldownInfo
            {
                Message = context.Message,
                Timestamp = Environment.TickCount & int.MaxValue
            };

            _users[context.User] = newInfo;
            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
