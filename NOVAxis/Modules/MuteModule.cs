using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis.Modules
{
    [Group("mute")]
    [RequireUserPermission(GuildPermission.MuteMembers)]
    public class MuteModule : ModuleBase<SocketCommandContext>
    {
        [Command, Summary("Swithes a user's permission to chat in text channels")]
        public async Task SwitchMute(IGuildUser user)
        {
            IRole role = await SetupMute();

            if (!user.RoleIds.Contains(role.Id))
            {
                await user.AddRoleAsync(role);

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithDescription($"(Přidělena role {role.Mention})")
                    .WithTitle($"Uživatel **{user.Username}** byl odpojen od textového protokolu").Build());
            }

            else
            {
                await user.RemoveRoleAsync(role);

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithDescription($"(Odebrána role {role.Mention})")
                    .WithTitle($"Uživatel **{user.Username}** byl připojen k textovému protokolu").Build());
            }
        }

        private async Task<IRole> SetupMute()
        {
            IRole role = Context.Guild.Roles.FirstOrDefault((x) => x.Name == "Muted");

            if (role == null)
            {
                role = await Context.Guild.CreateRoleAsync("Muted", new GuildPermissions(), new Color(0x818386));

                foreach (ITextChannel channel in Context.Guild.TextChannels)
                {
                    await channel.AddPermissionOverwriteAsync(role, new OverwritePermissions(sendMessages: PermValue.Deny));
                }

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithDescription($"(Vytvoření role {role.Mention})")
                    .WithTitle($"Mé jádro úspěšně nakonfigurovalo textový protokol").Build());
            }

            return role;
        }
    }
}
