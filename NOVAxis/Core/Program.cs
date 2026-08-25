using System;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Extensions;
using NOVAxis.Web;

namespace NOVAxis.Core
{
    public static class Program
    {
        public static ulong OwnerId => 269182357704015873L;

        public static string Version
            => Assembly.GetExecutingAssembly().GetName().Version?.ToString()[..5];

        public static Task Main(string[] args)
        {
            Console.WriteLine("NOVAxis v" + Version);

            var builder = Host.CreateDefaultBuilder(args)
                .ConfigureServices(SetupServices)
                .ConfigureAppConfiguration(SetupConfig);

            var webOptions = GetWebOptions();

            if (webOptions.Active)
            {
                builder.ConfigureWebHostDefaults(web => web
                    .UseUrls(webOptions.ListenAddress)
                    .Configure(WebPipeline.Configure));
            }

            // Logging stays last, as it clears every provider registered before it
            builder.ConfigureLogging(SetupLogging);

            var host = builder.Build();
            return host.RunAsync();
        }

        /// <summary>
        /// Reads the web section ahead of the host being built, because hosting a web
        /// server is decided before the host exists.
        /// </summary>
        private static WebOptions GetWebOptions()
        {
            var config = new ConfigurationBuilder();
            SetupConfig(config);

            var options = new WebOptions();
            config.Build().GetSection(WebOptions.Key).Bind(options);

            return options;
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
                .AddAnthropic(host.Configuration)
                .AddWebApp(host.Configuration)
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
