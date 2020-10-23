﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
        private static CommandService _commandService;
        private static IServiceProvider _services;

        public static DiscordShardedClient Client { get; private set; }
        public static ProgramConfig Config { get; private set; }

        public static short ShardsReady { get; private set; }
        public static ulong OwnerId => 269182357704015873L;

        public static string Version 
            => Assembly.GetExecutingAssembly().GetName().Version.ToString().Substring(0, 5);

        public static void Main(string[] args)
            => MainAsync().GetAwaiter().GetResult();

        private static async Task MainAsync()
        {
            ProgramConfig.LogEvent += Client_Log;
            Config = await ProgramConfig.LoadConfig();

            await Client_Log(new LogMessage(LogSeverity.Info, "Program", "NOVAxis v" + Version));

            Client = new DiscordShardedClient(new DiscordSocketConfig
            {
                LogLevel = Config.Log.Severity,
                TotalShards = Config.TotalShards,
                ExclusiveBulkDelete = true
            });

            _commandService = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async,
                LogLevel = Config.Log.Severity
            });

            Services.LavalinkService.Manager = new LavalinkManager(Client, new LavalinkManagerConfig
            {
                RESTHost = Config.Lavalink.Host,
                RESTPort = 2333,
                WebSocketHost = Config.Lavalink.Host,
                WebSocketPort = 2333,
                Authorization = Config.Lavalink.Login,
                TotalShards = Config.TotalShards
            }); 

            Services.LavalinkService.Manager.Log += Client_Log;
            Services.DatabaseService.LogEvent += Client_Log;

            _services = new ServiceCollection()
                .AddSingleton(new Services.AudioModuleService())
                .AddSingleton(new Services.DatabaseService())
                .AddSingleton(new Services.GuildService())
                .AddSingleton(new InteractiveService((BaseSocketClient)Client))
                .BuildServiceProvider();

            _commandService.CommandExecuted += CommandService_CommandExecuted;
            _commandService.AddTypeReader(typeof(TimeSpan), new TypeReaders.AudioModuleTypeReader());
            await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            
            Client.MessageReceived += Client_MessageReceived;
            Client.ShardReady += Client_Ready;
            Client.Log += Client_Log;

            if (Config.Lavalink.Start)
            {
                await ProgramCommand.ProgramCommandList
                    .First((x) => x.Name == "lavalink")
                    .Execute();
            }

            try
            {
                await Client.LoginAsync(TokenType.Bot, Config.LoginToken);
                await Client.StartAsync();

                await Client.SetGameAsync(Config.Activity.Online, type: Config.Activity.ActivityType);
                await Client.SetStatusAsync(Config.Activity.UserStatus);
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
                while (Client.LoginState == LoginState.LoggedIn)
                {
                    string input = Console.ReadLine();

                    for (int i = 0; i < ProgramCommand.ProgramCommandList.Count; i++)
                    {
                        ProgramCommand c = ProgramCommand.ProgramCommandList[i];

                        if (input == c.Name || c.Alias.Contains(input) && input != "")
                        {
                            await c.Execute();                     
                            break;
                        }

                        else if (i + 1 == ProgramCommand.ProgramCommandList.Count)
                            await Client_Log(new LogMessage(LogSeverity.Info, "Program", "Invalid ProgramCommand"));
                    }
                }
            });

            Console.Read();
        }

        public static async Task Client_Log(LogMessage arg)
        {
            ProgramConfig config =
                Config ?? new ProgramConfig();

            if (arg.Severity > config.Log.Severity)
                return;

            await ProgramLog.ToConsole(arg);

            if (config.Log.Active)
                await ProgramLog.ToFile(arg);
        }

        private static async Task Client_Ready(DiscordSocketClient shard)
        {
            await Client_Log(new LogMessage(LogSeverity.Info, "Shard #" + shard.ShardId, "Ready"));
            
            if (++ShardsReady == Config.TotalShards)
            {
                await Client_Log(new LogMessage(LogSeverity.Info, "Lavalink", "Start"));
                await Services.LavalinkService.Manager.StartAsync();
            }
        }

        private static async Task Client_MessageReceived(SocketMessage arg)
        {
            SocketUserMessage message = (SocketUserMessage)arg;
            ShardedCommandContext context = new ShardedCommandContext(Client, message);   

            if (context.Message == null) return;
            if (context.User.IsBot) return;

            var guildService = _services.GetService<Services.GuildService>();
            var guildInfo = await guildService.GetInfo(context);

            string prefix = guildInfo.Prefix;

            int argPos = 0;
            if (!(context.Message.HasStringPrefix(prefix, ref argPos) || 
                context.Message.HasMentionPrefix(Client.CurrentUser, ref argPos)))
            {
                if (context.User is IGuildUser guildUser)
                {
                    if (guildUser.RoleIds.Contains(guildInfo.MuteRole))
                        _ = arg.DeleteAsync();
                }

                return;
            }

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

            await _commandService.ExecuteAsync(context, argPos, _services);
        }

        private static async Task CommandService_CommandExecuted(Optional<CommandInfo> info, ICommandContext context, IResult result)
        {
            if (!result.IsSuccess)
            {
                switch (result.Error)
                {
                    case CommandError.UnknownCommand:
                        await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription($"(Příkaz `{context.Message.Content.Split(' ')[0]}` neexistuje)")
                            .WithTitle("Má verze jádra ještě není schopna téhle funkce").Build());
                        break;

                    case CommandError.BadArgCount:
                        await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription("(Neplatný počet argumentů)")
                            .WithTitle("Počet elektronů v elektronovém obale není roven počtu protonů v atomovém jádře").Build());
                        break;

                    case CommandError.ObjectNotFound:
                    case CommandError.ParseFailed:
                        await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription("(Neplatný argument)")
                            .WithTitle("Má databáze nebyla schopna rozpoznat daný prvek").Build());
                        break;

                    case CommandError.UnmetPrecondition:
                        if (result.ErrorReason.StartsWith("Invalid context for command"))
                            await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                                .WithColor(220, 20, 60)
                                .WithDescription("(Přístup odepřen)")
                                .WithTitle("Tento příkaz nelze vyvolat přímou zprávou").Build());

                        else
                            await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                                .WithColor(220, 20, 60)
                                .WithDescription("(Přístup odepřen)")
                                .WithTitle("Pro operaci s tímto modulem nemáš dodatečnou kvalifikaci").Build());
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
