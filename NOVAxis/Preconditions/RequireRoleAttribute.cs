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
        private readonly string[] _roles;

        public RequireRoleAttribute(string name)
        {
            _roles = new[] { name };
        }

        public RequireRoleAttribute(string[] names)
        {
            _roles = names;
        }

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is SocketGuildUser user)
            {
                bool match = true;

                foreach (string name in _roles)
                {
                    IRole role = (from r in context.Guild.Roles
                                  where r.Name.Contains(name)
                                  select r).Single();

                    if (!user.Roles.Contains(role))
                        match = false;
                }


                return match 
                    ? Task.FromResult(PreconditionResult.FromSuccess())
                    : Task.FromResult(PreconditionResult.FromError($"User requires guild role '{string.Join(", ", _roles)}'"));
            }

            return Task.FromResult(PreconditionResult.FromError("Invalid context for command"));
        }
    }
}
