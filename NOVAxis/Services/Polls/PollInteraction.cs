using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Services.Polls
{
    public class PollInteraction
    {
        public PollBase Poll { get; set; }
        public IUserMessage Message { get; set; }
        public IPollTracker Tracker { get; set; }
        public IPollEmbedBuilder Builder { get; set; }

        public async Task Refresh()
        {
            if (await Tracker.ShouldExpire())
                await Expire(rebuild: false);

            if (await Tracker.ShouldClose())
                await Close(rebuild: false);

            await Rebuild();
        }

        public async Task Rebuild()
        {
            await Message.ModifyAsync(props =>
            {
                props.Embed = Builder.BuildEmbed();
                props.Components = Builder.BuildComponents();
            });
        }

        public async Task Close(bool rebuild = true)
        {
            Poll.Close();
            if (rebuild) await Rebuild();
        }

        public async Task Expire(bool rebuild = true)
        {
            Poll.Expire();
            if (rebuild) await Rebuild();
        }
    }
}
