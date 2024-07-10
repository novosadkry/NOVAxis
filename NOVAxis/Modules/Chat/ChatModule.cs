using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.Extensions.Logging;

using NOVAxis.Utilities;
using NOVAxis.Extensions;

using Discord;
using Discord.Interactions;

using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace NOVAxis.Modules.Chat
{
    [RequireContext(ContextType.Guild)]
    [Group("chat", "Chat related commands")]
    public class ChatModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public ILogger<ChatModule> Logger { get; set; }
        public AnthropicClient AnthropicClient { get; set; }
        public InteractionCache InteractionCache { get; set; }

        private static readonly ImmutableArray<string> AllowedContentTypes
            = ["image/png", "image/jpeg", "image/webp", "image/tiff"];

        private const string SystemPrompt =
            """
            Jsi Discord bot, který komentuje každou zprávu satirickou a útočnou poznámkou.
            Don't mention.
            Don't write messages longer than 50 words.
            Odpovídej pouze v češtině a v české abecedě.
            """;

        private static ComponentBuilder AiReplyComponents(ulong id, bool disabled = false) => new ComponentBuilder()
            .WithButton(customId: $"ReplyAI_Accept,{id}", emote: new Emoji("\u2714\ufe0f"), style: ButtonStyle.Success, disabled: disabled)
            .WithButton(customId: $"ReplyAI_Retry,{id}", emote: new Emoji("\u2716"), style: ButtonStyle.Danger, disabled: disabled);

        [RequireOwner]
        [MessageCommand("Reply with AI")]
        public async Task CmdAiReply(IUserMessage message)
        {
            await DeferAsync(ephemeral: true);

            var reply = await RequestAiReply(message);

            var id = InteractionCache.Store(message);
            var components = AiReplyComponents(id).Build();

            await FollowupAsync(reply, ephemeral: true, components: components);
        }

        [ComponentInteraction("ReplyAI_Accept,*", true)]
        public async Task ReplyAI_Accept(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;

            if (InteractionCache[id] is IUserMessage message)
            {
                InteractionCache.Remove(id);

                var reply = interaction.Message.Content;

                await interaction.DeferAsync(ephemeral: true);
                await interaction.DeleteOriginalResponseAsync();

                await message.ReplyAsync(reply);
            }
        }

        [ComponentInteraction("ReplyAI_Retry,*", true)]
        public async Task ReplyAI_Retry(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;

            if (InteractionCache[id] is IUserMessage message)
            {
                await interaction.UpdateAsync(x =>
                {
                    x.Components = AiReplyComponents(id, disabled: true).Build();
                });

                var reply = await RequestAiReply(message);

                await interaction.ModifyOriginalResponseAsync(x =>
                {
                    x.Content = reply;
                    x.Components = AiReplyComponents(id).Build();
                });
            }
        }

        private async Task<string> RequestAiReply(IUserMessage message)
        {
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

            return result.Message;
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
