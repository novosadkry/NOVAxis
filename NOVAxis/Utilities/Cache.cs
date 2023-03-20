using System;

using Microsoft.Extensions.Caching.Memory;

namespace NOVAxis.Utilities
{
    public class Cache<TKey, TValue> : IDisposable
    {
        private bool _disposed;
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

        public Cache(MemoryCacheOptions options, MemoryCacheEntryOptions entryOptions)
        {
            _cache = new MemoryCache(options);
            _entryOptions = entryOptions;
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
