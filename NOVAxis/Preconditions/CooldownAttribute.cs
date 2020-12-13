using System;
using System.Threading.Tasks;

using NOVAxis.Services;

using Discord;
using Discord.Commands;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    class CooldownAttribute : PreconditionAttribute
    {
        private readonly TimeSpan _cooldown;
        private readonly Cache<IUser, int> _users;

        public CooldownAttribute(long millis)
        {
            _users = new Cache<IUser, int>();
            _cooldown = TimeSpan.FromMilliseconds(millis);
        }

        public CooldownAttribute(int seconds) : this(seconds * 1000L) {}

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (Environment.TickCount - _users[context.User] < _cooldown.TotalMilliseconds)
                return Task.FromResult(PreconditionResult.FromError("User has command on cooldown"));

            _users[context.User] = Environment.TickCount & int.MaxValue;
            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
