using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Utilities;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Preconditions
{
    public struct CooldownInfo
    {
        public IDiscordInteraction Interaction { get; set; }
        public bool WarningTriggered { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class CooldownAttribute : PreconditionAttribute
    {
        private readonly TimeSpan _cooldown;

        public CooldownAttribute(int seconds)
        {
            _cooldown = TimeSpan.FromSeconds(seconds);
        }

        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            var cooldownCache = services.GetRequiredService<CooldownCache>();
            var cooldownInfo = cooldownCache[context.User];

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
                    cooldownCache[context.User] = cooldownInfo;

                    return Task.FromResult(PreconditionResult.FromError("User has command on cooldown"));
                }
            }

            cooldownCache[context.User] = new CooldownInfo { Interaction = context.Interaction };
            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
