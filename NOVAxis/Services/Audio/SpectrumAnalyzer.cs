using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Services.Audio.YtDlp;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// Turns the PCM a player is about to send into a handful of loudness bands, for
    /// anything that wants to draw what is being heard.
    ///
    /// The web player has no audio of its own - it is a remote control - so a spectrum
    /// has to be computed here and pushed. Only guilds somebody is actually watching are
    /// tapped: <see cref="KeepOnly"/> decides which, and a guild without a tap costs the
    /// audio thread one dictionary lookup per frame and nothing else.
    /// </summary>
    public class SpectrumAnalyzer
    {
        /// <summary>
        /// Beyond this a tap is treated as silent. Comfortably longer than the gap between
        /// frames, short enough that a paused player stops sending within a tick or two.
        /// </summary>
        private static readonly TimeSpan Stale = TimeSpan.FromMilliseconds(250);

        private readonly ConcurrentDictionary<ulong, Tap> _taps = new();

        private AudioSpectrumOptions Options { get; }

        public SpectrumAnalyzer(IOptions<AudioOptions> options)
        {
            Options = options.Value.Spectrum;
        }

        public bool Active => Options.Active;

        /// <summary>
        /// Called from the playback loop, once per 20 ms frame. This is the audio hot path:
        /// a frame which arrives late is heard as a gap, so this does one copy and returns.
        /// Everything else - summing, windowing, the transform - is the reader's work.
        /// </summary>
        public void Write(ulong guildId, ReadOnlySpan<byte> pcm)
        {
            if (_taps.TryGetValue(guildId, out var tap))
                tap.Write(pcm);
        }

        /// <summary>
        /// The newest window as one byte per band, or null where there is nothing to draw:
        /// no tap, or nothing written recently. Silence is deliberately nothing rather than
        /// a flat line, so a paused player costs no traffic at all.
        /// </summary>
        public byte[] Read(ulong guildId)
        {
            return _taps.TryGetValue(guildId, out var tap) ? tap.Read() : null;
        }

        /// <summary>
        /// Points the analyzer at exactly these guilds, opening taps for the new ones and
        /// dropping the rest. Driven from whoever is watching, on a tick, rather than from
        /// each place a watcher comes and goes - one call site, and a viewer whose browser
        /// vanished without saying so is cleaned up on the next pass anyway.
        /// </summary>
        public void KeepOnly(IReadOnlyCollection<ulong> guildIds)
        {
            if (!Active)
                return;

            var wanted = guildIds as ISet<ulong> ?? guildIds.ToHashSet();

            foreach (var guildId in wanted)
                _taps.TryAdd(guildId, new Tap(Options));

            foreach (var open in _taps.Keys)
            {
                if (!wanted.Contains(open))
                    _taps.TryRemove(open, out _);
            }
        }

        /// <summary>
        /// One guild's rolling window of raw audio, plus the arithmetic to read it back as
        /// bands. Written by that guild's playback loop and read by the pusher, and by
        /// nobody else - which is what makes the lock free handover below sound.
        /// </summary>
        private sealed class Tap
        {
            private readonly AudioSpectrumOptions _options;

            private readonly byte[] _ring;
            private readonly float[] _window;
            private readonly float[] _real;
            private readonly float[] _imaginary;
            private readonly byte[] _bands;
            private readonly int[] _edges;
            private readonly float[] _tilt;

            /// <summary>
            /// Turns a bin's magnitude into the amplitude of the tone that produced it. Without
            /// it the numbers scale with the transform size and the window, so a dB floor
            /// would mean nothing and every band would sit clamped at the top.
            /// </summary>
            private readonly float _scale;

            /// <summary>
            /// Total bytes ever written. Published on every write and read before and after
            /// the reader copies, which is how the reader can tell whether the writer came
            /// round far enough to have overwritten what it was reading.
            /// </summary>
            private long _written;

            private long _writtenAt;

            public Tap(AudioSpectrumOptions options)
            {
                _options = options;

                var samples = options.FftSize;

                // Room for several windows, so the writer needs a good fraction of a second
                // to lap a reader which copies in microseconds
                _ring = new byte[samples * FfmpegBytesPerSample * 8];
                _window = Fft.Hann(samples);
                _real = new float[samples];
                _imaginary = new float[samples];
                _bands = new byte[options.Bands];
                _edges = Edges(options.Bands, samples, options);
                _tilt = Tilt(_edges, samples, options);

                var gain = 0f;

                foreach (var point in _window)
                    gain += point;

                // Two, because a real signal splits its energy between the positive and
                // negative halves of the spectrum and only one half is read back
                _scale = 2f / gain;
            }

            /// <summary>Stereo 16 bit: four bytes carry one moment of sound.</summary>
            private const int FfmpegBytesPerSample = 4;

            public void Write(ReadOnlySpan<byte> pcm)
            {
                // The last read of a segment is whatever ffmpeg had left, which need not be
                // a whole number of stereo samples. Letting a stray byte through would put
                // every frame after it half a sample out and swap the channels for good
                pcm = pcm[..(pcm.Length / FfmpegBytesPerSample * FfmpegBytesPerSample)];

                if (pcm.IsEmpty)
                    return;

                var position = (int)(Volatile.Read(ref _written) % _ring.Length);

                // The ring is a whole number of frames long, so a frame either fits or
                // wraps exactly once
                var first = Math.Min(pcm.Length, _ring.Length - position);

                pcm[..first].CopyTo(_ring.AsSpan(position));

                if (first < pcm.Length)
                    pcm[first..].CopyTo(_ring.AsSpan(0));

                Volatile.Write(ref _writtenAt, DateTime.UtcNow.Ticks);
                Volatile.Write(ref _written, Volatile.Read(ref _written) + pcm.Length);
            }

            public byte[] Read()
            {
                var age = DateTime.UtcNow.Ticks - Volatile.Read(ref _writtenAt);

                if (age > Stale.Ticks)
                    return null;

                // Two attempts: if the writer laps us mid copy the samples are a mixture of
                // two moments, which reads as a burst of noise. Rare enough that giving up
                // after one retry costs a single frame of animation
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var before = Volatile.Read(ref _written);

                    if (before < _real.Length * FfmpegBytesPerSample)
                        return null;

                    Sample(before);

                    var after = Volatile.Read(ref _written);

                    // Untouched as long as the writer has not come all the way round to
                    // where our window started
                    if (after - before < _ring.Length - _real.Length * FfmpegBytesPerSample)
                        return Fold();
                }

                return null;
            }

            /// <summary>
            /// Fills the transform's input with the newest window: the two channels summed
            /// to one, scaled to ±1, and tapered by the window function.
            /// </summary>
            private void Sample(long written)
            {
                var samples = _real.Length;
                var start = (int)((written - (long)samples * FfmpegBytesPerSample) % _ring.Length);

                for (var i = 0; i < samples; i++)
                {
                    var at = (start + i * FfmpegBytesPerSample) % _ring.Length;

                    var left = MemoryMarshal.Read<short>(_ring.AsSpan(at, 2));
                    var right = MemoryMarshal.Read<short>(_ring.AsSpan(at + 2, 2));

                    _real[i] = (left + right) / 65536f * _window[i];
                    _imaginary[i] = 0f;
                }
            }

            private byte[] Fold()
            {
                Fft.Transform(_real, _imaginary);

                var floor = _options.FloorDb;
                var span = Math.Max(_options.CeilingDb - floor, 1f);

                for (var band = 0; band < _bands.Length; band++)
                {
                    var from = _edges[band];
                    var to = _edges[band + 1];
                    var total = 0f;

                    // The mean, not the sum: log spaced bands get wider as they go up, and
                    // summing would hand the treble a loudness it has not got
                    for (var bin = from; bin < to; bin++)
                        total += _real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin];

                    var power = total / (to - from) * _scale * _scale;

                    // Power, so ten log ten - the same as twenty log of the magnitude
                    var db = 10f * MathF.Log10(power + 1e-12f) + _tilt[band];
                    var scaled = (db - floor) / span;

                    _bands[band] = (byte)Math.Clamp(scaled * 255f, 0f, 255f);
                }

                return (byte[])_bands.Clone();
            }

            /// <summary>
            /// Where each band starts, spaced logarithmically between the configured ends:
            /// pitch is a ratio, so linear bins would spend most of the width on the top
            /// octave and squeeze everything anyone hums into the first two bars.
            /// </summary>
            private static int[] Edges(int bands, int samples, AudioSpectrumOptions options)
            {
                var usable = samples / 2;
                var perBin = (float)FfmpegAudioStream.SampleRate / samples;

                var lowest = Math.Max(options.MinHz / perBin, 1f);
                var highest = Math.Min(options.MaxHz / perBin, usable - 1f);

                var edges = new int[bands + 1];

                for (var i = 0; i <= bands; i++)
                {
                    var ratio = (float)i / bands;
                    var bin = (int)MathF.Round(lowest * MathF.Pow(highest / lowest, ratio));

                    // Logarithms crowd the bottom: several of the lowest bands want the same
                    // bin, and neighbours moving together reads as a wide bass blob, which is
                    // what a visualiser should look like. A band with no bins at all is a bar
                    // that never moves, which does not
                    edges[i] = Math.Min(Math.Max(bin, i == 0 ? 1 : edges[i - 1] + 1), usable);
                }

                return edges;
            }

            /// <summary>
            /// How much each band is lifted, in dB. Recorded music rolls off with frequency,
            /// so a true reading leaves the treble looking dead; the usual reference is the
            /// tilt which renders pink noise flat.
            /// </summary>
            private static float[] Tilt(int[] edges, int samples, AudioSpectrumOptions options)
            {
                var perBin = (float)FfmpegAudioStream.SampleRate / samples;
                var tilt = new float[edges.Length - 1];

                for (var band = 0; band < tilt.Length; band++)
                {
                    var centre = (edges[band] + edges[band + 1]) / 2f * perBin;
                    var octaves = MathF.Log2(MathF.Max(centre, 1f) / 1000f);

                    tilt[band] = octaves * options.TiltDbPerOctave;
                }

                return tilt;
            }
        }
    }
}
