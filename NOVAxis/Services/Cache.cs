using Microsoft.Extensions.Caching.Memory;

namespace NOVAxis.Services
{
    class Cache<K, V>
    {
        private readonly MemoryCache _cache;

        public Cache()
        {
            _cache = new MemoryCache(new MemoryCacheOptions());
        }

        public V this[K key]
        {
            get => Get(key);
            set => Set(key, value);
        }

        public V Get(K key)
        {
            return _cache.Get<V>(key.GetHashCode().ToString());
        }

        public void Set(K key, V value)
        {
            _cache.Set(key.GetHashCode().ToString(), value);
        }
    }
}
