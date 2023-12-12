using Moq;
using Xunit;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

using NOVAxis.Core;
using NOVAxis.Utilities;

namespace NOVAxisTests.Utilities
{
    public class CacheTests
    {
        public static IEnumerable<object[]> SampleEntries()
        {
            yield return new object[] { "0", "hello" };
            yield return new object[] { true, 268477761354885487L };
            yield return new object[] { new { Name = "Foo", Id = 1 }, new[] { 'A', 'B' } };
        }

        [Theory]
        [MemberData(nameof(SampleEntries))]
        public void SetAndGet(object key, object value)
        {
            // Arrange
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cacheOptions = Options.Create(new CacheOptions());
            var cache = new Cache<object, object>("Mock", memoryCache, cacheOptions);

            // Act
            cache.Set(key, value);

            // Assert
            Assert.Equal(cache.Get(key), value);
            Assert.NotEqual(cache.Get("invalid"), value);
        }

        [Theory]
        [MemberData(nameof(SampleEntries))]
        public void Remove(object key, object value)
        {
            // Arrange
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cacheOptions = Options.Create(new CacheOptions());
            var cache = new Cache<object, object>("Mock", memoryCache, cacheOptions);

            // Act
            cache.Set(key, value);
            cache.Remove(key);

            // Assert
            Assert.NotEqual(cache.Get(key), value);
            Assert.Null(cache.Get(key));
        }

        [Fact]
        public void ReplacesDuplicateKeys()
        {
            // Arrange
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cacheOptions = Options.Create(new CacheOptions());
            var cache = new Cache<object, object>("Mock", memoryCache, cacheOptions);

            // Act
            var a = new { Name = "Foo", Id = 1 };
            var b = new { Name = "Foo", Id = 1 };
            cache.Set(a, "A");
            cache.Set(b, "B");

            // Assert
            Assert.Equal(cache.Get(a), "B");
            Assert.Equal(cache.Get(b), "B");
        }

        [Fact]
        public void CanHandleObjectsAsKeys()
        {
            // Arrange
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var cacheOptions = Options.Create(new CacheOptions());
            var cache = new Cache<object, object>("Mock", memoryCache, cacheOptions);

            // Act
            var a = new { Name = "Foo", Id = 1 };
            var b = new { Name = "Bar", Id = 2 };
            cache.Set(a, "A");
            cache.Set(b, "B");

            // Assert
            Assert.Equal(cache.Get(a), "A");
            Assert.Equal(cache.Get(b), "B");
        }
    }
}
