using System.Linq;
using System.Text;

using NOVAxis.Utilities;
using NOVAxis.Services.Polls;

using Discord;

namespace NOVAxis.Modules.Polls
{
    public class VotePollEmbedBuilder : IPollEmbedBuilder
    {
        private VotePoll Poll { get; }
        private PollEmbedBuilder PollBuilder { get; }

        public VotePollEmbedBuilder(VotePoll poll)
        {
            Poll = poll;
            PollBuilder = new PollEmbedBuilder(poll);
        }

        public Embed BuildEmbed()
        {
            var votes = Poll.Votes;

            return PollBuilder.GetEmbedBuilder()
                .WithDescription($"Potřebný počet hlasů: {votes.Count}/")
                .Build();
        }

        public MessageComponent BuildComponents()
        {
            return PollBuilder.GetComponentBuilder().Build();
        }
    }

    public class PollEmbedBuilder : IPollEmbedBuilder
    {
        public static ComponentBuilder ComponentsClosed =>
            new ComponentBuilder()
                .WithButton("Hlasování uzavřeno", "poll_closed", ButtonStyle.Secondary, disabled: true);

        public static ComponentBuilder ComponentsExpired =>
            new ComponentBuilder()
                .WithButton("Hlasování skončilo", "poll_expired", ButtonStyle.Secondary, disabled: true);

        private Poll Poll { get; }

        public PollEmbedBuilder(Poll poll)
        {
            Poll = poll;
        }

        public Embed BuildEmbed()
        {
            return GetEmbedBuilder().Build();
        }

        public MessageComponent BuildComponents()
        {
            return GetComponentBuilder().Build();
        }

        public EmbedBuilder GetEmbedBuilder()
        {
            var owner = Poll.Owner;
            var options = Poll.Options;
            var votes = Poll.Votes;

            const int barLength = 15;

            var users = votes
                .Select(vote =>
                {
                    var option = options[vote.Value];
                    return $"{vote.Key.DisplayName} ({option})";
                }).ToList();

            var builder = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle(Poll.Subject)
                .WithAuthor("zahájil nové hlasování", owner.GetAvatarUrl())
                .WithDescription($"Celkový počet hlasů: {votes.Count}");

            if (users.Count > 0)
                builder.WithFooter($"Hlasovali:\n {string.Join(", ", users)}");

            for (int i = 0; i < options.Length; i++)
            {
                var votesBar = new StringBuilder();
                var votesCount = votes.Count(x => x.Value == i);
                var votesRatio = Math.SafeDivision(votesCount, votes.Count);

                for (int j = 0; j < barLength; j++)
                {
                    var threshold = votesRatio * barLength;

                    votesBar.Append(j < threshold
                        ? new Emoji("\ud83d\udfe9")
                        : new Emoji("\u2b1b"));
                }

                builder.AddField($"{options[i]} ({votesRatio * 100:0.0}%)", $"`{votesBar}`");
            }

            return builder;
        }

        public ComponentBuilder GetComponentBuilder()
        {
            switch (Poll.State)
            {
                case PollState.Closed:
                    return ComponentsClosed;
                case PollState.Expired:
                    return ComponentsExpired;
            }

            var id = Poll.Id;
            var options = Poll.Options;
            var builder = new ComponentBuilder();

            for (int i = 0; i < options.Length; i++)
            {
                if (!string.IsNullOrEmpty(options[i]))
                    builder.WithButton(options[i], $"poll_vote_{id},{i}", ButtonStyle.Success);
            }

            builder.WithButton("Uzavřít hlasování", $"poll_close_{id}", ButtonStyle.Danger);

            return builder;
        }
    }
}
