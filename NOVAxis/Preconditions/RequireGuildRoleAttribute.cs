using System;
using System.Linq;
using System.Threading.Tasks;

using NOVAxis.Database.Guild;
using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

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

        public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is not SocketGuildUser user)
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
                match |= guildRole != null && !user.Roles.Contains(guildRole);
            }

            return !match 
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError($"User requires guild role '{string.Join(", ", _requiredRoles)}'");
        }
    }
}
