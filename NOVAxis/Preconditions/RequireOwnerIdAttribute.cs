using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using NOVAxis.Core;

namespace NOVAxis.Preconditions
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequireOwnerIdAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo command, IServiceProvider services)
        {
            return context.Client.TokenType switch
            {
                TokenType.Bot => Task.FromResult(context.User.Id != Program.OwnerId
                    ? PreconditionResult.FromError(ErrorMessage ?? "Command can only be run by the owner of the bot.")
                    : PreconditionResult.FromSuccess()),

                _ => Task.FromResult(PreconditionResult.FromError(
                    $"{nameof(RequireOwnerIdAttribute)} is not supported by this {nameof(TokenType)}."))
            };
        }
    }
}
