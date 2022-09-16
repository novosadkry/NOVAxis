using System;
using System.Reflection;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.TypeReaders;
using NOVAxis.Services.Guild;

using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;

using ICommandResult = Discord.Commands.IResult;

namespace NOVAxis.Modules
{
    public class ModuleHandler
    {
        private readonly DiscordShardedClient _client;
        private readonly IServiceProvider _services;

        public CommandService CommandService { get; }
        public InteractionService InteractionService { get; }

        public event Func<LogMessage, Task> LogEvent;

        public ModuleHandler(
            DiscordShardedClient client, 
            IServiceProvider services, 
            CommandService commandService, 
            InteractionService interactionService)
        {
            CommandService = commandService;
            InteractionService = interactionService;

            _services = services;
            _client = client;

            _client.MessageReceived += MessageReceived;
            CommandService.CommandExecuted += CommandExecuted;

            _client.InteractionCreated += InteractionCreated;
        }

        ~ModuleHandler()
        {
            _client.MessageReceived -= MessageReceived;
            CommandService.CommandExecuted -= CommandExecuted;
        }

        public async Task Setup()
        {
            CommandService.AddTypeReader<TimeSpan>(new TimeSpanTypeReader());

            await CommandService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            await InteractionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

            var config = Program.Config.Interaction.Commands;

            if (config.RegisterGlobally)
                await InteractionService.RegisterCommandsGloballyAsync();

            else if (config.RegisterToGuild != 0)
                await InteractionService.RegisterCommandsToGuildAsync(config.RegisterToGuild);
        }

        private async Task InteractionCreated(SocketInteraction arg)
        {
            var ctx = new ShardedInteractionContext(_client, arg);
            await InteractionService.ExecuteCommandAsync(ctx, _services);
        }

        private async Task MessageReceived(SocketMessage arg)
        {
            var message = (SocketUserMessage)arg;
            var context = new ShardedCommandContext(_client, message);

            if (context.Message == null) return;
            if (context.User.IsBot) return;

            var guildService = _services.GetService<GuildService>();
            var guildInfo = await guildService.GetInfo(context);

            string prefix = guildInfo.Prefix;

            int argPos = 0;
            if (!(context.Message.HasStringPrefix(prefix, ref argPos) ||
                context.Message.HasMentionPrefix(_client.CurrentUser, ref argPos)))
                return;

            switch (_client.Status)
            {
                case UserStatus.DoNotDisturb:
                    await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Bot je offline)")
                        .WithTitle("Mé jádro se nyní nachází ve fázi opravy či restartu").Build());
                    return;

                case UserStatus.AFK:
                    await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription("(Bot je nedostupný)")
                        .WithTitle("Mé jádro se nyní nachází ve fázi rapidního ochlazování").Build());
                    return;
            }

            await CommandService.ExecuteAsync(context, argPos, _services);
        }

        private async Task CommandExecuted(Optional<CommandInfo> info, ICommandContext context, ICommandResult result)
        {
            if (!result.IsSuccess)
            {
                string title = string.Empty, description = string.Empty;
                bool sendMessage = true, logWarning = false;

                switch (result.Error)
                {
                    case CommandError.UnknownCommand:
                        title = "Má verze jádra ještě není schopna téhle funkce";
                        description = $"(Příkaz `{context.Message.Content.Split(' ')[0]}` neexistuje)";
                        break;

                    case CommandError.BadArgCount:
                        title = "Počet elektronů v elektronovém obale není roven počtu protonů v atomovém jádře";
                        description = "(Neplatný počet argumentů)";
                        break;

                    case CommandError.ObjectNotFound:
                    case CommandError.ParseFailed:
                        title = "Má databáze nebyla schopna rozpoznat daný prvek";
                        description = "(Neplatný argument)";
                        break;

                    case CommandError.UnmetPrecondition:
                        switch (result.ErrorReason)
                        {
                            case "Invalid context for command":
                                title = "Tento příkaz nelze vyvolat přímou zprávou";
                                description = "(Přístup odepřen)";
                                break;

                            case "User has command on cooldown":
                                title = "Mé jádro nyní ochlazuje vybraný modul";
                                description = "(Příkaz je časově omezen)";
                                break;

                            case "User has command on cooldown (no warning)":
                                sendMessage = false;
                                break;

                            default:
                                title = "Pro operaci s tímto modulem nemáš dodatečnou kvalifikaci";
                                description = "(Přístup odepřen)";
                                break;
                        }
                        break;

                    default:
                        logWarning = true; sendMessage = false;
                        break;
                }

                if (sendMessage && !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(description))
                {
                    await Task.Run(() =>
                    {
                        context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription(description)
                            .WithTitle(title).Build());
                    });
                }

                LogEvent?.Invoke(new LogMessage(
                    logWarning ? LogSeverity.Warning : LogSeverity.Verbose,
                    "Command",
                    $"User {context.User.Username}#{context.User.Discriminator} was unable to execute command '{context.Message.Content}' Reason: '{result.ErrorReason}'"));
            }
        }
    }
}
