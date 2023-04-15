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
            public DateTimeOffset? Timestamp { get; set; }
            public bool WarningTriggered { get; set; }
        }

        private readonly TimeSpan _cooldown;
        private readonly Cache<IUser, CooldownInfo> _users;

        public CooldownAttribute(long millis)
        {
            _users = new Cache<IUser, CooldownInfo>();
            _cooldown = TimeSpan.FromMilliseconds(millis);
        }

        public CooldownAttribute(int seconds)
            : this(seconds * 1000L) { }

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var cooldownInfo = _users[context.User];
            var currentTime = context.Message.CreatedAt;

            if (cooldownInfo.Message == context.Message)
                return Task.FromResult(PreconditionResult.FromSuccess());

            if (cooldownInfo.Timestamp.HasValue && currentTime - cooldownInfo.Timestamp < _cooldown)
            {
                if (cooldownInfo.WarningTriggered)
                    return Task.FromResult(PreconditionResult.FromError("User has command on cooldown (no warning)"));
                
                cooldownInfo.WarningTriggered = true;
                _users[context.User] = cooldownInfo;
                
                return Task.FromResult(PreconditionResult.FromError("User has command on cooldown"));
            }

            _users[context.User] = new CooldownInfo
            {
                Message = context.Message,
                Timestamp = currentTime
            };
            
            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
