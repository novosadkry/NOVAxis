using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using EmbedIO;
using NOVAxis.Extensions;

using WebServerOptions = NOVAxis.Core.WebServerOptions;

namespace NOVAxis.Services.WebServer
{
    public sealed class WebServerHostService : IHostedService, IDisposable
    {
        private readonly CancellationTokenSource _stopTokenSource;

        private IServiceProvider ServiceProvider { get; }
        private EmbedIO.WebServer WebServer { get; set; }
        private ILogger<WebServerHostService> Logger { get; }
        private IOptions<WebServerOptions> Options { get; }

        public WebServerHostService(
            IOptions<WebServerOptions> options,
            ILogger<WebServerHostService> logger,
            IServiceProvider serviceProvider)
        {
            Logger = logger;
            Options = options;
            ServiceProvider = serviceProvider;

            _stopTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Swan.Logging.Logger.NoLogging();
            Logger.Info("Web Server host service starting...");

            WebServer = new EmbedIO.WebServer(o => o
                .WithUrlPrefix(Options.Value.Endpoint)
                .WithMode(HttpListenerMode.EmbedIO));

            WebServer.WithIoC(ServiceProvider);
            WebServer.WithWebApi(WebRoutes.Api.Base, m => m
                .RegisterController<WebDownloadController>());

            WebServer.Start(_stopTokenSource.Token);

            Logger.Info("Web Server host service started");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Logger.Info("Web Server host service stopping...");

            await _stopTokenSource.CancelAsync();

            Logger.Info("Web Server host service stopped");
        }

        public void Dispose()
        {
            WebServer?.Dispose();
            _stopTokenSource?.Dispose();
        }
    }
}
