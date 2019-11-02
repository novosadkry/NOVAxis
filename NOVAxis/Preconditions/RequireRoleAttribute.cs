using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    class RequireRoleAttribute : PreconditionAttribute
    {
        private readonly string[] roles;

        public RequireRoleAttribute(string name)
        {
            roles = new string[] { name };
        }

        public RequireRoleAttribute(string[] names)
        {
            roles = names;
        }

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is SocketGuildUser user)
            {
                bool match = true;

                foreach (string name in roles)
                {
                    IRole role = (from r in context.Guild.Roles
                                  where r.Name.Contains(name)
                                  select r).Single();

                    if (!user.Roles.Contains(role))
                        match = false;
                }

                if (match)
                    return Task.FromResult(PreconditionResult.FromSuccess());

                else
                    return Task.FromResult(PreconditionResult.FromError($"User requires guild role '{string.Join(", ", roles)}'"));
            }

            else
                return Task.FromResult(PreconditionResult.FromError("Invalid context for command"));
        }
    }
}
