﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Core
{
    public class ProgramCommand
    {
        public string Name { get; }
        public string[] Alias { get; }
        private readonly Action _command;

        public Task Execute()
        {
            return Task.Run(() => _command());
        }

        public ProgramCommand(string name, string[] alias, Action command)
        {
            Name = name;
            Alias = alias ?? new[] { "" };
            _command = command;
        }

        public static readonly List<ProgramCommand> ProgramCommandList = new List<ProgramCommand>
        {
            new ProgramCommand("exit", new[] { "logout", "stop" }, async () => 
            {
                try
                {
                    await Program.Exit();
                }

                catch (Exception e)
                {
                    await Program.Client_Log(new LogMessage(LogSeverity.Error, "Program",
                        "An exception occurred while ending the flow of execution" +
                        $"\nReason: {e.Message}"));
                }
            }),

            new ProgramCommand("reload", null, async () => 
            {
                try
                {
                    await Program.Exit();

                    Process.Start(Path.GetFileName(Assembly.GetEntryAssembly().Location));
                    Process.GetCurrentProcess().Close();
                    Process.GetCurrentProcess().Kill();
                }

                catch (Exception e)
                {
                    await Program.Client_Log(new LogMessage(LogSeverity.Error, "Program",
                        "An exception occurred while ending the flow of execution" +
                        $"\nReason: {e.Message}"));
                }
            }),

            new ProgramCommand("config_reset", null, async () => 
            {
                await ProgramConfig.ResetConfig();
                await Program.Client_Log(new LogMessage(LogSeverity.Info, "Program", "Forcing config reset"));
            }),           

            new ProgramCommand("clear", null, async () =>
            {
                Console.Clear();
                await Program.Client_Log(new LogMessage(LogSeverity.Info, "Program", "Forcing console clear"));
            }),

            new ProgramCommand("gc", null, async () => 
            {
                GC.Collect();
                await Program.Client_Log(new LogMessage(LogSeverity.Info, "Program", "Forcing GarbageCollector"));
            }),

            new ProgramCommand("alloc", null, async () => 
            {
                await Program.Client_Log(new LogMessage(LogSeverity.Info, "Program", $"Memory allocated: {GC.GetTotalMemory(false):0,0} bytes"));
            }),

            new ProgramCommand("offline", null, async () => 
            {
                await Program.Client.SetStatusAsync(UserStatus.DoNotDisturb);
                await Program.Client.SetGameAsync(Program.Config.Activity.Offline, type: ActivityType.Watching);
                await Program.Client_Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'DoNotDisturb'"));
            }),

            new ProgramCommand("online", null, async () => 
            {
                await Program.Client.SetStatusAsync(UserStatus.Online);
                await Program.Client.SetGameAsync(Program.Config.Activity.Online, type: Program.Config.Activity.ActivityType);
                await Program.Client_Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'Online'"));
            }),

            new ProgramCommand("afk", null, async () => 
            {
                await Program.Client.SetStatusAsync(UserStatus.AFK);
                await Program.Client.SetGameAsync(Program.Config.Activity.Afk, type: ActivityType.Watching);
                await Program.Client_Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'AFK'"));
            }),

            new ProgramCommand("lavalink", null, async () => 
            {
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
                        await Program.Client_Log(new LogMessage(LogSeverity.Info, "Lavalink", "Launching Lavalink node (command prompt)"));
                        break;

                    case PlatformID.Unix:
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = "-c \"screen -dmS novaxis-lavalink java -jar Lavalink.jar\"",
                            WorkingDirectory = Path.Combine(".", "Lavalink")
                        });
                        await Program.Client_Log(new LogMessage(LogSeverity.Info, "Lavalink", "Launching Lavalink node (screen)"));
                        break;

                    default:
                        await Program.Client_Log(new LogMessage(LogSeverity.Info, "Program", $"This command isn't supported on your OS ({Environment.OSVersion.Platform})"));
                        return;
                }        
            }),
        };
    }
}