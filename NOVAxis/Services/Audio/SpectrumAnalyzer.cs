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
        /// Points per transform. Must be a power of two, and must cover the gap between two
        /// pushes or audio falls between the windows unanalysed. At 48 kHz and thirty frames
        /// a second that gap is 1600 samples, so 2048 is the first size that fits - 43 ms,
        /// and 23 Hz per bin, which is what makes the bottom two octaves more than one smear.
        /// </summary>
        public const int FftSize = 2048;

        /// <summary>How many bars the spectrum is folded into.</summary>
        private const int Bands = 48;

        /// <summary>Lowest frequency given a band of its own.</summary>
        private const float MinHz = 40f;

        /// <summary>
        /// Highest. Deliberately short of Nyquist: what yt-dlp fetches is usually Opus or
        /// AAC around 128 kbps, which is filtered off near 16 kHz, and bands above that
        /// would sit at the floor for ever and read as broken bars.
        /// </summary>
        private const float MaxHz = 16000f;

        /// <summary>
        /// Quietest level worth drawing, in dB relative to full scale. Anything below reads
        /// as nothing: without a floor, the noise between notes fills the bottom of every bar.
        /// </summary>
        private const float FloorDb = -70f;

        /// <summary>
        /// Loudest. Above full scale on purpose: what is drawn is not a level any more but
        /// a level with its movement stretched by <see cref="Expand"/>, and a loud band
        /// overshoots. Headroom here is what a kick has to grow into - without it the bass
        /// simply sits against the top and the beat cannot be seen at all.
        /// </summary>
        private const float CeilingDb = 6f;

        /// <summary>
        /// Lift applied per octave. Music rolls off with frequency, so without this the
        /// treble looks dead and everything piles into the bass. Three is the value that
        /// renders pink noise flat, which is the usual reference for a music visualiser.
        /// </summary>
        private const float TiltDbPerOctave = 3f;

        /// <summary>
        /// How far each band's movement is exaggerated around its own recent average.
        ///
        /// A true reading looks dead, and no choice of floor and ceiling fixes it: over a
        /// bass line that never stops, a kick is perhaps six decibels louder, and six of
        /// the sixty on show is a bar twitching by a tenth of its height. Stretching what
        /// moves while leaving what does not where it is keeps the shape of the spectrum
        /// honest and lets the beat actually read.
        /// </summary>
        private const float Expand = 3.2f;

        /// <summary>
        /// How long a band takes to accept a new level as its normal. Long enough not to
        /// swallow the beat it is there to reveal, short enough to follow a track change.
        /// </summary>
        private const float AdaptSeconds = 1.5f;

        /// <summary>
        /// Shapes the height once the level has been scaled. Above one it pushes the middle
        /// of the range down, which leaves the loudest moments standing further clear of
        /// everything around them - at the price of the quiet ones, which is the whole
        /// trade: past about 1.5 the beat is unmissable and a sustained pad has all but
        /// gone. Measured against real material, this is where both still read.
        /// </summary>
        private const float Gamma = 1.3f;

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
                _taps.TryAdd(guildId, new Tap(Options.Fps));

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
            private readonly byte[] _ring;
            private readonly float[] _window;
            private readonly float[] _real;
            private readonly float[] _imaginary;
            private readonly byte[] _bands;
            private readonly int[] _edges;
            private readonly float[] _tilt;

            /// <summary>
            /// Each band's own idea of normal, in dB, which is what its movement is
            /// measured against. Null until the first reading gives it somewhere to start -
            /// beginning at zero would make the opening second one enormous spike.
            /// </summary>
            private float[] _normal;

            private readonly float _adapt;

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

            public Tap(int fps)
            {
                // Room for several windows, so the writer needs a good fraction of a second
                // to lap a reader which copies in microseconds
                _ring = new byte[FftSize * FfmpegBytesPerSample * 8];
                _window = Fft.Hann(FftSize);
                _real = new float[FftSize];
                _imaginary = new float[FftSize];
                _bands = new byte[Bands];
                _edges = Edges();
                _tilt = Tilt(_edges);

                var gain = 0f;

                foreach (var point in _window)
                    gain += point;

                // Two, because a real signal splits its energy between the positive and
                // negative halves of the spectrum and only one half is read back
                _scale = 2f / gain;

                // One step of an exponential average, sized so a band forgets an old level
                // over roughly AdaptSeconds - which depends on how often it is read
                var steps = AdaptSeconds * Math.Clamp(fps, 1, 60);
                _adapt = 1f - MathF.Exp(-1f / steps);
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

                const float span = CeilingDb - FloorDb;

                var first = _normal == null;

                if (first)
                    _normal = new float[_bands.Length];

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

                    if (first)
                        _normal[band] = db;
                    else
                        _normal[band] += (db - _normal[band]) * _adapt;

                    // Where the band usually sits stays put; how far it has moved from
                    // there is what gets stretched
                    var shown = _normal[band] + (db - _normal[band]) * Expand;
                    var scaled = MathF.Pow(Math.Clamp((shown - FloorDb) / span, 0f, 1f), Gamma);

                    _bands[band] = (byte)Math.Clamp(scaled * 255f, 0f, 255f);
                }

                return (byte[])_bands.Clone();
            }

            /// <summary>
            /// Where each band starts, spaced logarithmically between the two ends:
            /// pitch is a ratio, so linear bins would spend most of the width on the top
            /// octave and squeeze everything anyone hums into the first two bars.
            /// </summary>
            private static int[] Edges()
            {
                const int usable = FftSize / 2;
                const float perBin = (float)FfmpegAudioStream.SampleRate / FftSize;

                var lowest = Math.Max(MinHz / perBin, 1f);
                var highest = Math.Min(MaxHz / perBin, usable - 1f);

                var edges = new int[Bands + 1];

                for (var i = 0; i <= Bands; i++)
                {
                    var ratio = (float)i / Bands;
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
            private static float[] Tilt(int[] edges)
            {
                const float perBin = (float)FfmpegAudioStream.SampleRate / FftSize;
                var tilt = new float[edges.Length - 1];

                for (var band = 0; band < tilt.Length; band++)
                {
                    var centre = (edges[band] + edges[band + 1]) / 2f * perBin;
                    var octaves = MathF.Log2(MathF.Max(centre, 1f) / 1000f);

                    tilt[band] = octaves * TiltDbPerOctave;
                }

                return tilt;
            }
        }
    }
}
