using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

using NOVAxis.Core;
using NOVAxis.Preconditions;
using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;

using Discord;

namespace NOVAxis.Utilities
{
    public class InteractionCache : Cache<ulong, object>
    {
        public InteractionCache(IMemoryCache cache, IOptions<CacheOptions> options)
            : base(nameof(InteractionCache), cache, options) { }

        public ulong Store(object value)
        {
            var snowflake = Snowflake.Next();
            Set(snowflake, value);
            return snowflake;
        }
    }

    /// <summary>
    /// Keyed by the id of a user, as Discord.Net hands out more than one object
    /// for the same person.
    /// </summary>
    public class CooldownCache : Cache<ulong, CooldownInfo>
    {
        public CooldownCache(IMemoryCache cache, IOptions<CacheOptions> options)
            : base(nameof(CooldownCache), cache, options) { }
    }

    /// <summary>
    /// Tracks already looked up, kept under the input which found them. What a lookup
    /// returns does not change: the address of a stream is resolved for every playback.
    /// </summary>
    public class AudioSearchCache : Cache<string, AudioLoadResult>
    {
        public AudioSearchCache(IMemoryCache cache, IOptions<CacheOptions> options)
            : base(nameof(AudioSearchCache), cache, options) { }
    }

    /// <summary>
    /// What a link turned out to be, kept under its address. Two requests for one download -
    /// the format list, then the download itself - would otherwise each pay for an
    /// extraction, and what a link resolves to does not change between them.
    ///
    /// Holding this does not let anything past the network guard: what is kept here is the
    /// titling and the formats on offer, and every byte is still fetched through the guard
    /// when the download runs. A name which resolved publicly when it was looked up and
    /// privately by the time it is fetched is refused at the fetch, cache or no cache.
    /// </summary>
    public class DownloadProbeCache : Cache<string, YtDlpMediaInfo>
    {
        public DownloadProbeCache(IMemoryCache cache, IOptions<CacheOptions> options)
            : base(nameof(DownloadProbeCache), cache, options) { }
    }

    public class Cache<TKey, TValue>
    {
        private readonly string _prefix;
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _entryOptions;

        public Cache(string prefix, IMemoryCache cache, IOptions<CacheOptions> options)
        {
            _prefix = prefix;
            _cache = cache;

            _entryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.Value.AbsoluteExpiration,
                SlidingExpiration = options.Value.RelativeExpiration
            };

            _entryOptions.RegisterPostEvictionCallback((_, value, _, _) =>
            {
                if (value is IDisposable context)
                    context.Dispose();
            });
        }

        public TValue this[TKey key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        public TValue GetOrAdd(TKey key, TValue value)
        {
            return GetOrAdd(key, entry =>
            {
                entry.SetOptions(_entryOptions);
                return value;
            });
        }

        public void Set(TKey key, TValue value)
        {
            Set(key, value, _entryOptions);
        }

        public TValue Get(TKey key)
        {
            return _cache.Get<TValue>(GetIndex(key));
        }

        public bool TryGetValue(TKey key, out TValue result)
        {
            return _cache.TryGetValue(GetIndex(key), out result);
        }

        public void Remove(TKey key)
        {
            _cache.Remove(GetIndex(key));
        }

        private TValue GetOrAdd(TKey key, Func<ICacheEntry, TValue> valueFactory)
        {
            return _cache.GetOrCreate(GetIndex(key), valueFactory);
        }

        private void Set(TKey key, TValue value, MemoryCacheEntryOptions entryOptions)
        {
            _cache.Set(GetIndex(key), value, entryOptions);
        }

        /// <summary>
        /// Builds the index out of the key itself. A hash code would let two keys which
        /// happen to share one read each other's entry.
        /// </summary>
        private string GetIndex(TKey key)
        {
            return $"{_prefix}:{key}";
        }
    }
}
