using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Web.Hubs;

namespace NOVAxis.Web
{
    /// <summary>
    /// Pushes the spectrum to whoever has it open, at its own pace and on its own loop.
    ///
    /// Deliberately not part of the one second state tick. That loop awaits its sends, so
    /// a browser which has stopped draining would hold up everything behind it - survivable
    /// once a second, not thirty times. Here a guild whose last frame has not gone out yet
    /// is skipped rather than queued: dropping a frame of animation is the right answer,
    /// and letting the queue grow is not.
    /// </summary>
    public class SpectrumBroadcastService : BackgroundService
    {
        private readonly Dictionary<ulong, Task> _inFlight = new();
        private readonly Dictionary<ulong, string> _names = new();

        private PlayerHubTracker Tracker { get; }
        private PlayerBroadcaster Broadcaster { get; }
        private SpectrumAnalyzer Spectrum { get; }
        private IOptions<AudioOptions> Options { get; }
        private ILogger<SpectrumBroadcastService> Logger { get; }

        public SpectrumBroadcastService(
            PlayerHubTracker tracker,
            PlayerBroadcaster broadcaster,
            SpectrumAnalyzer spectrum,
            IOptions<AudioOptions> options,
            ILogger<SpectrumBroadcastService> logger)
        {
            Tracker = tracker;
            Broadcaster = broadcaster;
            Spectrum = spectrum;
            Options = options;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var settings = Options.Value.Spectrum;

            if (!settings.Active)
                return;

            var rate = Math.Clamp(settings.Fps, 1, 60);
            var window = settings.FftSize;
            var hop = FfmpegAudioStream.SampleRate / rate;

            if (window < hop)
            {
                // Otherwise the gap between two reads is wider than a read, and the audio
                // in between is never looked at - transients fall through and it stutters
                Logger.Warning($"A {window} point window cannot keep up with {rate} frames " +
                               $"a second; {hop} points pass between them and are not analysed");
            }

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1d / rate));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    Sweep(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Logger.Warning("Unable to push a spectrum frame", e);
                }
            }
        }

        private void Sweep(CancellationToken stoppingToken)
        {
            var wanted = Tracker.SpectrumGuilds;

            // The analyzer taps exactly whoever is being drawn, so a guild nobody has the
            // spectrum open for costs its playback loop nothing but a lookup
            Spectrum.KeepOnly(wanted);

            foreach (var guildId in wanted)
            {
                if (_inFlight.TryGetValue(guildId, out var sending) && !sending.IsCompleted)
                    continue;

                var bands = Spectrum.Read(guildId);

                // Nothing playing, or paused: silence is no message at all rather than a
                // flat line, so an idle guild is free
                if (bands == null)
                    continue;

                if (!_names.TryGetValue(guildId, out var name))
                    _names[guildId] = name = guildId.ToString();

                var sent = Broadcaster.PushSpectrumAsync(name, bands, stoppingToken);

                // A send which fails is a dropped frame and nothing more, but a failure
                // nobody looks at is one raised again at a finalizer
                _ = sent.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

                _inFlight[guildId] = sent;
            }

            Forget(wanted);
        }

        /// <summary>
        /// Stops the bookkeeping growing a row per guild ever watched.
        /// </summary>
        private void Forget(IReadOnlyList<ulong> wanted)
        {
            if (_inFlight.Count <= wanted.Count)
                return;

            var open = wanted.ToHashSet();

            foreach (var guildId in _inFlight.Keys.ToList())
            {
                if (!open.Contains(guildId))
                {
                    _inFlight.Remove(guildId);
                    _names.Remove(guildId);
                }
            }
        }
    }
}
