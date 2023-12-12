using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

using NOVAxis.Core;
using NOVAxis.Preconditions;

using Discord;

namespace NOVAxis.Utilities
{
    public class InteractionCache : Cache<ulong, object>
    {
        public InteractionCache(IMemoryCache cache, IOptions<CacheOptions> options)
            : base(nameof(InteractionCache), cache, options) { }

        public ulong Store(object value)
        {
            var snowflake = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now);
            Set(snowflake, value);
            return snowflake;
        }
    }

    public class CooldownCache : Cache<IUser, CooldownInfo>
    {
        public CooldownCache(IMemoryCache cache, IOptions<CacheOptions> options)
            : base(nameof(CooldownCache), cache, options) { }
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

        private string GetIndex(TKey key)
        {
            return _prefix + key.GetHashCode();
        }
    }
}
