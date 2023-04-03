using NOVAxis.Core;
using NOVAxis.Utilities;

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

        public static IServiceCollection AddCache<TKey, TValue>(this IServiceCollection collection)
        {
            collection.AddSingleton(new Cache<TKey, TValue>());
            return collection;
        }
    }
}
