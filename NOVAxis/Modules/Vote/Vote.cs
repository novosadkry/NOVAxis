using System;
using System.Collections.Generic;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Modules.Vote
{
    public readonly record struct Vote(IGuildUser User, int OptionIndex);

    public class VoteContext
    {
        public VoteContext(IGuildUser owner, StartVoteModal modal)
        {
            Votes = [];
            Owner = owner;
            Subject = modal.Subject;
            Options = modal.Options.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        public string Subject { get; }
        public IGuildUser Owner { get; }
        public string[] Options { get; }
        public List<Vote> Votes { get; }
    }

    public class StartVoteModal : IModal
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
    }
}
