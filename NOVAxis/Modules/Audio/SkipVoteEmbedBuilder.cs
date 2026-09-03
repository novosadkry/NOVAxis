using System.Linq;
using System.Text;

using NOVAxis.Services.Polls;

using Discord;

namespace NOVAxis.Modules.Audio
{
    /// <summary>
    /// The skip vote as a message. Shows the count against the threshold rather than a
    /// share of those who happened to answer - what matters is how far off it still is.
    /// </summary>
    public class SkipVoteEmbedBuilder : IPollEmbedBuilder
    {
        private const int BarLength = 12;

        private SkipVote Vote { get; }

        public SkipVoteEmbedBuilder(SkipVote vote)
        {
            Vote = vote;
        }

        public Embed BuildEmbed()
        {
            var favour = Vote.InFavour;

            var builder = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle(Vote.Subject)
                .WithAuthor("vyvolal hlasování o přeskočení", Vote.Owner.GetAvatarUrl())
                .WithDescription(Describe());

            var bar = new StringBuilder();

            for (var i = 0; i < BarLength; i++)
            {
                var filled = Vote.Needed > 0 && i < (double)favour / Vote.Needed * BarLength;
                bar.Append(filled ? new Emoji("🟩") : new Emoji("⬛"));
            }

            builder.AddField($"Pro přeskočení: {favour}/{Vote.Needed}", $"`{bar}`");

            var voters = Vote.Votes
                .Where(x => x.Value == SkipVote.Yes)
                .Select(x => x.Key.DisplayName)
                .ToList();

            if (voters.Count > 0)
                builder.WithFooter($"Pro: {string.Join(", ", voters)}");

            return builder.Build();
        }

        private string Describe()
        {
            if (Vote.Passed)
                return "Hlasování prošlo, skladba se přeskakuje.";

            if (Vote.Rejected)
                return "Skladba hraje dál — pro přeskočení už se potřebný počet hlasů nesejde.";

            if (Vote.State != PollState.Opened)
                return "Hlasování skončilo bez rozhodnutí, skladba hraje dál.";

            var missing = Vote.Needed - Vote.InFavour;

            return $"Posluchačů v kanálu: {Vote.Listeners}. Chybí ještě {missing} " +
                   (missing == 1 ? "hlas." : missing < 5 ? "hlasy." : "hlasů.");
        }

        public MessageComponent BuildComponents()
        {
            if (Vote.State != PollState.Opened)
            {
                return new ComponentBuilder()
                    .WithButton(Vote.Passed ? "Přeskočeno" : "Hlasování skončilo",
                        "skipvote_done", ButtonStyle.Secondary, disabled: true)
                    .Build();
            }

            return new ComponentBuilder()
                .WithButton("Přeskočit", $"skipvote_yes_{Vote.Id}", ButtonStyle.Success)
                .WithButton("Nechat hrát", $"skipvote_no_{Vote.Id}", ButtonStyle.Secondary)
                .Build();
        }
    }
}
