using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Addons.Interactive;

using SharpLink;

namespace NOVAxis
{
    class Program
    {
        private static DiscordSocketClient client;
        private static CommandService commandService;
        private static IServiceProvider services;

        private static ProgramConfig config;

        public static string Version
        {
            get => Assembly.GetExecutingAssembly().GetName().Version.ToString().Substring(0, 5);
        }

        public static void Main(string[] args)
            => MainAsync().GetAwaiter().GetResult();

        private static async Task MainAsync()
        {
            config = await ProgramConfig.LoadConfig(Client_Log);

            await Client_Log(new LogMessage(LogSeverity.Info, "Program", "NOVAxis v" + Version));

            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = config.LogSeverity
            });

            commandService = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async,
                LogLevel = config.LogSeverity
            });

            Services.LavalinkService.Manager = new LavalinkManager(client, new LavalinkManagerConfig
            {
                RESTHost = "localhost",
                RESTPort = 2333,
                WebSocketHost = "localhost",
                WebSocketPort = 2333,
                Authorization = config.LavalinkLogin,
                TotalShards = 1
            });

            Services.LavalinkService.Manager.Log += Client_Log;

            services = new ServiceCollection()
                .AddSingleton(new Services.AudioModuleService(config))
                .AddSingleton(new InteractiveService((BaseSocketClient)client))
                .BuildServiceProvider();

            commandService.CommandExecuted += CommandService_CommandExecuted;
            commandService.AddTypeReader(typeof(TimeSpan), new TypeReaders.AudioModuleTypeReader());
            await commandService.AddModulesAsync(Assembly.GetEntryAssembly(), services);

            client.MessageReceived += Client_MessageReceived;
            client.Ready += Client_Ready;
            client.Log += Client_Log;

            if (config.StartLavalink)
            {
                await ProgramCommand.ProgramCommandList.First((x) => x.Name == "lavalink")
                    .Execute(new ProgramCommand.Context
                    {
                        Client_Log = Client_Log
                    });
            }

            try
            {
                await client.LoginAsync(TokenType.Bot, config.LoginToken);
                await client.StartAsync();
            }

            catch (Exception e)
            {
                await Client_Log(new LogMessage(LogSeverity.Error, "Program",
                    "The flow of execution has been halted due to an exception" +
                    $"\nReason: {e.Message}"));
                Console.Read();
                return;
            }

            await Task.Run(async () =>
            {
                while (client.LoginState == LoginState.LoggedIn)
                {
                    string input = Console.ReadLine();

                    for (int i = 0; i < ProgramCommand.ProgramCommandList.Count; i++)
                    {
                        ProgramCommand c = ProgramCommand.ProgramCommandList[i];

                        if (input == c.Name || c.Alias.Contains(input) && input != "")
                        {
                            await c.Execute(new ProgramCommand.Context
                            {
                                Client = client,
                                Config = config,
                                Client_Log = Client_Log
                            });

                            break;
                        }

                        else if (i + 1 == ProgramCommand.ProgramCommandList.Count)
                            await Client_Log(new LogMessage(LogSeverity.Info, "Program", "Invalid ProgramCommand"));
                    }
                }
            });

            Console.Read();
        }

        private static async Task Client_Log(LogMessage arg)
        {
            ProgramConfig config =
                Program.config ?? new ProgramConfig();

            if (arg.Severity > config.LogSeverity)
                return;

            await ProgramLog.ToConsole(arg);

            if (config.Log)
                await ProgramLog.ToFile(arg);
        }

        private async static Task Client_Ready()
        {
            await Services.LavalinkService.Manager.StartAsync();
            await client.SetGameAsync(config.Activity, type: config.ActivityType);
            await client.SetStatusAsync(config.UserStatus);
        }

        private async static Task Client_MessageReceived(SocketMessage arg)
        {
            SocketUserMessage message = (SocketUserMessage)arg;
            SocketCommandContext context = new SocketCommandContext(client, message);   

            if (context.Message == null) return;
            if (context.User.IsBot) return;

            int argPos = 0;
            if (!(context.Message.HasStringPrefix("~", ref argPos) || 
                context.Message.HasMentionPrefix(client.CurrentUser, ref argPos)))
                return;

            switch (client.Status)
            {
                case UserStatus.DoNotDisturb:
                    await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription($"(Bot je offline)")
                        .WithTitle($"Mé jádro se nyní nachází ve fázi opravy či restartu").Build());
                    return;

                case UserStatus.AFK:
                    await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(255, 150, 0)
                        .WithDescription($"(Bot je nedostupný)")
                        .WithTitle($"Mé jádro se nyní nachází ve fázi rapidního ochlazování").Build());
                    return;
            }

            await commandService.ExecuteAsync(context, argPos, services);
        }

        private async static Task CommandService_CommandExecuted(Optional<CommandInfo> info, ICommandContext context, IResult result)
        {
            if (!result.IsSuccess)
            {
                switch (result.Error)
                {
                    case CommandError.UnknownCommand:
                        await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription($"(Příkaz `{context.Message.Content.Split(' ')[0]}` neexistuje)")
                            .WithTitle($"Má verze jádra ještě není schopna téhle funkce").Build());
                        break;

                    case CommandError.BadArgCount:
                        await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription("(Neplatný počet argumentů)")
                            .WithTitle($"Pokud má být atom neutrální, musí být počet elektronů v elektronovém obalu roven počtu protonů v atomovém jádře").Build());
                        break;

                    case CommandError.ObjectNotFound:
                    case CommandError.ParseFailed:
                        await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription("(Neplatný argument)")
                            .WithTitle($"Má databáze nebyla schopna rozpoznat daný prvek").Build());
                        break;

                    case CommandError.UnmetPrecondition:
                        if (result.ErrorReason.StartsWith("Invalid context for command"))
                        {
                            await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                                .WithColor(220, 20, 60)
                                .WithDescription("(Přístup odepřen)")
                                .WithTitle($"Tento příkaz nelze vyvolat přímou zprávou").Build());
                        }

                        else
                        {
                            await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                                .WithColor(220, 20, 60)
                                .WithDescription("(Přístup odepřen)")
                                .WithTitle($"Pro operaci s tímto modulem nemáš dodatečnou kvalifikaci").Build());
                        }
                        break;

                    default:
                        await Client_Log(new LogMessage(
                            LogSeverity.Warning,
                            "Command",
                            $"User {context.User.Username}#{context.User.Discriminator} was unable to execute command '{context.Message.Content}' Reason: '{result.ErrorReason}'"));
                        break;
                }
            }
        }
    }
}
