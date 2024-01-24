using System;
using System.Threading.Tasks;

using Discord;
using Discord.Interactions;

using NOVAxis.Preconditions;
using NOVAxis.Services.Polls;

namespace NOVAxis.Modules.Polls
{
    [Cooldown(1)]
    [Group("poll", "Create interactive polls")]
    [RequireContext(ContextType.Guild)]
    public class PollModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public class BasicPollModal : IModal
        {
            public string Title => "Nové hlasování";

            [RequiredInput]
            [InputLabel("Zadejte téma pro nové hlasování")]
            [ModalTextInput("subject", maxLength: 100)]
            public string Subject { get; set; }

            [RequiredInput]
            [InputLabel("Možnosti (každá na novém řádku)")]
            [ModalTextInput("options", maxLength: 200, initValue: "Ano\nNe", style: TextInputStyle.Paragraph)]
            public string Options { get; set; }

            public string[] GetOptionsArray()
            {
                return Options.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        public PollService PollService { get; set; }

        [SlashCommand("basic", "Creates a poll on a given subject")]
        public async Task PollStartBasic()
        {
            await RespondWithModalAsync<BasicPollModal>("poll_start_basic");
        }

        [ModalInteraction("poll_start_basic", true)]
        public async Task PollStartBasicHandler(BasicPollModal modal)
        {
            var guildUser = (IGuildUser)Context.User;
            var subject = modal.Subject;
            var options = modal.GetOptionsArray();

            var poll = new PollBuilder(guildUser, subject, options)
                .OnEnded(async poll =>
                {
                    await poll.Message.ModifyAsync(message =>
                    {
                        message.Components = PollEmbedBuilder.ComponentsEnded;
                    });
                })
                .Build();

            PollService.Add(poll);

            var embedBuilder = new PollEmbedBuilder(poll);
            await RespondAsync(
                embed: embedBuilder.BuildEmbed(),
                components: embedBuilder.BuildComponents());

            poll.Message = await GetOriginalResponseAsync();
        }

        [ComponentInteraction("poll_end_*", true)]
        public async Task PollEnd(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;
            var poll = PollService.Get(id);

            if (poll == null)
            {
                await interaction!.UpdateAsync(message =>
                {
                    message.Components = PollEmbedBuilder.ComponentsEnded;
                });

                return;
            }

            if (poll.Owner != Context.User)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Hlasování může ukončit pouze ten, kdo jej vytvořil)")
                    .WithTitle("Pro tuto akci nemáš dostatečnou kvalifikaci")
                    .Build());

                return;
            }

            await poll.End();
            await DeferAsync();
        }

        [ComponentInteraction("poll_vote_*,*", true)]
        public async Task PollVote(ulong id, int optionIndex)
        {
            var guildUser = (IGuildUser)Context.User;
            var interaction = (IComponentInteraction)Context.Interaction;

            var poll = PollService.Get(id);
            var pollBuilder = new PollEmbedBuilder(poll);

            if (poll == null)
            {
                await interaction!.UpdateAsync(message =>
                {
                    message.Components = PollEmbedBuilder.ComponentsEnded;
                });

                return;
            }

            var votes = poll.Votes;

            if (votes.TryGetValue(guildUser, out var value) && value == optionIndex)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Hlasovat lze pouze jednou)")
                    .WithTitle("Mé jádro nemůže akceptovat duplicitní hlasy")
                    .Build());

                return;
            }

            votes[guildUser] = optionIndex;

            await interaction!.UpdateAsync(message =>
            {
                message.Embed = pollBuilder.BuildEmbed();
            });
        }
    }
}
