using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Discord;

namespace NOVAxis.Core
{
    public class ProgramLogger : ILogger
    {
        private string Source { get; }
        private IOptions<LogOptions> Options { get; }

        public ProgramLogger(string source, IOptions<LogOptions> options)
        {
            Source = source;
            Options = options;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= Options.Value.Level;
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

            Console.Write($"{DateTime.Now} ");

            Console.ForegroundColor = logColor;
            Console.Write($"{logLevel,-11} ");

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"[{Source}] ");

            Console.ForegroundColor = logColor;
            Console.WriteLine($"{formatter(state, exception)}");

            Console.ResetColor();
        }

        public static string MessageFormatter(LogMessage msg, Exception error)
        {
            return $"<{msg.Source}> {msg.Message} {error}";
        }
    }

    public class ProgramLoggerProvider : ILoggerProvider
    {
        private IOptions<LogOptions> Options { get; }

        public ProgramLoggerProvider(IOptions<LogOptions> options)
            { Options = options; }

        public ILogger CreateLogger(string source) =>
            new ProgramLogger(source, Options);

        public void Dispose() { }
    }
}
