using System;
using System.Text;
using Microsoft.Extensions.Logging;

using Discord;

namespace NOVAxis.Core
{
    public class ProgramLogger : ILogger
    {
        private string Source { get; }

        public ProgramLogger(string source)
        {
            Source = source;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => default;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var builder = new StringBuilder();

            if (!IsEnabled(logLevel))
                return;

            var logColor = logLevel switch
            {
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                LogLevel.Trace or LogLevel.Debug => ConsoleColor.DarkGray,
                _ => ConsoleColor.White
            };

            var colorCode = GetColorEscapeCode(logColor);

            builder.Append(GetColorEscapeCode(ConsoleColor.Gray));
            builder.Append($"{DateTime.Now} ");

            builder.Append(colorCode);
            builder.Append($"{logLevel,-11} ");

            builder.Append(GetColorEscapeCode(ConsoleColor.Blue));
            builder.Append($"[{Source}] ");

            builder.Append(colorCode);
            builder.Append($"{formatter(state, exception)}");

            builder.Append(DefaultColorEscapeCode);

            Console.WriteLine(builder.ToString());
            Console.ResetColor();
        }

        public static string MessageFormatter(string msg, Exception error)
        {
            return $"{msg} {error}";
        }

        public static string MessageFormatter(LogMessage msg, Exception error)
        {
            return $"<{msg.Source}> {msg.Message} {error}";
        }

        private const string DefaultColorEscapeCode = "\x1B[39m\x1B[22m";

        static string GetColorEscapeCode(ConsoleColor color) => color switch
        {
            ConsoleColor.Black => "\x1B[30m",
            ConsoleColor.DarkRed => "\x1B[31m",
            ConsoleColor.DarkGreen => "\x1B[32m",
            ConsoleColor.DarkYellow => "\x1B[33m",
            ConsoleColor.DarkBlue => "\x1B[34m",
            ConsoleColor.DarkMagenta => "\x1B[35m",
            ConsoleColor.DarkCyan => "\x1B[36m",
            ConsoleColor.Gray => "\x1B[37m",
            ConsoleColor.DarkGray => "\x1B[90m",
            ConsoleColor.Red => "\x1B[1m\x1B[31m",
            ConsoleColor.Green => "\x1B[1m\x1B[32m",
            ConsoleColor.Yellow => "\x1B[1m\x1B[33m",
            ConsoleColor.Blue => "\x1B[1m\x1B[34m",
            ConsoleColor.Magenta => "\x1B[1m\x1B[35m",
            ConsoleColor.Cyan => "\x1B[1m\x1B[36m",
            ConsoleColor.White => "\x1B[1m\x1B[37m",
            _ => DefaultColorEscapeCode
        };
    }

    public class ProgramLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string source) =>
            new ProgramLogger(source);

        public void Dispose() { }
    }
}
