using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;
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
                var logger = services.GetRequiredService<ProgramLogger>();

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
                            await logger.Log(new LogMessage(LogSeverity.Info, "Program", "Invalid ProgramCommand"));
                    }
                }
            });
        }

        public static readonly List<ProgramCommand> CommandList = new()
        {
            new ProgramCommand("exit", new[] { "logout", "stop" }, async services =>
            {
                var logger = services.GetRequiredService<ProgramLogger>();

                try
                {
                    await Program.Exit(services);
                }

                catch (Exception e)
                {
                    await logger.Log(new LogMessage(LogSeverity.Error, "Program",
                        "An exception occurred while ending the flow of execution" +
                        $"\nReason: {e.Message}"));
                }
            }),

            new ProgramCommand("reload", null, async services => 
            {
                var logger = services.GetRequiredService<ProgramLogger>();

                try
                {
                    await Program.Exit(services);

                    Process.Start(Path.GetFileName(Assembly.GetEntryAssembly().Location));
                    Process.GetCurrentProcess().Close();
                    Process.GetCurrentProcess().Kill();
                }

                catch (Exception e)
                {
                    await logger.Log(new LogMessage(LogSeverity.Error, "Program",
                        "An exception occurred while ending the flow of execution" +
                        $"\nReason: {e.Message}"));
                }
            }),

            new ProgramCommand("config_reset", null, async services =>
            {
                var logger = services.GetRequiredService<ProgramLogger>();
                
                await ProgramConfig.ResetConfig();
                await logger.Log(new LogMessage(LogSeverity.Info, "Program", "Forcing config reset"));
            }),           

            new ProgramCommand("clear", null, async services =>
            {
                var logger = services.GetRequiredService<ProgramLogger>();

                Console.Clear();
                await logger.Log(new LogMessage(LogSeverity.Info, "Program", "Forcing console clear"));
            }),

            new ProgramCommand("gc", null, async services =>
            {
                var logger = services.GetRequiredService<ProgramLogger>();

                GC.Collect();
                await logger.Log(new LogMessage(LogSeverity.Info, "Program", "Forcing GarbageCollector"));
            }),

            new ProgramCommand("alloc", null, async services =>
            {
                var logger = services.GetRequiredService<ProgramLogger>();
                await logger.Log(new LogMessage(LogSeverity.Info, "Program", $"Memory allocated: {GC.GetTotalMemory(false):0,0} bytes"));
            }),

            new ProgramCommand("offline", null, async services =>
            {
                var client = services.GetService<DiscordShardedClient>();
                var logger = services.GetRequiredService<ProgramLogger>();
                var config = services.GetService<ProgramConfig>();

                await client.SetStatusAsync(UserStatus.DoNotDisturb);
                await client.SetGameAsync(config.Activity.Offline, type: ActivityType.Watching);
                await logger.Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'DoNotDisturb'"));
            }),

            new ProgramCommand("online", null, async services =>
            {
                var client = services.GetService<DiscordShardedClient>();
                var logger = services.GetRequiredService<ProgramLogger>();
                var config = services.GetService<ProgramConfig>();

                await client.SetStatusAsync(UserStatus.Online);
                await client.SetGameAsync(config.Activity.Online, type: config.Activity.ActivityType);
                await logger.Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'Online'"));
            }),

            new ProgramCommand("afk", null, async services =>
            {
                var client = services.GetService<DiscordShardedClient>();
                var logger = services.GetRequiredService<ProgramLogger>();
                var config = services.GetService<ProgramConfig>();

                await client.SetStatusAsync(UserStatus.AFK);
                await client.SetGameAsync(config.Activity.Afk, type: ActivityType.Watching);
                await logger.Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'AFK'"));
            }),

            new ProgramCommand("lavalink", null, async services =>
            {
                var logger = services.GetRequiredService<ProgramLogger>();

                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/C java -jar Lavalink.jar",
                            WorkingDirectory = Path.Combine(".", "Lavalink"),
                            UseShellExecute = true
                        });
                        await logger.Log(new LogMessage(LogSeverity.Info, "Lavalink", "Launching Lavalink node (command prompt)"));
                        break;

                    case PlatformID.Unix:
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = "-c \"screen -dmS novaxis-lavalink java -jar Lavalink.jar\"",
                            WorkingDirectory = Path.Combine(".", "Lavalink")
                        });
                        await logger.Log(new LogMessage(LogSeverity.Info, "Lavalink", "Launching Lavalink node (screen)"));
                        break;

                    default:
                        await logger.Log(new LogMessage(LogSeverity.Info, "Program", $"This command isn't supported on your OS ({Environment.OSVersion.Platform})"));
                        return;
                }        
            }),
        };
    }
}
