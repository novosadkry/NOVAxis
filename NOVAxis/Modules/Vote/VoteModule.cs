using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Interactions;

using NOVAxis.Preconditions;
using NOVAxis.Utilities;

namespace NOVAxis.Modules.Vote
{
    [Cooldown(1)]
    [RequireContext(ContextType.Guild)]
    public class VoteModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public InteractionCache InteractionCache { get; set; }

        [SlashCommand("vote", "Creates a vote on a given subject")]
        public async Task StartVote()
        {
            await RespondWithModalAsync<StartVoteModal>(nameof(StartVote));
        }

        [ModalInteraction(nameof(StartVote), true)]
        public async Task StartVoteModalResponse(StartVoteModal modal)
        {
            var voteContext = new VoteContext(modal);

            var embed = BuildVoteEmbed(voteContext);
            var id = InteractionCache.Store(voteContext);

            var options = voteContext.Options;
            var builder = new ComponentBuilder();

            for (int i = 0; i < options.Length; i++)
            {
                if (!string.IsNullOrEmpty(options[i]))
                    builder.WithButton(options[i], $"vote_{id},{i}", ButtonStyle.Success);
            }

            await RespondAsync(embed: embed, components: builder.Build());
        }

        [ComponentInteraction("vote_*,*")]
        public async Task VoteFor(ulong id, int optionIndex)
        {
            var guildUser = (IGuildUser) Context.User;
            var interaction = (IComponentInteraction) Context.Interaction;

            if (InteractionCache[id] is not VoteContext voteContext)
            {
                await interaction!.UpdateAsync(message =>
                {
                    message.Components = new ComponentBuilder()
                        .WithButton("Hlasování skončilo", "vote_ended", ButtonStyle.Secondary, disabled: true)
                        .Build();
                });

                return;
            }

            var votes = voteContext.Votes;
            var vote = new Vote(guildUser, optionIndex);

            if (votes.Contains(vote))
            {
                await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Hlasovat lze pouze jednou)")
                    .WithTitle("Mé jádro nemůže akceptovat duplicitní hlasy")
                    .Build());

                return;
            }

            // Remove all existing votes
            votes.RemoveAll(x => x.User == guildUser);
            votes.Add(vote); // and add the current vote

            await interaction!.UpdateAsync(message =>
            {
                message.Embed = BuildVoteEmbed(voteContext);
            });
        }

        private Embed BuildVoteEmbed(VoteContext voteContext)
        {
            var votes = voteContext.Votes;
            var votesTotal = voteContext.Votes.Count;
            var options = voteContext.Options;

            const int barLength = 15;

            var users = votes.Select(vote =>
            {
                var option = options[vote.OptionIndex];
                return $"{vote.User.DisplayName} ({option})";
            });

            var builder = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle(voteContext.Subject)
                .WithAuthor("zahájil nové hlasování", Context.User.GetAvatarUrl())
                .WithDescription($"Celkový počet hlasů: {votes.Count}")
                .WithFooter($"Hlasovali:\n {string.Join(", ", users)}");

            for (int i = 0; i < options.Length; i++)
            {
                var votesBar = new StringBuilder();
                var votesCount = votes.Count(x => x.OptionIndex == i);
                var votesRatio = SafeDivision(votesCount, votesTotal);

                for (int j = 0; j < barLength; j++)
                {
                    var threshold = votesRatio * barLength;

                    votesBar.Append(i < threshold
                        ? new Emoji("\ud83d\udfe9")
                        : new Emoji("\u2b1b"));
                }

                builder.AddField($"{options[i]} ({votesRatio * 100:0.0}%)", $"`{votesBar}`");
            }

            return builder.Build();
        }

        private static float SafeDivision(float a, float b)
        {
            return a == 0 ? 0 : a / b;
        }
    }
}
