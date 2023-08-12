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

        public static IServiceCollection AddCache<TKey, TValue>(this IServiceCollection collection, CacheOptions options = null)
        {
            options ??= new CacheOptions();
            collection.AddSingleton(new Cache<TKey, TValue>(options));
            return collection;
        }

        public static IServiceCollection AddInteractionCache(this IServiceCollection collection, CacheOptions options = null)
        {
            options ??= new CacheOptions();
            collection.AddSingleton(new InteractionCache(options));
            return collection;
        }
    }
}
