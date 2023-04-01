using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace NOVAxis.Core
{
    public class ProgramCommand
    {
        public string Name { get; }
        public string[] Alias { get; }
        private Func<IServiceProvider, Task> Command { get; }

        public Task Execute(IServiceProvider services)
        {
            return Task.Run(() => Command(services));
        }

        public ProgramCommand(string name, string[] alias, Func<IServiceProvider, Task> command)
        {
            Name = name;
            Alias = alias ?? new[] { "" };
            Command = command;
        }

        public static Task AwaitCommands(IServiceProvider services)
        {
            Console.CancelKeyPress += async (_, args) => 
            {
                args.Cancel = true;
                await CommandList
                    .Find(x => x.Name == "exit")
                    .Execute(services);
            };

            return Task.Run(async () =>
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                var buffer = new StringBuilder();

                while (Program.IsRunning)
                {
                    if (SpinWait.SpinUntil(() => Console.KeyAvailable, 100))
                    {
                        var keyInfo = Console.ReadKey(true);

                        switch (keyInfo.Key)
                        {
                            case ConsoleKey.Enter:
                            {
                                var input = buffer.ToString();
                                buffer.Clear();

                                var cmd = CommandList.Find(x => x.Name == input || x.Alias.Contains(input));

                                if (cmd != null)
                                    await cmd.Execute(services);
                                else
                                    logger.LogInformation("Invalid ProgramCommand");
                            } break;

                            case ConsoleKey.Backspace or ConsoleKey.Delete when buffer.Length > 0:
                                buffer.Remove(buffer.Length - 1, 1);
                                break;

                            default:
                                buffer.Append(keyInfo.KeyChar);
                                break;
                        }
                    }
                }
            });
        }

        public static readonly List<ProgramCommand> CommandList = new()
        {
            new ProgramCommand("exit", new[] { "logout", "stop" }, async services =>
            {
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    await Program.Exit(services);
                }

                catch (Exception e)
                {
                    logger.LogError("An exception occurred while ending the flow of execution" + 
                                    $"\nReason: {e.Message}");
                }
            }),

            new ProgramCommand("reload", null, async services => 
            {
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    await Program.Exit(services);

                    Process.Start(Path.GetFileName(Assembly.GetEntryAssembly().Location));
                    Process.GetCurrentProcess().Close();
                    Process.GetCurrentProcess().Kill();
                }

                catch (Exception e)
                {
                    logger.LogError("An exception occurred while ending the flow of execution" +
                                    $"\nReason: {e.Message}");
                }
            }),

            new ProgramCommand("config_reset", null, async services =>
            {
                var logger = services.GetRequiredService<ILogger<Program>>();

                await ProgramConfig.ResetConfig();
                logger.LogInformation("Forcing config reset");
            }),           

            new ProgramCommand("clear", null, async services =>
            {
                var logger = services.GetRequiredService<ProgramLogger>();

                Console.Clear();
                logger.LogInformation("Forcing console clear");
            }),

            new ProgramCommand("gc", null, async services =>
            {
                var logger = services.GetRequiredService<ILogger<Program>>();

                GC.Collect();
                logger.LogInformation("Forcing GarbageCollector");
            }),

            new ProgramCommand("alloc", null, async services =>
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation($"Memory allocated: {GC.GetTotalMemory(false):0,0} bytes");
            }),

            new ProgramCommand("offline", null, async services =>
            {
                var client = services.GetService<DiscordShardedClient>();
                var logger = services.GetRequiredService<ILogger<Program>>();
                var config = services.GetService<ProgramConfig>();

                await client.SetStatusAsync(UserStatus.DoNotDisturb);
                await client.SetGameAsync(config.Activity.Offline, type: ActivityType.Watching);
                logger.LogInformation("UserStatus set to 'DoNotDisturb'");
            }),

            new ProgramCommand("online", null, async services =>
            {
                var client = services.GetService<DiscordShardedClient>();
                var logger = services.GetRequiredService<ILogger<Program>>();
                var config = services.GetService<ProgramConfig>();

                await client.SetStatusAsync(UserStatus.Online);
                await client.SetGameAsync(config.Activity.Online, type: config.Activity.ActivityType);
                logger.LogInformation("UserStatus set to 'Online'");
            }),

            new ProgramCommand("afk", null, async services =>
            {
                var client = services.GetService<DiscordShardedClient>();
                var logger = services.GetRequiredService<ILogger<Program>>();
                var config = services.GetService<ProgramConfig>();

                await client.SetStatusAsync(UserStatus.AFK);
                await client.SetGameAsync(config.Activity.Afk, type: ActivityType.Watching);
                logger.LogInformation("UserStatus set to 'AFK'");
            })
        };
    }
}
