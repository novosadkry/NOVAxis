using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Utilities;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Preconditions
{
    public record CooldownKey(ulong UserId, string CommandName);

    public struct CooldownInfo
    {
        public ICommandInfo CommandInfo { get; set; }
        public DateTimeOffset LastExecution { get; set; }
        public bool WarningTriggered { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class CooldownAttribute : PreconditionAttribute
    {
        private readonly TimeSpan _cooldown;
        private readonly string _commandName;

        public CooldownAttribute(int seconds, [CallerMemberName] string commandName = null)
        {
            _cooldown = TimeSpan.FromSeconds(seconds);
            _commandName = commandName;
        }

        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            var cooldownCache = services.GetRequiredService<CooldownCache>();
            var commandName = $"{_commandName ?? commandInfo.Module.Name}::{commandInfo.Name}";
            var cooldownKey = new CooldownKey(context.User.Id, commandName);
            var cooldownInfo = cooldownCache[cooldownKey];

            if (cooldownInfo.CommandInfo != null)
            {
                var previous = cooldownInfo.LastExecution;
                var current = context.Interaction.CreatedAt;

                if (current - previous < _cooldown)
                {
                    if (cooldownInfo.WarningTriggered)
                        return Task.FromResult(PreconditionResult.FromError("User has command on cooldown (no warning)"));

                    cooldownInfo.WarningTriggered = true;
                    cooldownCache[cooldownKey] = cooldownInfo;

                    return Task.FromResult(PreconditionResult.FromError("User has command on cooldown"));
                }
            }

            cooldownCache[cooldownKey] = new CooldownInfo
            {
                CommandInfo = commandInfo,
                LastExecution = DateTimeOffset.UtcNow
            };

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
