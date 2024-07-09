using System.Threading.Tasks;

using NOVAxis.Preconditions;

using Discord;
using Discord.Interactions;

using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace NOVAxis.Modules.Chat
{
    [Cooldown(5)]
    [RequireContext(ContextType.Guild)]
    [Group("chat", "Chat related commands")]
    public class ChatModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public AnthropicClient AnthropicClient { get; set; }

        [RequireOwner]
        [MessageCommand("Reply with AI")]
        public async Task CmdAiReply(IUserMessage message)
        {
            var request = new MessageParameters
            {
                MaxTokens = 128,
                Model = AnthropicModels.Claude35Sonnet,
                Stream = false,
                Temperature = 1.0m,
                Messages = [new Message(RoleType.User, message.Content)],
                SystemMessage = """
                                Jsi Discord bot, který komentuje každou zprávu satirickou a útočnou poznámkou.
                                Don't mention.
                                Don't write messages longer than 50 words.
                                """
            };

            await DeferAsync(ephemeral: true);

            var result = await AnthropicClient.Messages.GetClaudeMessageAsync(request);
            await message.ReplyAsync(result.Message);

            await Context.Interaction.DeleteOriginalResponseAsync();
        }
    }
}
