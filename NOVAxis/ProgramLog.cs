using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis
{
    static class ProgramLog
    {
        private const string logPathFormat = @"log\log_{0}.txt";

        private static string logPath
        {
            get => string.Format(logPathFormat, DateTime.Now.ToShortDateString());
        }

        public static Task ToConsole(LogMessage arg)
        { 
            switch (arg.Severity)
            {
                case LogSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;

                case LogSeverity.Error:
                case LogSeverity.Critical:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;

                case LogSeverity.Debug:
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
            }

            Console.WriteLine($"[{DateTime.Now}] {arg.Source} | <{arg.Severity}> {arg.Message}");
            Console.ForegroundColor = ConsoleColor.Gray;

            return Task.CompletedTask;
        }

        public static async Task ToFile(LogMessage arg)
        {
            if (!Directory.Exists(Directory.GetParent(logPath).FullName))
                Directory.CreateDirectory(Directory.GetParent(logPath).FullName);

            using (StreamWriter s = new StreamWriter(logPath, true, Encoding.UTF8))
                await s.WriteLineAsync($"[{DateTime.Now}] {arg.Source} | <{arg.Severity}> {arg.Message}");
        }
    }
}
