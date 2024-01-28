using System;
using System.Threading.Tasks;

using NOVAxis.Services.Polls;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Modules.Polls
{
    public class QuestionPollBuilder
    {
        private IGuildUser Owner { get; set; }
        private string Question { get; set; }
        private string[] Options { get; set; }

        public QuestionPollBuilder WithOwner(IGuildUser owner)
        {
            Owner = owner;
            return this;
        }

        public QuestionPollBuilder WithQuestion(string question)
        {
            Question = question;
            return this;
        }

        public QuestionPollBuilder WithOptions(string[] options)
        {
            Options = options;
            return this;
        }

        public Poll Build()
        {
            var poll = new Poll(Owner, Question, Options);

            poll.OnClosed += async (_, _) =>
            {
                await poll.InteractionMessage.ModifyAsync(message =>
                {
                    message.Components = PollEmbedBuilder.ComponentsClosed;
                });
            };

            poll.OnExpired += async (_, _) =>
            {
                await poll.InteractionMessage.ModifyAsync(message =>
                {
                    message.Components = PollEmbedBuilder.ComponentsExpired;
                });
            };

            return poll;
        }
    }

    public class QuestionPollModal : IModal
    {
        public string Title => "Nové hlasování";

        [RequiredInput]
        [InputLabel("Zadejte otázku pro nové hlasování")]
        [ModalTextInput("question", maxLength: 100)]
        public string Question { get; set; }

        [RequiredInput]
        [InputLabel("Možnosti (každá na novém řádku)")]
        [ModalTextInput("options", maxLength: 200, initValue: "Ano\nNe", style: TextInputStyle.Paragraph)]
        public string Options { get; set; }

        public string[] GetOptionsArray()
        {
            return Options.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public class QuestionPollTracker : IPollTracker
    {
        public Poll Poll { get; }
        public TimeSpan Duration { get; }

        public QuestionPollTracker(Poll poll, TimeSpan duration)
        {
            Poll = poll;
            Duration = duration;
        }

        public ValueTask<bool> ShouldClose()
        {
            var result = Poll.State == PollState.Opened &&
                         Poll.StartTime + Duration > DateTime.Now;

            return new ValueTask<bool>(result);
        }

        public async ValueTask<bool> ShouldExpire()
        {
            return Poll.State == PollState.Closed || await ShouldClose();
        }
    }
}
