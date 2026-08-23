using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

using Discord;
using Discord.Audio;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Plays audio into a single guild's voice channel. Every player owns its voice
    /// connection, its queue and the ffmpeg process feeding it, so guilds never share state.
    /// </summary>
    public sealed class YtDlpAudioPlayer : IAudioPlayer
    {
        /// <summary>
        /// Why the currently playing track was interrupted. Written before the track's
        /// cancellation token is triggered and read once the pump unwinds.
        /// </summary>
        private enum PlaybackInterrupt
        {
            None,
            Skip,
            Stop,
            Seek,
            Replace
        }

        private enum PlaybackOutcome
        {
            Completed,
            Skipped,
            Stopped,
            Seek,
            Replaced,
            Failed
        }

        private readonly AudioTrackQueue _queue = new();
        private readonly SemaphoreSlim _wakeup = new(0, 1);
        private readonly SemaphoreSlim _commandLock = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();

        private readonly IVoiceChannel _voiceChannel;
        private readonly ITextChannel _textChannel;
        private readonly YtDlpClient _client;
        private readonly AudioNotifier _notifier;
        private readonly AudioOptions _options;
        private readonly ILogger _logger;
        private readonly Func<YtDlpAudioPlayer, ValueTask> _onDestroyed;

        private IAudioClient _audioClient;
        private AudioOutStream _outStream;
        private Task _playbackTask;

        private CancellationTokenSource _trackCts;
        private PlaybackInterrupt _interrupt;
        private TimeSpan _seekPosition;

        private TaskCompletionSource _resumeSignal;
        private volatile bool _paused;
        private volatile float _volume = 1.0f;
        private volatile int _state = (int)AudioPlayerState.NotPlaying;

        private AudioTrackQueueItem _currentItem;
        private AudioTrackQueueItem _replacementItem;
        private AudioTrackQueueItem _repeatItem;

        private TimeSpan _segmentStart;
        private long _segmentBytes;

        private ulong _prefetchedId;
        private Task<YtDlpStreamInfo> _prefetched;

        private bool _disposed;

        public YtDlpAudioPlayer(
            IVoiceChannel voiceChannel,
            ITextChannel textChannel,
            YtDlpClient client,
            AudioNotifier notifier,
            IOptions<AudioOptions> options,
            ILogger logger,
            Func<YtDlpAudioPlayer, ValueTask> onDestroyed)
        {
            _voiceChannel = voiceChannel;
            _textChannel = textChannel;
            _client = client;
            _notifier = notifier;
            _options = options.Value;
            _logger = logger;
            _onDestroyed = onDestroyed;

            _queue.Enqueued += Signal;
        }

        public ulong GuildId => _voiceChannel.GuildId;
        public ulong VoiceChannelId => _voiceChannel.Id;

        public IAudioTrackQueue Queue => _queue;
        public AudioTrackQueueItem CurrentItem => _currentItem;
        public AudioTrack CurrentTrack => _currentItem?.Track;

        public AudioPlayerState State => (AudioPlayerState)_state;
        public bool IsPaused => _paused;
        public float Volume => _volume;
        public AudioRepeatMode RepeatMode { get; set; }

        public TimeSpan Position
        {
            get
            {
                var elapsed = TimeSpan.FromTicks(
                    Interlocked.Read(ref _segmentBytes) * TimeSpan.TicksPerSecond / FfmpegAudioStream.BytesPerSecond);

                return _segmentStart + elapsed;
            }
        }

        /// <summary>
        /// The moment the player last had nothing to do, used by the inactivity tracker.
        /// </summary>
        public DateTimeOffset? InactiveSince { get; private set; } = DateTimeOffset.UtcNow;

        internal ITextChannel TextChannel => _textChannel;
        internal IVoiceChannel VoiceChannel => _voiceChannel;

        public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _audioClient = await _voiceChannel.ConnectAsync(_options.SelfDeaf, false);
            _audioClient.Disconnected += OnDisconnected;

            _outStream = _audioClient.CreatePCMStream(AudioApplication.Music);
            _playbackTask = Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);

            _logger.Info($"Connected to voice channel '{_voiceChannel.Name}' of guild {GuildId}");
        }

        #region Commands

        public async ValueTask PlayAsync(AudioTrackQueueItem item, bool enqueue = true, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);

            await _commandLock.WaitAsync(cancellationToken);

            try
            {
                if (enqueue || State == AudioPlayerState.NotPlaying)
                {
                    await _queue.AddAsync(item, cancellationToken);
                    return;
                }

                _replacementItem = item;
                Interrupt(PlaybackInterrupt.Replace);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async ValueTask SkipAsync(int count = 1, CancellationToken cancellationToken = default)
        {
            await _commandLock.WaitAsync(cancellationToken);

            try
            {
                if (count > 1)
                    await _queue.RemoveRangeAsync(0, count - 1, cancellationToken);

                _repeatItem = null;
                Interrupt(PlaybackInterrupt.Skip);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            await _commandLock.WaitAsync(cancellationToken);

            try
            {
                await _queue.ClearAsync(cancellationToken);

                _repeatItem = null;
                Interrupt(PlaybackInterrupt.Stop);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
        {
            await _commandLock.WaitAsync(cancellationToken);

            try
            {
                if (_paused) return;

                _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _paused = true;

                if (State == AudioPlayerState.Playing)
                    _state = (int)AudioPlayerState.Paused;
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
        {
            await _commandLock.WaitAsync(cancellationToken);

            try
            {
                if (!_paused) return;

                _paused = false;

                if (State == AudioPlayerState.Paused)
                    _state = (int)AudioPlayerState.Playing;

                _resumeSignal?.TrySetResult();
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async ValueTask SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        {
            await _commandLock.WaitAsync(cancellationToken);

            try
            {
                if (_currentItem == null) return;

                _seekPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
                Interrupt(PlaybackInterrupt.Seek);
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public ValueTask SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _volume = Math.Clamp(volume, 0.0f, 2.0f);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return DisposeAsync();
        }

        #endregion

        #region Playback

        private async Task RunAsync(CancellationToken lifetimeToken)
        {
            while (!lifetimeToken.IsCancellationRequested)
            {
                var item = TakeNext();

                if (item == null)
                {
                    _currentItem = null;
                    _segmentStart = TimeSpan.Zero;
                    Interlocked.Exchange(ref _segmentBytes, 0);

                    _state = (int)AudioPlayerState.NotPlaying;
                    InactiveSince ??= DateTimeOffset.UtcNow;

                    try { await _wakeup.WaitAsync(lifetimeToken); }
                    catch (OperationCanceledException) { break; }

                    continue;
                }

                InactiveSince = null;

                PlaybackOutcome outcome;

                try
                {
                    outcome = await PlayTrackAsync(item, lifetimeToken);
                }
                catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    outcome = PlaybackOutcome.Failed;
                    await _notifier.TrackExceptionAsync(_textChannel, item, e);
                }

                await ApplyRepeatAsync(item, outcome);
            }

            _state = (int)AudioPlayerState.Destroyed;
        }

        /// <summary>
        /// Picks the next item to play, honouring a pending replacement or a repeated track.
        /// </summary>
        private AudioTrackQueueItem TakeNext()
        {
            if (_replacementItem != null)
            {
                var replacement = _replacementItem;
                _replacementItem = null;

                return replacement;
            }

            if (_repeatItem != null)
            {
                var repeated = _repeatItem;
                _repeatItem = null;

                return repeated;
            }

            return _queue.TryDequeue(out var item) ? item : null;
        }

        private async ValueTask ApplyRepeatAsync(AudioTrackQueueItem item, PlaybackOutcome outcome)
        {
            // Only a track which ran to its end is worth repeating. Requiring that it also
            // produced audio keeps a source which yields nothing from looping at full speed.
            if (outcome != PlaybackOutcome.Completed || Position <= TimeSpan.Zero)
                return;

            switch (RepeatMode)
            {
                case AudioRepeatMode.Track:
                    _repeatItem = item;
                    break;

                case AudioRepeatMode.Queue:
                    await _queue.AddAsync(item);
                    break;
            }
        }

        /// <summary>
        /// Plays a track through to its end, an interruption, or a failure. A track is normally
        /// a single segment - seeking is what splits it into more than one.
        /// </summary>
        private async Task<PlaybackOutcome> PlayTrackAsync(AudioTrackQueueItem item, CancellationToken lifetimeToken)
        {
            _currentItem = item;

            var position = TimeSpan.Zero;
            var announce = true;

            while (true)
            {
                var outcome = await PlaySegmentAsync(item, position, announce, lifetimeToken);

                if (outcome != PlaybackOutcome.Seek)
                    return outcome;

                // The track is already on screen, so a seek only continues it
                position = _seekPosition;
                announce = false;
            }
        }

        /// <summary>
        /// Plays one segment: a single decoder run, from <paramref name="position"/> until the
        /// track ends or something interrupts it. A decoder cannot be told to seek once it is
        /// running, so a seek ends the segment and the caller starts the next one.
        /// </summary>
        private async Task<PlaybackOutcome> PlaySegmentAsync(
            AudioTrackQueueItem item,
            TimeSpan position,
            bool announce,
            CancellationToken lifetimeToken)
        {
            using var trackCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);

            _trackCts = trackCts;
            _interrupt = PlaybackInterrupt.None;

            var streamInfo = await ResolveAsync(item, trackCts.Token);

            await using var stream = FfmpegAudioStream.Start(
                _options.YtDlp, streamInfo, position, _logger, trackCts.Token);

            _segmentStart = position;
            Interlocked.Exchange(ref _segmentBytes, 0);
            _state = (int)(_paused ? AudioPlayerState.Paused : AudioPlayerState.Playing);

            if (announce)
                await _notifier.TrackStartedAsync(_textChannel, item, _paused, _volume);

            StartPrefetch(lifetimeToken);

            var outcome = await PumpAsync(stream, trackCts.Token, lifetimeToken);

            // Whatever is still buffered belongs to the track we are leaving behind
            if (outcome != PlaybackOutcome.Completed)
                await ClearBufferAsync();

            return outcome;
        }

        /// <summary>
        /// Moves decoded audio into the voice connection one Opus frame at a time, which keeps
        /// the reaction time to a skip or a pause down to a single frame.
        /// </summary>
        private async Task<PlaybackOutcome> PumpAsync(
            FfmpegAudioStream stream,
            CancellationToken trackToken,
            CancellationToken lifetimeToken)
        {
            var buffer = new byte[FfmpegAudioStream.FrameSize];

            try
            {
                while (true)
                {
                    if (_paused)
                    {
                        await _outStream.FlushAsync(trackToken);
                        await WaitForResumeAsync(trackToken);
                    }

                    var read = await stream.ReadAsync(buffer, trackToken);

                    if (read == 0)
                        break;

                    ApplyVolume(buffer.AsSpan(0, read), _volume);

                    await _outStream.WriteAsync(buffer.AsMemory(0, read), trackToken);
                    Interlocked.Exchange(ref _segmentBytes, stream.BytesRead);
                }

                await _outStream.FlushAsync(lifetimeToken);

                var exitCode = await stream.WaitForExitAsync(lifetimeToken);

                if (exitCode != 0)
                    throw new ProcessException(_options.YtDlp.FfmpegPath, exitCode, stream.GetErrorOutput());

                return PlaybackOutcome.Completed;
            }
            catch (OperationCanceledException) when (!lifetimeToken.IsCancellationRequested)
            {
                return _interrupt switch
                {
                    PlaybackInterrupt.Stop => PlaybackOutcome.Stopped,
                    PlaybackInterrupt.Seek => PlaybackOutcome.Seek,
                    PlaybackInterrupt.Replace => PlaybackOutcome.Replaced,
                    _ => PlaybackOutcome.Skipped
                };
            }
        }

        /// <summary>
        /// Drops the audio Discord has not sent out yet, so that a skip is heard at once
        /// instead of after the write buffer drains.
        /// </summary>
        private async ValueTask ClearBufferAsync()
        {
            try { await _outStream.ClearAsync(CancellationToken.None); }
            catch (Exception e) { _logger.Debug($"Failed to drop the buffered audio: {e.Message}"); }
        }

        private async Task WaitForResumeAsync(CancellationToken cancellationToken)
        {
            while (_paused)
            {
                var signal = _resumeSignal;

                if (signal == null)
                    return;

                await signal.Task.WaitAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Scales the samples in place. Discord has no server side volume, so gain has to be
        /// applied here - which also means it takes effect without restarting the decoder.
        /// </summary>
        private static void ApplyVolume(Span<byte> buffer, float volume)
        {
            if (Math.Abs(volume - 1.0f) < 0.001f)
                return;

            var samples = MemoryMarshal.Cast<byte, short>(buffer);

            for (var i = 0; i < samples.Length; i++)
                samples[i] = (short)Math.Clamp(samples[i] * volume, short.MinValue, short.MaxValue);
        }

        private async ValueTask<YtDlpStreamInfo> ResolveAsync(AudioTrackQueueItem item, CancellationToken cancellationToken)
        {
            var prefetched = _prefetched;

            if (prefetched != null && _prefetchedId == item.RequestId)
            {
                _prefetched = null;
                _prefetchedId = 0;

                try
                {
                    return await prefetched.WaitAsync(cancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.Debug($"Prefetched address of '{item.Track.Title}' was unusable, resolving again");
                }
            }

            return await _client.ResolveStreamAsync(item.Track, cancellationToken);
        }

        /// <summary>
        /// Resolving an address takes seconds, so the next one is fetched while the current
        /// track is still playing.
        /// </summary>
        private void StartPrefetch(CancellationToken lifetimeToken)
        {
            if (!_options.YtDlp.Prefetch)
                return;

            var next = _queue.Count > 0 ? _queue[0] : null;

            if (next?.Track == null || _prefetchedId == next.RequestId)
                return;

            _prefetchedId = next.RequestId;
            _prefetched = Task.Run(
                () => _client.ResolveStreamAsync(next.Track, lifetimeToken).AsTask(),
                CancellationToken.None);

            // A skip or a queue change can leave this prefetch with nobody to await it
            ProcessRunner.Observe(_prefetched);
        }

        private void Interrupt(PlaybackInterrupt interrupt)
        {
            _interrupt = interrupt;

            try { _trackCts?.Cancel(); }
            catch (ObjectDisposedException) { /* the track already ended on its own */ }

            Signal();
        }

        private void Signal()
        {
            try { _wakeup.Release(); }
            catch (SemaphoreFullException) { /* the loop is already awake */ }
            catch (ObjectDisposedException) { /* the player is going away */ }
        }

        #endregion

        private Task OnDisconnected(Exception exception)
        {
            if (exception != null)
                _logger.Warning($"Voice connection of guild {GuildId} dropped", exception);

            // Disposal awaits the playback loop, which cannot run on the gateway's callback
            _ = Task.Run(async () => await DisposeAsync(), CancellationToken.None);

            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await _commandLock.WaitAsync();

            try
            {
                if (_disposed) return;
                _disposed = true;
            }
            finally
            {
                _commandLock.Release();
            }

            _state = (int)AudioPlayerState.Destroyed;
            _queue.Enqueued -= Signal;

            if (_audioClient != null)
                _audioClient.Disconnected -= OnDisconnected;

            // Release the pump, then let the loop unwind before tearing anything down
            await _lifetime.CancelAsync();
            _resumeSignal?.TrySetResult();
            Signal();

            if (_playbackTask != null)
            {
                try { await _playbackTask.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { _logger.Warning($"Playback loop of guild {GuildId} did not stop in time"); }
                catch (Exception e) { _logger.Warning($"Playback loop of guild {GuildId} faulted while stopping", e); }
            }

            if (_outStream != null)
            {
                try { await _outStream.DisposeAsync(); }
                catch (Exception e) { _logger.Debug($"Failed to dispose the voice output stream: {e.Message}"); }
            }

            if (_audioClient != null)
            {
                try { await _audioClient.StopAsync(); }
                catch (Exception e) { _logger.Debug($"Failed to stop the voice client: {e.Message}"); }

                _audioClient.Dispose();
            }

            _lifetime.Dispose();
            _currentItem = null;

            if (_onDestroyed != null)
                await _onDestroyed(this);

            _logger.Info($"Disconnected from voice channel '{_voiceChannel.Name}' of guild {GuildId}");
        }
    }
}
