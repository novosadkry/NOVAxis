using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Core
{
    public static class ProgramLog
    {
        private static string LogPathFormat => Path.Combine(".", "log", "log_{0}.txt");
        private static string LogPath => string.Format(LogPathFormat, $"{DateTime.Now.Day}.{DateTime.Now.Month}.{DateTime.Now.Year}");

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

            await using StreamWriter writer = new StreamWriter(LogPath, true, Encoding.UTF8);
            await writer.WriteLineAsync($"[{DateTime.Now}] {arg.Source} | <{arg.Severity}> {arg.Message}");
        }
    }
}
