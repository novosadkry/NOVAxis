using System;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    class RequireRoleAttribute : PreconditionAttribute
    {
        private readonly string[] _requiredRoles;

        public RequireRoleAttribute(string name)
        {
            _requiredRoles = new[] { name };
        }

        public RequireRoleAttribute(string[] names)
        {
            _requiredRoles = names;
        }

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is SocketGuildUser user)
            {
                bool match = true;

                foreach (string role in _requiredRoles)
                {
                    IRole guildRole = (from r in context.Guild.Roles
                                  where r.Name.Contains(role)
                                  select r).FirstOrDefault();

                    if (guildRole == null)
                        continue;

                    if (!user.Roles.Contains(guildRole))
                        match = false;
                }


                return match 
                    ? Task.FromResult(PreconditionResult.FromSuccess())
                    : Task.FromResult(PreconditionResult.FromError($"User requires guild role '{string.Join(", ", _requiredRoles)}'"));
            }

            return Task.FromResult(PreconditionResult.FromError("Invalid context for command"));
        }
    }
}
