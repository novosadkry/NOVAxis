/*
using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequireGuildRoleAttribute : PreconditionAttribute
    {
        private readonly string[] _requiredRoles;

        public RequireGuildRoleAttribute(string name)
        {
            _requiredRoles = new[] { name };
        }

        public RequireGuildRoleAttribute(string[] names)
        {
            _requiredRoles = names;
        }

        public override async Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            if (context.User is not IGuildUser user)
                return PreconditionResult.FromError("Invalid context for command");

            GuildInfo guildInfo;
            await using (var scope = services.CreateAsyncScope())
            {
                guildInfo = await scope.ServiceProvider
                    .GetService<GuildDbContext>()
                    .Get(context);
            }

            if (guildInfo == null)
                return PreconditionResult.FromSuccess();

            bool match = false;
            foreach (string role in _requiredRoles)
            {
                ulong? id = guildInfo.Roles
                    .Find(x => x.Name == role)?.Id;

                if (!id.HasValue) continue;
                IRole guildRole = context.Guild.GetRole(id.Value);

                // Set flag when the role exists, but the user doesn't have it
                match |= guildRole != null && !user.RoleIds.Contains(id.Value);
            }

            return !match
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError($"User requires guild role '{string.Join(", ", _requiredRoles)}'");
        }
    }
}
*/
