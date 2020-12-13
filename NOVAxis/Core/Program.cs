using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Services.Audio;
using NOVAxis.Services.Guild;
using NOVAxis.Services.Database;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

using Interactivity;
using Victoria;

namespace NOVAxis.Core
{
    public class Program
    {  
        private static CommandService _commandService;
        public static IServiceProvider Services;

        public static DiscordShardedClient Client { get; private set; }
        public static ProgramConfig Config { get; private set; }

        public static short ShardsReady { get; private set; }
        public static ulong OwnerId => 269182357704015873L;

        public static string Version 
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString().Substring(0, 5);

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
                ExclusiveBulkDelete = true,
                MessageCacheSize = 100
            });

            _commandService = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async,
                LogLevel = Config.Log.Severity
            });

            LavaConfig lavaConfig = new LavaConfig
            {
                Hostname = Config.Lavalink.Host,
                Port = Config.Lavalink.Port,
                Authorization = Config.Lavalink.Login,
                SelfDeaf = Config.Lavalink.SelfDeaf,
                LogSeverity = Config.Log.Severity
            };

            LavaNode lavaNode = new LavaNode(Client, lavaConfig);
            lavaNode.OnLog += Client_Log;

            DatabaseService databaseService = DatabaseService.GetService(Config.Database);
            databaseService.LogEvent += Client_Log;
            if (Config.Database.Active) await databaseService.Setup();

            GuildService guildService = new GuildService(databaseService);
            if (Config.Database.Active) await guildService.LoadFromDatabase();

            Services = new ServiceCollection()
                .AddSingleton(lavaNode)
                .AddSingleton(databaseService)
                .AddSingleton(guildService)
                .AddSingleton(new InteractivityService(Client))
                .AddSingleton(new AudioModuleService(lavaNode))
                .BuildServiceProvider();

            _commandService.CommandExecuted += CommandService_CommandExecuted;
            _commandService.AddTypeReader(typeof(TimeSpan), new TypeReaders.AudioModuleTypeReader());
            await _commandService.AddModulesAsync(Assembly.GetEntryAssembly(), Services);
            
            Client.MessageReceived += Client_MessageReceived;
            Client.ShardReady += Client_Ready;
            Client.Log += Client_Log;

            if (Config.Lavalink.Start)
            {
                await ProgramCommand.ProgramCommandList
                    .First(x => x.Name == "lavalink")
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

                        if (input == c.Name || c.Alias.Contains(input) && !string.IsNullOrWhiteSpace(input))
                        {
                            await c.Execute();
                            break;
                        }

                        if (i + 1 == ProgramCommand.ProgramCommandList.Count)
                            await Client_Log(new LogMessage(LogSeverity.Info, "Program", "Invalid ProgramCommand"));
                    }
                }
            });
        }

        public static async Task Exit()
        {
            var lavaNodeInstance = Services.GetService<LavaNode>();

            if (lavaNodeInstance.IsConnected)
                await lavaNodeInstance.DisposeAsync();

            await Client.LogoutAsync();
            await Client.StopAsync();
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
            var lavaNodeInstance = Services.GetService<LavaNode>();

            if (++ShardsReady == Config.TotalShards)
            {
                await Client_Log(new LogMessage(LogSeverity.Info, "Victoria", "Connecting"));
                _ = lavaNodeInstance.ConnectAsync();
            }
        }

        private static async Task Client_MessageReceived(SocketMessage arg)
        {
            SocketUserMessage message = (SocketUserMessage)arg;
            ShardedCommandContext context = new ShardedCommandContext(Client, message);

            if (context.Message == null) return;
            if (context.User.IsBot) return;

            var guildService = Services.GetService<GuildService>();
            var guildInfo = await guildService.GetInfo(context);

            string prefix = guildInfo.Prefix;

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

            await _commandService.ExecuteAsync(context, argPos, Services);
        }

        private static async Task CommandService_CommandExecuted(Optional<CommandInfo> info, ICommandContext context, IResult result)
        {
            if (!result.IsSuccess)
            {
                bool logWarning = false;

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
                        switch (result.ErrorReason)
                        {
                            case "Invalid context for command":
                                await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                                    .WithColor(220, 20, 60)
                                    .WithDescription("(Přístup odepřen)")
                                    .WithTitle("Tento příkaz nelze vyvolat přímou zprávou").Build());
                                break;
                            case "User has command on cooldown":
                                await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                                    .WithColor(220, 20, 60)
                                    .WithDescription("(Příkaz je časově omezen)")
                                    .WithTitle("Mé jádro nyní ochlazuje vybraný modul").Build());
                                break;
                            default:
                                await context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                                    .WithColor(220, 20, 60)
                                    .WithDescription("(Přístup odepřen)")
                                    .WithTitle("Pro operaci s tímto modulem nemáš dodatečnou kvalifikaci").Build());
                                break;
                        }
                        break;

                    default:
                        logWarning = true;
                        break;
                }

                await Client_Log(new LogMessage(
                    logWarning ? LogSeverity.Warning : LogSeverity.Verbose,
                    "Command",
                    $"User {context.User.Username}#{context.User.Discriminator} was unable to execute command '{context.Message.Content}' Reason: '{result.ErrorReason}'"));
            }
        }
    }
}
