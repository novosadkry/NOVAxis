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
        public PollService PollService { get; set; }

        [SlashCommand("question", "Creates a poll on a given question")]
        public async Task PollStartQuestion()
        {
            await RespondWithModalAsync<QuestionPollModal>(nameof(PollStartQuestionHandler));
        }

        [ModalInteraction(nameof(PollStartQuestionHandler), true)]
        public async Task PollStartQuestionHandler(QuestionPollModal modal)
        {
            var guildUser = (IGuildUser)Context.User;
            var question = modal.Question;
            var options = modal.GetOptionsArray();

            var poll = new QuestionPollBuilder()
                .WithOwner(guildUser)
                .WithQuestion(question)
                .WithOptions(options)
                .Build();

            PollService.Add(new QuestionPollTracker(poll, TimeSpan.FromMinutes(15)));

            var embedBuilder = new PollEmbedBuilder(poll);
            await RespondAsync(
                embed: embedBuilder.BuildEmbed(),
                components: embedBuilder.BuildComponents());

            poll.InteractionMessage = await GetOriginalResponseAsync();
        }

        [ComponentInteraction("poll_close_*", true)]
        public async Task PollClose(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;
            var pollTracker = PollService.Get(id);

            if (pollTracker == null)
            {
                await interaction!.UpdateAsync(message =>
                {
                    message.Components = PollEmbedBuilder.ComponentsExpired;
                });

                return;
            }

            var poll = pollTracker.Poll;

            if (poll.Owner != Context.User)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Hlasování může ukončit pouze ten, kdo jej vytvořil)")
                    .WithTitle("Pro tuto akci nemáš dostatečnou kvalifikaci")
                    .Build());

                return;
            }

            await poll.Close();

            await DeferAsync();
        }

        [ComponentInteraction("poll_vote_*,*", true)]
        public async Task PollVote(ulong id, int optionIndex)
        {
            var guildUser = (IGuildUser)Context.User;
            var interaction = (IComponentInteraction)Context.Interaction;

            var pollTracker = PollService.Get(id);

            if (pollTracker == null)
            {
                await interaction!.UpdateAsync(message =>
                {
                    message.Components = PollEmbedBuilder.ComponentsExpired;
                });

                return;
            }

            var poll = pollTracker.Poll;
            var pollBuilder = new PollEmbedBuilder(poll);

            if (!poll.AddVote(guildUser, optionIndex))
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Hlasovat lze pouze jednou)")
                    .WithTitle("Mé jádro nemůže akceptovat duplicitní hlasy")
                    .Build());

                return;
            }

            await interaction!.UpdateAsync(message =>
            {
                message.Embed = pollBuilder.BuildEmbed();
            });
        }
    }
}
