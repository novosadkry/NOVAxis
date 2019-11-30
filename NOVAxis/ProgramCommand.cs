using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using System.IO;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis
{
    class ProgramCommand
    {
        public class Context
        {
            public DiscordSocketClient Client { get; set; }
            public ProgramConfig Config { get; set; }
            public Func<LogMessage, Task> Client_Log { get; set; }
        }

        public string Name { get; }
        public string[] Alias { get; }
        private Action<Context> _command;

        public Task Execute(Context context)
        {
            return Task.Run(() => _command(context));
        }

        public ProgramCommand(string name, string[] alias, Action<Context> command)
        {
            Name = name;
            Alias = alias ?? new[] { "" };
            _command = command;
        }

        public static readonly List<ProgramCommand> ProgramCommandList = new List<ProgramCommand>
        {
            new ProgramCommand("exit", new[] { "logout", "stop" }, async (context) => 
            {
                try
                {
                    await context.Client.LogoutAsync();
                    await context.Client.StopAsync();

                    try { await Services.LavalinkService.Manager.StopAsync(); }
                    catch (ObjectDisposedException) { }
                }

                catch (Exception e)
                {
                    await context.Client_Log(new LogMessage(LogSeverity.Error, "Program",
                        "An exception occurred while ending the flow of execution" +
                        $"\nReason: {e.Message}"));
                }
            }),

            new ProgramCommand("reload", null, async (context) => 
            {
                try
                {
                    await context.Client.LogoutAsync();
                    await context.Client.StopAsync();

                    try { await Services.LavalinkService.Manager.StopAsync(); }
                    catch (ObjectDisposedException) { }

                    Process.Start(Path.GetFileName(Assembly.GetEntryAssembly().Location));
                    Process.GetCurrentProcess().Close();
                    Process.GetCurrentProcess().Kill();
                }

                catch (Exception e)
                {
                    await context.Client_Log(new LogMessage(LogSeverity.Error, "Program",
                        "An exception occurred while ending the flow of execution" +
                        $"\nReason: {e.Message}"));
                }
            }),

            new ProgramCommand("config_reset", null, async (context) => 
            {
                await ProgramConfig.ResetConfig();
                await context.Client_Log(new LogMessage(LogSeverity.Info, "Program", "Forcing config reset"));
            }),           

            new ProgramCommand("clear", null, async (context) =>
            {
                Console.Clear();
                await context.Client_Log(new LogMessage(LogSeverity.Info, "Program", "Forcing console clear"));
            }),

            new ProgramCommand("gc", null, async (context) => 
            {
                GC.Collect();
                await context.Client_Log(new LogMessage(LogSeverity.Info, "Program", "Forcing GarbageCollector"));
            }),

            new ProgramCommand("alloc", null, async (context) => 
            {
                await context.Client_Log(new LogMessage(LogSeverity.Info, "Program", $"Memory allocated: {GC.GetTotalMemory(false):0,0} bytes"));
            }),

            new ProgramCommand("offline", null, async (context) => 
            {
                await context.Client.SetStatusAsync(UserStatus.DoNotDisturb);
                await context.Client.SetGameAsync(context.Config.Activity.Offline, type: ActivityType.Watching);
                await context.Client_Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'DoNotDisturb'"));
            }),

            new ProgramCommand("online", null, async (context) => 
            {
                await context.Client.SetStatusAsync(UserStatus.Online);
                await context.Client.SetGameAsync(context.Config.Activity.Online, type: context.Config.ActivityType);
                await context.Client_Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'Online'"));
            }),

            new ProgramCommand("afk", null, async (context) => 
            {
                await context.Client.SetStatusAsync(UserStatus.AFK);
                await context.Client.SetGameAsync(context.Config.Activity.Afk, type: ActivityType.Watching);
                await context.Client_Log(new LogMessage(LogSeverity.Info, "Discord", "UserStatus set to 'AFK'"));
            }),

            new ProgramCommand("lavalink", null, async (context) => 
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                        Process.Start(new ProcessStartInfo { FileName = Path.Combine(".", "Lavalink", "Lavalink.bat"), UseShellExecute = true });
                        await context.Client_Log(new LogMessage(LogSeverity.Info, "Lavalink", "Launching Lavalink node (command prompt)"));
                        break;

                    case PlatformID.Unix:
                        Process.Start(new ProcessStartInfo { FileName = Path.Combine(".", "Lavalink", "Lavalink.sh") });
                        await context.Client_Log(new LogMessage(LogSeverity.Info, "Lavalink", "Launching Lavalink node (screen)"));
                        break;

                    default:
                        await context.Client_Log(new LogMessage(LogSeverity.Info, "Program", $"This command isn't supported on your OS ({Environment.OSVersion.Platform})"));
                        return;
                }        
            }),

            new ProgramCommand("lavalink_stats", null, async (context) =>
            {
                var stats = Services.LavalinkService.ManagerStats;

                await context.Client_Log(new LogMessage(LogSeverity.Info, "Lavalink",
                    $"Lavalink statistics: " +
                    $"\nMemory allocated: {stats?.Memory.Allocated:0,0} bytes " +
                    $"\nMemory free: {stats?.Memory.Free:0,0} bytes " +
                    $"\nMemory used: {stats?.Memory.Used:0,0} bytes " +
                    $"\nCPU cores: {stats?.CPU.Cores} " +
                    $"\nCPU systemload: {stats?.CPU.SystemLoad:0.000} % " +
                    $"\nCPU lavalinkload: {stats?.CPU.LavalinkLoad:0.000} % " +
                    $"\nPlayers: {stats?.Players} " +
                    $"| Playing: {stats?.PlayingPlayers} " +
                    $"\nUptime: {TimeSpan.FromMilliseconds(stats?.Uptime ?? 0)}"));
            }),
        };
    }
}
