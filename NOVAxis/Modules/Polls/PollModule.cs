using System;
using System.Threading.Tasks;

using Discord;
using Discord.Interactions;

using NOVAxis.Preconditions;
using NOVAxis.Services.Polls;

namespace NOVAxis.Modules.Polls
{
    [Cooldown(1)]
    [RequireContext(ContextType.Guild)]
    public class PollModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public PollService PollService { get; set; }

        [SlashCommand("poll", "Creates a poll on a given subject")]
        public async Task PollStart()
        {
            await RespondWithModalAsync<PollModal>(nameof(PollStartHandler));
        }

        [ModalInteraction(nameof(PollStartHandler), true)]
        public async Task PollStartHandler(PollModal modal)
        {
            var guildUser = (IGuildUser)Context.User;
            var subject = modal.Subject;
            var options = modal.GetOptionsArray();

            var poll = new Poll(guildUser, subject, options);
            var pollInteraction = new PollInteraction
            {
                Poll = poll,
                Builder = new PollEmbedBuilder(poll),
                Tracker = new TimeoutPollTracker(poll, TimeSpan.FromMinutes(15))
            };

            PollService.Add(pollInteraction);

            var embedBuilder = new PollEmbedBuilder(poll);
            await RespondAsync(
                embed: embedBuilder.BuildEmbed(),
                components: embedBuilder.BuildComponents());

            pollInteraction.Message = await GetOriginalResponseAsync();
        }

        [ComponentInteraction("poll_close_*", true)]
        public async Task PollClose(ulong id)
        {
            var interaction = (IComponentInteraction)Context.Interaction;
            var pollInteraction = PollService.Get(id);

            if (pollInteraction == null)
            {
                await interaction!.UpdateAsync(message =>
                {
                    message.Components = PollEmbedBuilder
                        .ComponentsExpired.Build();
                });

                return;
            }

            var poll = pollInteraction.Poll;

            if (poll.Owner != Context.User)
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Hlasování může ukončit pouze ten, kdo jej vytvořil)")
                    .WithTitle("Pro tuto akci nemáš dostatečnou kvalifikaci")
                    .Build());

                return;
            }

            await pollInteraction.Close();

            await DeferAsync();
        }

        [ComponentInteraction("poll_vote_*,*", true)]
        public async Task PollVote(ulong id, int optionIndex)
        {
            var guildUser = (IGuildUser)Context.User;
            var interaction = (IComponentInteraction)Context.Interaction;

            var pollInteraction = PollService.Get(id);

            if (pollInteraction == null)
            {
                await interaction!.UpdateAsync(message =>
                {
                    message.Components = PollEmbedBuilder
                        .ComponentsExpired.Build();
                });

                return;
            }

            var poll = pollInteraction.Poll;

            if (!poll.AddVote(guildUser, optionIndex))
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Hlasovat lze pouze jednou)")
                    .WithTitle("Mé jádro nemůže akceptovat duplicitní hlasy")
                    .Build());

                return;
            }

            await pollInteraction.Rebuild();
        }
    }
}
