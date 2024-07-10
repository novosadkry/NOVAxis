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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace NOVAxis.Modules.Chat
{
    [RequireContext(ContextType.Guild)]
    [Group("chat", "Chat related commands")]
    public class ChatModule : InteractionModuleBase<ShardedInteractionContext>
    {
        private class AiReplyContext
        {
            public IUserMessage UserMessage { get; set; }
            public List<ImageContent> PromptImages { get; set; }
        }

        public ILogger<ChatModule> Logger { get; set; }
        public AnthropicClient AnthropicClient { get; set; }
        public InteractionCache InteractionCache { get; set; }

        private static readonly ImmutableArray<string> AllowedContentTypes
            = ["image/png", "image/jpeg", "image/webp", "image/gif"];

        private const string SystemPrompt =
            """
            Jsi Discord bot, který komentuje každou zprávu satirickou a útočnou poznámkou.
            Don't mention.
            Don't write messages longer than 50 words.
            Odpovídej pouze v češtině a v české abecedě.
            """;

        private static ComponentBuilder AiReplyComponents(ulong id, bool disabled = false) => new ComponentBuilder()
            .WithButton(customId: $"ReplyAI_Accept,{id}", emote: new Emoji("\u2714\ufe0f"), style: ButtonStyle.Success, disabled: disabled)
            .WithButton(customId: $"ReplyAI_Retry,{id}", emote: new Emoji("\u2716"), style: ButtonStyle.Danger, disabled: disabled)
            .WithButton(customId: $"ReplyAI_Cancel,{id}", emote: new Emoji("\ud83d\uddd1\ufe0f"), style: ButtonStyle.Secondary, disabled: disabled);

        [RequireOwner]
        [MessageCommand("Reply with AI")]
        public async Task CmdAiReply(IUserMessage message)
        {
            await DeferAsync(ephemeral: true);

            var context = new AiReplyContext
            {
                UserMessage = message,
                PromptImages = await GetPromptImages(message)
            };

            var reply = await RequestAiReply(context);

            if (reply is null)
            {
                await FollowupAsync("Pro tuhle zprávu nelze vytvořit odpověď.", ephemeral: true);
                return;
            }

            var id = InteractionCache.Store(context);
            var components = AiReplyComponents(id).Build();

            await FollowupAsync(reply, ephemeral: true, components: components);
        }

        [ComponentInteraction("ReplyAI_Accept,*", true)]
        public async Task ReplyAI_Accept(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;

            if (InteractionCache[id] is AiReplyContext context)
            {
                InteractionCache.Remove(id);

                var reply = interaction.Message.Content;

                await interaction.DeferAsync(ephemeral: true);
                await interaction.DeleteOriginalResponseAsync();

                await context.UserMessage.ReplyAsync(reply);
            }
        }

        [ComponentInteraction("ReplyAI_Retry,*", true)]
        public async Task ReplyAI_Retry(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;

            if (InteractionCache[id] is AiReplyContext context)
            {
                await interaction.UpdateAsync(x =>
                {
                    x.Components = AiReplyComponents(id, disabled: true).Build();
                });

                var reply = await RequestAiReply(context);

                if (reply is null)
                {
                    InteractionCache.Remove(id);

                    await interaction.ModifyOriginalResponseAsync(x =>
                    {
                        x.Content = "Pro tuhle zprávu nelze vytvořit odpověď.";
                        x.Components = AiReplyComponents(id, disabled: true).Build();
                    });

                    return;
                }

                await interaction.ModifyOriginalResponseAsync(x =>
                {
                    x.Content = reply;
                    x.Components = AiReplyComponents(id).Build();
                });
            }
        }

        [ComponentInteraction("ReplyAI_Cancel,*", true)]
        public async Task ReplyAI_Cancel(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;

            if (InteractionCache[id] is AiReplyContext)
            {
                InteractionCache.Remove(id);

                await interaction.DeferAsync(ephemeral: true);
                await interaction.DeleteOriginalResponseAsync();
            }
        }

        private async Task<List<ImageContent>> GetPromptImages(IMessage message)
        {
            var images = from attachment in message.Attachments
                where AllowedContentTypes.Contains(attachment.ContentType)
                select attachment;

            var promptImages = new List<ImageContent>();

            foreach (var image in images)
            {
                promptImages.Add(new ImageContent
                {
                    Source = new ImageSource
                    {
                        MediaType = image.ContentType,
                        Data = await DownloadImage(image.Url)
                    }
                });
            }

            return promptImages;
        }

        private async Task<string> RequestAiReply(AiReplyContext context)
        {
            var promptMessage = new Message
            {
                Role = RoleType.User,
                Content = [..context.PromptImages]
            };

            if (!string.IsNullOrEmpty(context.UserMessage.Content))
            {
                promptMessage.Content.Add(new TextContent
                {
                    Text = context.UserMessage.Content
                });
            }

            if (promptMessage.Content.Count == 0)
                return null;

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

        private async Task<string> DownloadImage(string url, int maxImageSize = 1_000_000)
        {
            using var image = await LoadImage();
            var imageSize = image.Width * image.Height;

            if (imageSize > maxImageSize)
            {
                var reduction = maxImageSize / (float) imageSize;
                var newWidth = (int)(reduction * image.Width);
                var newHeight = (int)(reduction * image.Height);

                Logger.Debug($"Resizing image from w:{image.Width} h:{image.Height} to w:{newWidth} h:{newHeight}");

                image.Mutate(x => x.Resize(newWidth, newHeight));
            }

            await using var memoryStream = new MemoryStream();
            await image.SaveAsync(memoryStream, image.Metadata.DecodedImageFormat!);

            return Convert.ToBase64String(memoryStream.ToArray());

            async Task<Image> LoadImage()
            {
                using var client = new HttpClient();
                await using var imageStream = await client.GetStreamAsync(url);
                return await Image.LoadAsync(imageStream);
            }
        }
    }
}
