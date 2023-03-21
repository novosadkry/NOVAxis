using System;
using System.Reflection;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.TypeReaders;
using NOVAxis.Database.Guild;

using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;

using ICommandResult = Discord.Commands.IResult;
using IResult = Discord.Interactions.IResult;

namespace NOVAxis.Modules
{
    public class ModuleHandler
    {
        private DiscordShardedClient Client { get; }
        private ProgramConfig Config { get; }
        private ProgramLogger Logger { get; }
        private IServiceProvider Services { get; }
        private CommandService CommandService { get; }
        private InteractionService InteractionService { get; }

        public ModuleHandler(DiscordShardedClient client, 
            ProgramConfig config,
            ProgramLogger logger,
            IServiceProvider services,
            CommandService commandService,
            InteractionService interactionService)
        {
            Client = client;
            Config = config;
            Logger = logger;
            Services = services;
            CommandService = commandService;
            InteractionService = interactionService;

            Client.MessageReceived += MessageReceived;
            Client.InteractionCreated += InteractionCreated;

            CommandService.CommandExecuted += CommandExecuted;
            CommandService.Log += Logger.Log;

            InteractionService.InteractionExecuted += InteractionExecuted;
            InteractionService.Log += Logger.Log;
        }

        public async Task Setup()
        {
            CommandService.AddTypeReader<TimeSpan>(new TimeSpanTypeReader(), true);

            await using var scope = Services.CreateAsyncScope();
            await CommandService.AddModulesAsync(Assembly.GetEntryAssembly(), scope.ServiceProvider);
            await InteractionService.AddModulesAsync(Assembly.GetEntryAssembly(), scope.ServiceProvider);

            var config = Config.Interaction.Commands;

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

        private async Task MessageReceived(SocketMessage arg)
        {
            var message = (SocketUserMessage) arg;
            var context = new ShardedCommandContext(Client, message);

            if (context.Message == null) return;
            if (context.User.IsBot) return;

            string prefix;
            await using (var scope = Services.CreateAsyncScope())
            {
                var guildInfo = await scope.ServiceProvider
                    .GetService<GuildDbContext>()
                    .Get(context);

                prefix = guildInfo?.Prefix ?? Config.Interaction.DefaultPrefix;
            }

            int argPos = 0;
            if (!(context.Message.HasStringPrefix(prefix, ref argPos) ||
                context.Message.HasMentionPrefix(Client.CurrentUser, ref argPos)))
                return;

            switch (Client.Status)
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

            await CommandService.ExecuteAsync(context, argPos, Services);
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

                await Logger.Log(new LogMessage(
                    logWarning ? LogSeverity.Warning : LogSeverity.Verbose,
                    "Command",
                    $"User {context.User.Username}#{context.User.Discriminator} was unable to execute command '{context.Message.Content}' Reason: '{result.ErrorReason}'"));
            }
        }
    }
}
