using Xunit;
using Moq;

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
            using var cache = new Cache<object, object>();

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
            using var cache = new Cache<object, object>();

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
            var a = new { Name = "Foo", Id = 1 };
            var b = new { Name = "Foo", Id = 1 };
            using var cache = new Cache<object, object>();

            // Act
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
            var a = new { Name = "Foo", Id = 1 };
            var b = new { Name = "Bar", Id = 2 };
            using var cache = new Cache<object, object>();

            // Act
            cache.Set(a, "A");
            cache.Set(b, "B");

            // Assert
            Assert.Equal(cache.Get(a), "A");
            Assert.Equal(cache.Get(b), "B");
        }

        [Fact]
        public async Task CallsDisposeOnEviction()
        {
            // Arrange
            var mock = new Mock<IDisposable>();
            var expiration = TimeSpan.FromTicks(1);
            using var cache = new Cache<int, IDisposable>(expiration, expiration);

            // Act
            cache.Set(0, mock.Object);
            await Task.Delay(1000);

            // Assert
            Assert.Null(cache.Get(0));
            mock.Verify(x => x.Dispose(), Times.AtLeastOnce);
        }

        [Fact]
        public void CallsDisposeOnRemove()
        {
            // Arrange
            var mock = new Mock<IDisposable>();
            using var cache = new Cache<int, IDisposable>();

            // Act
            cache.Set(0, mock.Object);
            cache.Remove(0);

            // Assert
            mock.Verify(x => x.Dispose(), Times.AtLeastOnce);
        }
    }
}