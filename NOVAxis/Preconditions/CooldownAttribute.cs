using System;
using System.Threading.Tasks;

using NOVAxis.Utilities;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class CooldownAttribute : PreconditionAttribute
    {
        private struct CooldownInfo
        {
            public IDiscordInteraction Interaction { get; set; }
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

        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            var cooldownInfo = _users[context.User];

            if (cooldownInfo.Interaction == context.Interaction)
                return Task.FromResult(PreconditionResult.FromSuccess());

            if (cooldownInfo.Interaction != null)
            {
                var previous = cooldownInfo.Interaction.CreatedAt;
                var current = context.Interaction.CreatedAt;

                if (current - previous < _cooldown)
                {
                    if (cooldownInfo.WarningTriggered)
                        return Task.FromResult(PreconditionResult.FromError("User has command on cooldown (no warning)"));

                    cooldownInfo.WarningTriggered = true;
                    _users[context.User] = cooldownInfo;

                    return Task.FromResult(PreconditionResult.FromError("User has command on cooldown"));
                }
            }

            _users[context.User] = new CooldownInfo { Interaction = context.Interaction };
            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
