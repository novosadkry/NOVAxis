using System.Threading.Tasks;

using Discord.Interactions;

using NOVAxis.Preconditions;

namespace NOVAxis.Modules.Video
{
    [RequireContext(ContextType.Guild)]
    public class VideoModule : InteractionModuleBase<ShardedInteractionContext>
    {
        [Cooldown(30)]
        [SlashCommand("download", "Downloads a given video")]
        public async Task Download(string url)
        {

        }
    }
}
