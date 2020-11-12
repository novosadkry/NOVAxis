using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Core;
using NOVAxis.Services.Guild;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    class RequireRoleAttribute : PreconditionAttribute
    {
        private readonly string[] _requiredRoles;
        private readonly bool _useGuildInfo;

        public RequireRoleAttribute(string name, bool useGuildInfo = false)
        {
            _requiredRoles = new[] { name };
            _useGuildInfo = useGuildInfo;
        }

        public RequireRoleAttribute(string[] names, bool useGuildInfo = false)
        {
            _requiredRoles = names;
            _useGuildInfo = useGuildInfo;
        }

        public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is SocketGuildUser user)
            {
                bool match = true;

                if (_useGuildInfo)
                {
                    var guildService = Program.Services.GetService<GuildService>();
                    var guildInfo = await guildService.GetInfo(context);

                    foreach (string role in _requiredRoles)
                    {
                        var property = guildInfo.GetType().GetProperty(role);

                        if (property == null)
                            continue;

                        IRole guildRole = context.Guild.GetRole((ulong)property.GetValue(guildInfo));

                        if (guildRole == null)
                            continue;

                        if (!user.Roles.Contains(guildRole))
                            match = false;
                    }
                }

                else
                {
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
                }

                return match 
                    ? PreconditionResult.FromSuccess()
                    : PreconditionResult.FromError($"User requires guild role '{string.Join(", ", _requiredRoles)}'");
            }

            return PreconditionResult.FromError("Invalid context for command");
        }
    }
}
