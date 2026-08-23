using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

using NOVAxis.Core;

using Discord;
using Discord.LibDave.Binding;

namespace NOVAxis.Extensions
{
    public static class DiscordExtensions
    {
        public static LogSeverity ToSeverity(this LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => LogSeverity.Debug,
                LogLevel.Debug => LogSeverity.Verbose,
                LogLevel.Information => LogSeverity.Info,
                LogLevel.Warning => LogSeverity.Warning,
                LogLevel.Error => LogSeverity.Error,
                LogLevel.Critical or LogLevel.None => LogSeverity.Critical,
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
            };
        }

        public static LogLevel ToLevel(this LogSeverity logSeverity)
        {
            return logSeverity switch
            {
                LogSeverity.Debug => LogLevel.Trace,
                LogSeverity.Verbose => LogLevel.Debug,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Critical => LogLevel.Critical,
                _ => throw new ArgumentOutOfRangeException(nameof(logSeverity), logSeverity, null)
            };
        }

        public static LogLevel ToLevel(this LoggingSeverity logSeverity)
        {
            return logSeverity switch
            {
                LoggingSeverity.Verbose => LogLevel.Trace,
                LoggingSeverity.Info => LogLevel.Information,
                LoggingSeverity.Warning => LogLevel.Warning,
                LoggingSeverity.Error => LogLevel.Error,
                LoggingSeverity.None => LogLevel.None,
                _ => throw new ArgumentOutOfRangeException(nameof(logSeverity), logSeverity, null)
            };
        }

        public static Task Log(this ILogger logger, LogMessage msg)
        {
            logger.Log(msg.Severity.ToLevel(), 0, msg, msg.Exception, ProgramLogger.MessageFormatter);
            return Task.CompletedTask;
        }

        public static void Log(this ILogger logger, LoggingSeverity severity, string file, int line, string message)
        {
            logger.Log(severity.ToLevel(), 0, $"{Path.GetFileName(file)}#{line}: {message}", null, ProgramLogger.MessageFormatter);
        }

        public static IAsyncEnumerable<IGuildUser> GetHumanUsers(this IVoiceChannel voiceChannel)
        {
            return voiceChannel.GetUsersAsync()
                .Flatten()
                .Where(u => !u.IsBot);
        }
    }
}
