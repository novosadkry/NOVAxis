using System;

using Discord;
using Microsoft.Extensions.Logging;

namespace NOVAxis.Core
{
    public class ProgramLogger : ILogger
    {
        private string Source { get; }
        private ProgramConfig Config { get; }

        public ProgramLogger(string source, ProgramConfig config)
        {
            Source = source;
            Config = config;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= Config.Log.Level;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => default;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var logColor = logLevel switch
            {
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                LogLevel.Trace or LogLevel.Debug => ConsoleColor.DarkGray,
                _ => ConsoleColor.White
            };

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"{DateTime.Now} ");
            Console.ForegroundColor = logColor;
            Console.Write($"{logLevel,-11} ");

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"[{Source}] ");

            Console.ForegroundColor = logColor;
            Console.WriteLine($"{formatter(state, exception)}");

            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static string MessageFormatter(LogMessage msg, Exception error)
        {
            return $"<{msg.Source}> {msg.Message} {error}";
        }
    }

    public class ProgramLoggerProvider : ILoggerProvider
    {
        private ProgramConfig Config { get; }

        public ProgramLoggerProvider(ProgramConfig config)
            { Config = config; }

        public ILogger CreateLogger(string source) =>
            new ProgramLogger(source, Config);

        public void Dispose() { }
    }
}
