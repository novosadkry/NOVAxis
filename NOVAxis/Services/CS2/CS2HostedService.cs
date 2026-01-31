using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOVAxis.Database.Entities;
using NOVAxis.Extensions;

namespace NOVAxis.Services.CS2
{
    public class CS2HostedService : IHostedService, IDisposable
    {
        private Task _executionTask;
        private CancellationTokenSource _stopTokenSource;

        private readonly ILogger<CS2HostedService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public CS2HostedService(
            ILogger<CS2HostedService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _stopTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.Info("CS2 host service starting...");

            var stopToken = _stopTokenSource.Token;
            _executionTask = Task.Run(() => RunAsync(stopToken), cancellationToken);

            _logger.Info("CS2 host service started");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.Info("CS2 host service stopping...");

            await _stopTokenSource.CancelAsync();

            try { await _executionTask.WaitAsync(cancellationToken); }
            catch (TaskCanceledException) { }

            _logger.Info("CS2 host service stopped");
        }

        private async Task RunAsync(CancellationToken stopToken)
        {
            while (!stopToken.IsCancellationRequested)
            {
                try
                {
                    await DoWork(stopToken);
                    await Task.Delay(5000, stopToken);
                }
                catch (Exception e) when (e is not TaskCanceledException)
                {
                    _logger.Error("The flow of execution has been halted due to an exception", e);
                    return;
                }
            }
        }

        private async Task DoWork(CancellationToken stopToken)
        {
            stopToken.ThrowIfCancellationRequested();

            await using var scope = _serviceProvider.CreateAsyncScope();
            var demoService = scope.ServiceProvider.GetRequiredService<CS2DemoService>();
            var queueService = scope.ServiceProvider.GetRequiredService<CS2DemoQueueService>();

            // Check for messages in the queue
            var message = await queueService.DequeueAsync();
            if (message == null) return;

            try
            {
                await demoService.ProcessDemoAsync(message);

                message.Status = CS2DemoQueueStatus.Completed;
                await queueService.UpdateStatusAsync(message);
            }
            catch (Exception ex)
            {
                message.Status = CS2DemoQueueStatus.Failed;
                await queueService.UpdateStatusAsync(message);

                _logger.Error("Error processing demo from queue", ex);
                await Task.Delay(10000, stopToken); // Wait longer on error
            }
        }

        public void Dispose()
        {
            _executionTask?.Dispose();
            _stopTokenSource?.Dispose();
        }
    }
}
