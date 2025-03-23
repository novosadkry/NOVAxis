using System;

using NOVAxis.Services.Polls;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Modules.Polls
{
    public class PollModal : IModal
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

    public class VotePoll : PollBase
    {
        public VotePoll(IGuildUser owner, string subject)
            : base(owner, subject, ["Ano", "Ne"]) { }
    }
}
