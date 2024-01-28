using System;

using NOVAxis.Core;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NOVAxis.Extensions
{
    public static class ProgramExtensions
    {
        public static ILoggingBuilder AddProgramLogger(this ILoggingBuilder builder)
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ProgramLoggerProvider>());
            builder.SetMinimumLevel(LogLevel.Trace);
            return builder;
        }

        public static void Warning(this ILogger logger, string message, Exception exception = null)
        {
            logger.Log(LogLevel.Warning, 0, message, exception, ProgramLogger.MessageFormatter);
        }

        public static void Error(this ILogger logger, string message, Exception exception = null)
        {
            logger.Log(LogLevel.Error, 0, message, exception, ProgramLogger.MessageFormatter);
        }

        public static void Trace(this ILogger logger, string message, Exception exception = null)
        {
            logger.Log(LogLevel.Trace, 0, message, exception, ProgramLogger.MessageFormatter);
        }

        public static void Critical(this ILogger logger, string message, Exception exception = null)
        {
            logger.Log(LogLevel.Critical, 0, message, exception, ProgramLogger.MessageFormatter);
        }

        public static void Info(this ILogger logger, string message, Exception exception = null)
        {
            logger.Log(LogLevel.Information, 0, message, exception, ProgramLogger.MessageFormatter);
        }

        public static void Debug(this ILogger logger, string message, Exception exception = null)
        {
            logger.Log(LogLevel.Debug, 0, message, exception, ProgramLogger.MessageFormatter);
        }
    }
}
