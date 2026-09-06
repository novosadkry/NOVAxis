using System;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// An in-place radix-2 FFT, and the window to feed it through.
    ///
    /// Hand rolled rather than taken from a package: a thousand points thirty times a
    /// second is microseconds of work, and the smallest numerics library that could do it
    /// is a larger cost than the eighty lines below.
    /// </summary>
    public static class Fft
    {
        /// <summary>
        /// A Hann window of <paramref name="length"/> points. Cut a signal into
        /// rectangular blocks and every block's edges look like a step change to the
        /// transform, which smears energy across the whole spectrum; tapering the ends to
        /// zero is what stops a pure tone reading as a wall.
        /// </summary>
        public static float[] Hann(int length)
        {
            var window = new float[length];

            for (var i = 0; i < length; i++)
                window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (length - 1)));

            return window;
        }

        /// <summary>
        /// Transforms <paramref name="real"/> and <paramref name="imaginary"/> in place.
        /// Both must be the same power-of-two length.
        /// </summary>
        public static void Transform(Span<float> real, Span<float> imaginary)
        {
            var n = real.Length;

            if (n != imaginary.Length || n < 2 || (n & (n - 1)) != 0)
                throw new ArgumentException("FFT needs two spans of the same power-of-two length");

            // Decimation in time: the butterflies below expect their input in bit reversed
            // order, so the permutation happens first rather than being threaded through
            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;

                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;

                j ^= bit;

                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
                }
            }

            for (var length = 2; length <= n; length <<= 1)
            {
                var angle = -2f * MathF.PI / length;
                var stepReal = MathF.Cos(angle);
                var stepImaginary = MathF.Sin(angle);

                for (var start = 0; start < n; start += length)
                {
                    float twiddleReal = 1f, twiddleImaginary = 0f;

                    for (var k = 0; k < length / 2; k++)
                    {
                        var a = start + k;
                        var b = a + length / 2;

                        var oddReal = real[b] * twiddleReal - imaginary[b] * twiddleImaginary;
                        var oddImaginary = real[b] * twiddleImaginary + imaginary[b] * twiddleReal;

                        real[b] = real[a] - oddReal;
                        imaginary[b] = imaginary[a] - oddImaginary;
                        real[a] += oddReal;
                        imaginary[a] += oddImaginary;

                        var nextReal = twiddleReal * stepReal - twiddleImaginary * stepImaginary;
                        twiddleImaginary = twiddleReal * stepImaginary + twiddleImaginary * stepReal;
                        twiddleReal = nextReal;
                    }
                }
            }
        }
    }
}
