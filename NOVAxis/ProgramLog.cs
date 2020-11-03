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
        private static string LogPathFormat
        {
            get => Path.Combine(".", "log", "log_{0}.txt");
        }

        private static string LogPath
        {
            get => string.Format(LogPathFormat, string.Format("{0}.{1}.{2}", DateTime.Now.Day, DateTime.Now.Month, DateTime.Now.Year));
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
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
            }

            Console.WriteLine($"[{DateTime.Now}] {arg.Source} | <{arg.Severity}> {arg.Message} {arg.Exception}");
            Console.ForegroundColor = ConsoleColor.Gray;

            return Task.CompletedTask;
        }

        public static async Task ToFile(LogMessage arg)
        {
            if (!Directory.Exists(Directory.GetParent(LogPath).FullName))
                Directory.CreateDirectory(Directory.GetParent(LogPath).FullName);

            using (StreamWriter s = new StreamWriter(LogPath, true, Encoding.UTF8))
                await s.WriteLineAsync($"[{DateTime.Now}] {arg.Source} | <{arg.Severity}> {arg.Message}");
        }
    }
}
