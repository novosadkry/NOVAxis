using System;
using System.Reflection;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Extensions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace NOVAxis.Modules
{
    public class ModuleHandler
    {
        private DiscordShardedClient Client { get; }
        private IOptions<InteractionOptions> Options { get; }
        private ILogger<ModuleHandler> Logger { get; }
        private IServiceProvider Services { get; }
        private InteractionService InteractionService { get; }

        public ModuleHandler(DiscordShardedClient client, 
            IOptions<InteractionOptions> options,
            ILogger<ModuleHandler> logger,
            IServiceProvider services,
            InteractionService interactionService)
        {
            Client = client;
            Options = options;
            Logger = logger;
            Services = services;
            InteractionService = interactionService;

            Client.InteractionCreated += InteractionCreated;
            InteractionService.InteractionExecuted += InteractionExecuted;
            InteractionService.Log += logger.Log;
        }

        public async Task Setup()
        {
            await using var scope = Services.CreateAsyncScope();
            await InteractionService.AddModulesAsync(Assembly.GetEntryAssembly(), scope.ServiceProvider);

            var config = Options.Value.Commands;

            if (config.RegisterGlobally)
                await InteractionService.RegisterCommandsGloballyAsync();

            else if (config.RegisterToGuild != 0)
                await InteractionService.RegisterCommandsToGuildAsync(config.RegisterToGuild);
        }

        private async Task InteractionCreated(SocketInteraction arg)
        {
            var ctx = new ShardedInteractionContext(Client, arg);
            await InteractionService.ExecuteCommandAsync(ctx, Services);
        }

        private async Task InteractionExecuted(ICommandInfo info, IInteractionContext context, IResult result)
        {
            var slashCommandData = context.Interaction.Data as SocketSlashCommandData;

            if (!result.IsSuccess)
            {
                string title = string.Empty, description = string.Empty;
                bool sendMessage = true, logWarning = false;

                switch (result.Error)
                {
                    case InteractionCommandError.UnknownCommand:
                        title = "Má verze jádra ještě není schopna téhle funkce";
                        description = $"(Příkaz `{slashCommandData?.Name}` neexistuje)";
                        break;

                    case InteractionCommandError.BadArgs:
                    case InteractionCommandError.ParseFailed:
                    case InteractionCommandError.ConvertFailed:
                        title = "Má databáze nebyla schopna rozpoznat daný prvek";
                        description = "(Neplatný argument)";
                        break;

                    case InteractionCommandError.UnmetPrecondition:
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
                        context.Interaction.RespondAsync(
                            ephemeral: true, 
                            embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription(description)
                            .WithTitle(title).Build());
                    });
                }

                await Logger.Log(new LogMessage(
                    logWarning ? LogSeverity.Warning : LogSeverity.Verbose,
                    "Command",
                    $"User {context.User.Username}#{context.User.Discriminator} was unable to execute command '{slashCommandData?.Name}' Reason: '{result.ErrorReason}'"));
            }
        }
    }
}
