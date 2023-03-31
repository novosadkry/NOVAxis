using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
            return Task.Run(async () =>
            {
                var client = services.GetRequiredService<DiscordShardedClient>();
                var logger = services.GetRequiredService<ILogger<Program>>();

                while (client.LoginState == LoginState.LoggedIn)
                {
                    string input = Console.ReadLine();

                    for (int i = 0; i < CommandList.Count; i++)
                    {
                        ProgramCommand c = CommandList[i];

                        if (input == c.Name || c.Alias.Contains(input) && !string.IsNullOrWhiteSpace(input))
                        {
                            await c.Execute(services);
                            break;
                        }

                        if (i + 1 == CommandList.Count)
                            logger.LogInformation("Invalid ProgramCommand");
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
