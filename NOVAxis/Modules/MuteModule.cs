using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using NOVAxis.Services;

namespace NOVAxis.Modules
{
    [Group("mute")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.MuteMembers)]
    public class MuteModule : ModuleBase<SocketCommandContext>
    {
        public GuildService GuildService { get; set; }

        [Command, Summary("Swithes a user's permission to chat in text channels")]
        public async Task SwitchMute(IGuildUser user)
        {
            var guildInfo = await GuildService.GetInfo(Context);
            IRole role = Context.Guild.GetRole(guildInfo.MuteRole);

            if (role == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Role neexistuje nebo nebyla nastavena)")
                    .WithTitle("Mé jádro nebylo pro tenhle příkaz nakonfigurováno").Build());

                return;
            }

            if (!user.RoleIds.Contains(role.Id))
            {
                await user.AddRoleAsync(role);

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithDescription($"(Přidělena role {role.Mention})")
                    .WithTitle($"Uživatel **{user.Username}** byl odpojen od textového protokolu").Build());
            }

            else
            {
                await user.RemoveRoleAsync(role);

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithDescription($"(Odebrána role {role.Mention})")
                    .WithTitle($"Uživatel **{user.Username}** byl připojen k textovému protokolu").Build());
            }
        }

        [Command("setrole"), Summary("Sets the guild's mute role which is used to identify muted users")]
        public async Task SetMuteRole(IRole role)
        {
            var guildInfo = await GuildService.GetInfo(Context);
            guildInfo.MuteRole = role.Id;

            await GuildService.SetInfo(Context, guildInfo);

            await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithDescription($"(Nastavena role {role.Mention})")
                .WithTitle("Konfigurace mého jádra proběhla úspešně").Build());
        }
    }
}
