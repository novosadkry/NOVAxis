using System;

using Microsoft.Extensions.Caching.Memory;

namespace NOVAxis.Utilities
{
    public class Cache<TKey, TValue>
    {
        private readonly MemoryCache _cache;
        private readonly MemoryCacheEntryOptions _entryOptions; 

        public Cache() 
            : this(new MemoryCacheOptions(), new MemoryCacheEntryOptions()) { }

        public Cache(TimeSpan? absolute, TimeSpan? sliding)
            : this(new MemoryCacheOptions(), new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absolute,
                SlidingExpiration = sliding
            })
        { }

        public Cache(TimeSpan? absolute, TimeSpan? sliding, PostEvictionDelegate postEvictionCallback)
            : this(new MemoryCacheOptions(), new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absolute,
                SlidingExpiration = sliding,
            }.RegisterPostEvictionCallback(postEvictionCallback)) 
        { }

        public Cache(MemoryCacheOptions options, MemoryCacheEntryOptions entryOptions)
        {
            _cache = new MemoryCache(options);
            _entryOptions = entryOptions;
        }

        public TValue this[TKey key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        public TValue Get(TKey key)
        {
            return _cache.Get<TValue>(key.GetHashCode().ToString());
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
            _cache.Set(key.GetHashCode().ToString(), value, entryOptions);
        }

        public bool TryGetValue(TKey key, out TValue result)
        {
            return _cache.TryGetValue(key, out result);
        }

        public void Remove(TKey key)
        {
            _cache.Remove(key);
        }
    }
}
