using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NOVAxis.Services.Polls
{
    public class PollHostService : IHostedService, IDisposable
    {
        private Task _executionTask;
        private CancellationTokenSource _stopTokenSource;

        private PollService PollService { get; }
        private ILogger<PollHostService> Logger { get; }

        public PollHostService(PollService pollService, ILogger<PollHostService> logger)
        {
            Logger = logger;
            PollService = pollService;

            _stopTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Logger.LogInformation("Polls host service starting...");

            var stopToken = _stopTokenSource.Token;
            _executionTask = Task.Run(() => RunAsync(stopToken), cancellationToken);

            Logger.LogInformation("Polls host service started");
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

            Logger.LogInformation("Polls host service stopping...");

            await _stopTokenSource.CancelAsync();

            try { await _executionTask.WaitAsync(cancellationToken); }
            catch (TaskCanceledException) { }

            Logger.LogInformation("Polls host service stopped");
        }

        private async Task DoWork(CancellationToken stopToken)
        {
            stopToken.ThrowIfCancellationRequested();

            Logger.LogDebug("Polls host service tick");

            var toRemove = new List<ulong>();

            foreach (var tracker in PollService.Trackers)
            {
                var poll = tracker.Poll;

                if (tracker.ShouldExpire())
                    poll.Expire();

                if (tracker.ShouldClose())
                    poll.Close();

                if (poll.State == PollState.Expired)
                    toRemove.Add(poll.Id);
            }

            foreach (var id in toRemove)
            {
                PollService.Remove(id);
                Logger.LogDebug("Removed tracker for poll id {}", id);
            }
        }

        public void Dispose()
        {
            _executionTask?.Dispose();
            _stopTokenSource?.Dispose();
        }
    }
}
