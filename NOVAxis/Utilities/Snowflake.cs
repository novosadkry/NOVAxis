using System;
using System.Threading;

using Discord;

namespace NOVAxis.Utilities
{
    /// <summary>
    /// Hands out snowflake identifiers of our own. A real one owes its uniqueness to the
    /// counter in its low bits, which <see cref="SnowflakeUtils.ToSnowflake"/> leaves
    /// empty - two of those made within the same millisecond come out equal.
    /// </summary>
    public static class Snowflake
    {
        /// <summary>
        /// The worker, process and increment fields of a snowflake, which are ours to fill.
        /// </summary>
        private const ulong SequenceMask = (1UL << 22) - 1;

        private static ulong _sequence;

        public static ulong Next()
        {
            var sequence = Interlocked.Increment(ref _sequence) & SequenceMask;

            return SnowflakeUtils.ToSnowflake(DateTimeOffset.UtcNow) | sequence;
        }
    }
}
