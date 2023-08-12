using System;

using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Caching.Memory;

using Discord;

namespace NOVAxis.Utilities
{
    public class CacheOptions
    {
        public CacheOptions() { }

        public CacheOptions(TimeSpan? absoluteExpiration, TimeSpan? relativeExpiration)
        {
            AbsoluteExpiration = absoluteExpiration;
            RelativeExpiration = relativeExpiration;
        }

        public ISystemClock Clock { get; init; }
        public TimeSpan? AbsoluteExpiration { get; init; }
        public TimeSpan? RelativeExpiration { get; init; }
    }

    public class InteractionCache : Cache<ulong, object>
    {
        public InteractionCache()
            : base(new CacheOptions()) { }

        public InteractionCache(TimeSpan? absolute, TimeSpan? relative)
            : base(new CacheOptions(absolute, relative)) { }

        public InteractionCache(CacheOptions options)
            : base(options) { }

        public ulong Store(object value)
        {
            var snowflake = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now);
            Set(snowflake, value);
            return snowflake;
        }
    }

    public class Cache<TKey, TValue> : IDisposable
    {
        private bool _disposed;
        private readonly MemoryCache _cache;
        private readonly MemoryCacheEntryOptions _entryOptions;

        public Cache() 
            : this(new CacheOptions()) { }

        public Cache(TimeSpan? absolute, TimeSpan? relative)
            : this(new CacheOptions(absolute, relative)) { }

        public Cache(CacheOptions options)
        {
            _cache = new MemoryCache(new MemoryCacheOptions { Clock = options.Clock });
            _entryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.AbsoluteExpiration,
                SlidingExpiration = options.RelativeExpiration
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

        public TValue Get(TKey key)
        {
            return _cache.Get<TValue>(key);
        }

        public TValue GetOrAdd(TKey key, TValue value)
        {
            return GetOrAdd(key, entry =>
            {
                entry.SetOptions(_entryOptions);
                return value;
            });
        }

        public TValue GetOrAdd(TKey key, Func<ICacheEntry, TValue> valueFactory)
        {
            return _cache.GetOrCreate(key, valueFactory);
        }

        public void Set(TKey key, TValue value)
        {
            Set(key, value, _entryOptions);
        }

        public void Set(TKey key, TValue value, MemoryCacheEntryOptions entryOptions)
        {
            _cache.Set(key, value, entryOptions);
        }

        public bool TryGetValue(TKey key, out TValue result)
        {
            return _cache.TryGetValue(key, out result);
        }

        public void Remove(TKey key)
        {
            _cache.Remove(key);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
                _cache.Dispose();

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~Cache()
        {
            Dispose(false);
        }
    }
}
