using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NOVAxis.Services.Vote
{
    public class VoteHostService : IHostedService, IDisposable
    {
        private Task _executionTask;
        private ILogger<VoteHostService> _logger;
        private CancellationTokenSource _stopTokenSource;

        public VoteHostService(ILogger<VoteHostService> logger)
        {
            _logger = logger;
            _stopTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Vote host service starting...");

            var stopToken = _stopTokenSource.Token;
            _executionTask = Task.Run(() => RunAsync(stopToken), cancellationToken);

            _logger.LogInformation("Vote host service started");
        }

        private async Task RunAsync(CancellationToken stopToken)
        {
            while (!stopToken.IsCancellationRequested)
            {
                var workTask = DoWork(stopToken);
                var delayTask = Task.Delay(TimeSpan.FromMinutes(1), stopToken);

                await Task.WhenAll(workTask, delayTask);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Vote host service stopping...");

            await _stopTokenSource.CancelAsync();

            try { await _executionTask.WaitAsync(cancellationToken); }
            catch (TaskCanceledException) { }

            _logger.LogInformation("Vote host service stopped");
        }

        private async Task DoWork(CancellationToken stopToken)
        {
            stopToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Vote host service tick");

            // TODO: Go through each active vote and remove ones that reached end-of-life
        }

        public void Dispose()
        {
            _executionTask?.Dispose();
            _stopTokenSource?.Dispose();
        }
    }
}
