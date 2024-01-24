using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Extensions;

namespace NOVAxis.Core
{
    public class Program
    {
        public static ulong OwnerId => 269182357704015873L;

        public static string Version
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString()[..5];

        public static Task Main(string[] args)
        {
            Console.WriteLine("NOVAxis v" + Version);

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureServices(SetupServices)
                .ConfigureAppConfiguration(SetupConfig)
                .ConfigureLogging(SetupLogging);

            var host = builder.Build();
            return host.RunAsync();
        }

        private static void SetupConfig(IConfigurationBuilder config)
        {
            config
                .AddJsonFile("config.json", true)
                .AddEnvironmentVariables()
                .Build();
        }

        private static void SetupServices(HostBuilderContext host, IServiceCollection services)
        {
            services
                .AddMemoryCache()
                .AddConfiguration(host.Configuration)
                .AddDiscord(host.Configuration)
                .AddInteractions(host.Configuration)
                .AddAudio(host.Configuration)
                .AddPolls(host.Configuration)
                .BuildServiceProvider(true);
        }

        private static void SetupLogging(HostBuilderContext host, ILoggingBuilder builder)
        {
            builder.AddConfiguration(host.Configuration);
            builder.ClearProviders();
            builder.AddProgramLogger();
        }
    }
}
