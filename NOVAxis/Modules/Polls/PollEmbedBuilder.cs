using System.Linq;
using System.Text;

using NOVAxis.Services.Polls;

using Discord;

namespace NOVAxis.Modules.Polls
{
    public class PollEmbedBuilder
    {
        private Poll _poll;

        public PollEmbedBuilder(Poll poll)
        {
            _poll = poll;
        }

        public static MessageComponent ComponentsClosed =>
            new ComponentBuilder()
                .WithButton("Hlasování uzavřeno", "poll_closed", ButtonStyle.Secondary, disabled: true)
                .Build();

        public static MessageComponent ComponentsEnded =>
            new ComponentBuilder()
                .WithButton("Hlasování skončilo", "poll_ended", ButtonStyle.Secondary, disabled: true)
                .Build();

        public MessageComponent BuildComponents()
        {
            var id = _poll.Id;
            var options = _poll.Options;
            var builder = new ComponentBuilder();

            for (int i = 0; i < options.Length; i++)
            {
                if (!string.IsNullOrEmpty(options[i]))
                    builder.WithButton(options[i], $"poll_vote_{id},{i}", ButtonStyle.Success);
            }

            builder.WithButton("Ukončit hlasování", $"poll_end_{id}", ButtonStyle.Danger);

            return builder.Build();
        }

        public Embed BuildEmbed()
        {
            var owner = _poll.Owner;
            var options = _poll.Options;
            var votes = _poll.Votes;

            const int barLength = 15;

            var users = votes
                .Select(vote =>
                {
                    var option = options[vote.Value];
                    return $"{vote.Key.DisplayName} ({option})";
                }).ToList();

            var builder = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle(_poll.Subject)
                .WithAuthor("zahájil nové hlasování", owner.GetAvatarUrl())
                .WithDescription($"Celkový počet hlasů: {votes.Count}");

            if (users.Count > 0)
                builder.WithFooter($"Hlasovali:\n {string.Join(", ", users)}");

            for (int i = 0; i < options.Length; i++)
            {
                var votesBar = new StringBuilder();
                var votesCount = votes.Count(x => x.Value == i);
                var votesRatio = SafeDivision(votesCount, votes.Count);

                for (int j = 0; j < barLength; j++)
                {
                    var threshold = votesRatio * barLength;

                    votesBar.Append(j < threshold
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
