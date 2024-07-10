using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.Extensions.Logging;

using NOVAxis.Extensions;
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
        public ILogger<ChatModule> Logger { get; set; }
        public AnthropicClient AnthropicClient { get; set; }

        private static readonly ImmutableArray<string> AllowedContentTypes
            = ["image/png", "image/jpeg", "image/webp", "image/tiff"];

        private const string SystemPrompt =
            """
            Jsi Discord bot, který komentuje každou zprávu satirickou a útočnou poznámkou.
            Don't mention.
            Don't write messages longer than 50 words.
            Odpovídej pouze v češtině.
            """;

        [RequireOwner]
        [MessageCommand("Reply with AI")]
        public async Task CmdAiReply(IUserMessage message)
        {
            await DeferAsync(ephemeral: true);

            var images = from attachment in message.Attachments
                where AllowedContentTypes.Contains(attachment.ContentType)
                select attachment;

            var promptImages = new List<ImageContent>();

            foreach (var image in images)
            {
                if (image.Width * image.Height > 1_000_000)
                {
                    Logger.Warning($"Message attachment exceeds allowed size, skipping. ({image.Url})");
                    continue;
                }

                promptImages.Add(new ImageContent
                {
                    Source = new ImageSource
                    {
                        MediaType = image.ContentType,
                        Data = await DownloadImage(image.Url)
                    }
                });
            }

            var promptMessage = new Message
            {
                Role = RoleType.User,
                Content = [..promptImages]
            };

            if (!string.IsNullOrEmpty(message.Content))
            {
                promptMessage.Content.Add(new TextContent
                {
                    Text = message.Content
                });
            }

            var request = new MessageParameters
            {
                MaxTokens = 128,
                Model = AnthropicModels.Claude35Sonnet,
                Stream = false,
                Temperature = 1.0m,
                Messages = [promptMessage],
                SystemMessage = SystemPrompt
            };

            var result = await AnthropicClient.Messages.GetClaudeMessageAsync(request);
            await message.ReplyAsync(result.Message);

            await Context.Interaction.DeleteOriginalResponseAsync();
        }

        private async Task<string> DownloadImage(string url)
        {
            using var client = new HttpClient();

            await using var memoryStream = new MemoryStream();
            await using var imageStream = await client.GetStreamAsync(url);
            await imageStream.CopyToAsync(memoryStream);

            var imageBytes = memoryStream.ToArray();
            return Convert.ToBase64String(imageBytes);
        }
    }
}
