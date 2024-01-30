using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using NOVAxis.Extensions;

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

            Logger.Info("Polls host service starting...");

            var stopToken = _stopTokenSource.Token;
            _executionTask = Task.Run(() => RunAsync(stopToken), cancellationToken);

            Logger.Info("Polls host service started");
        }

        private async Task RunAsync(CancellationToken stopToken)
        {
            while (!stopToken.IsCancellationRequested)
            {
                try
                {
                    await DoWork(stopToken);
                    await Task.Delay(TimeSpan.FromMinutes(1), stopToken);
                }
                catch (Exception e) when (e is not TaskCanceledException)
                {
                    Logger.Error("The flow of execution has been halted due to an exception", e);
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Logger.Info("Polls host service stopping...");

            await _stopTokenSource.CancelAsync();

            try { await _executionTask.WaitAsync(cancellationToken); }
            catch (TaskCanceledException) { }

            Logger.Info("Polls host service stopped");
        }

        private async Task DoWork(CancellationToken stopToken)
        {
            stopToken.ThrowIfCancellationRequested();

            Logger.Debug("Polls host service tick");

            var toRemove = new List<ulong>();

            foreach (var interaction in PollService.Interactions)
            {
                await interaction.Refresh();

                if (interaction.Poll.State == PollState.Expired)
                    toRemove.Add(interaction.Poll.Id);
            }

            foreach (var id in toRemove)
            {
                PollService.Remove(id);
                Logger.Debug($"Removed interaction for poll id {id}");
            }
        }

        public void Dispose()
        {
            _executionTask?.Dispose();
            _stopTokenSource?.Dispose();
        }
    }
}
