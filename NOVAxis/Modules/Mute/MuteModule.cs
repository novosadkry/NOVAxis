using System.Linq;
using System.Threading.Tasks;

using NOVAxis.Preconditions;

using Discord;
using Discord.Commands;

namespace NOVAxis.Modules.Mute
{
    [Cooldown(1)]
    [Group("mute")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.MuteMembers)]
    [RequireBotPermission(GuildPermission.ManageChannels | GuildPermission.MuteMembers | GuildPermission.ManageRoles)]
    public class MuteModule : ModuleBase<ShardedCommandContext>
    {
        public const string RoleName = "Muted";

        [Command, Summary("Swithes a user's permission to chat in text channels")]
        public async Task SwitchMute(IGuildUser user)
        {
            IRole role = await SetupMute();

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

        private async Task<IRole> SetupMute()
        {
            IRole role = Context.Guild.Roles.FirstOrDefault(r => r.Name == RoleName);

            if (role == null)
            {
                role = await Context.Guild.CreateRoleAsync(RoleName, new GuildPermissions(), new Color(0x818386), false, false);
                
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithDescription($"(Vytvoření role {role.Mention})")
                    .WithTitle("Mé jádro úspěšně nakonfigurovalo textový protokol").Build());
            }

            foreach (ITextChannel channel in Context.Guild.TextChannels)
                await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(sendMessages: PermValue.Deny));

            return role;
        }
    }
}
