using Assert = Xunit.Assert;

namespace EFCoreSecondLevelCacheInterceptor.UnitTests;

public class LockProviderTests
{
    [Fact]
    public void Dispose_DisposesLockProvider()
    {
        using (var lockProvider = new LockProvider())
        {
            // Assert
            Assert.True(condition: true); // If no exception is thrown, the test passes
        }
    }

    [Fact]
    public void LockWrite_WithKeys_ReturnsNonNullReleaser()
    {
        // Arrange
        using var lockProvider = new LockProvider();

        // Act
        using var releaser = lockProvider.LockWrite(new[] { "Products", "Users" });

        // Assert
        Assert.IsAssignableFrom<IDisposable>(releaser);
    }

    [Fact]
    public async Task LockWriteAsync_WithKeys_ReturnsNonNullReleaser()
    {
        // Arrange
        using var lockProvider = new LockProvider();

        // Act
        var releaser = await lockProvider.LockWriteAsync(new[] { "Products", "Users" });

        // Assert
        Assert.IsAssignableFrom<IDisposable>(releaser);
    }

    [Fact]
    public void LockWrite_WithKeys_CanBeDisposed()
    {
        // Arrange
        using var lockProvider = new LockProvider();
        var releaser = lockProvider.LockWrite(new[] { "Products", "Users" });

        // Act
        releaser?.Dispose();

        // Assert
        Assert.True(condition: true);
    }

    [Fact]
    public async Task LockWriteAsync_WithKeys_CanBeDisposed()
    {
        // Arrange
        using var lockProvider = new LockProvider();
        var releaser = await lockProvider.LockWriteAsync(new[] { "Products", "Users" });

        // Act
        releaser?.Dispose();

        // Assert
        Assert.True(condition: true);
    }

    [Fact]
    public void LockRead_WithKeys_ReturnsNonNullReleaser()
    {
        // Arrange
        using var lockProvider = new LockProvider();

        // Act
        using var releaser = lockProvider.LockRead(new[] { "Products", "Users" });

        // Assert
        Assert.IsAssignableFrom<IDisposable>(releaser);
    }

    [Fact]
    public async Task LockReadAsync_WithKeys_ReturnsNonNullReleaser()
    {
        // Arrange
        using var lockProvider = new LockProvider();

        // Act
        var releaser = await lockProvider.LockReadAsync(new[] { "Products", "Users" });

        // Assert
        Assert.IsAssignableFrom<IDisposable>(releaser);
    }

    [Fact]
    public async Task LockRead_AllowsConcurrentReaders_OnSameKey()
    {
        // Arrange
        using var lockProvider = new LockProvider();
        var keys = new[] { "Products" };

        // Act - both read locks should be acquirable without blocking each other
        var releaser1 = await lockProvider.LockReadAsync(keys);
        var releaser2 = await lockProvider.LockReadAsync(keys);

        // Assert - both releasers were granted
        Assert.NotNull(releaser1);
        Assert.NotNull(releaser2);

        releaser1?.Dispose();
        releaser2?.Dispose();
    }

    [Fact]
    public async Task LockRead_DoesNotBlock_ConcurrentReadersOnDifferentKeys()
    {
        // Arrange
        using var lockProvider = new LockProvider();

        // Act
        var releaserA = await lockProvider.LockReadAsync(new[] { "Products" });
        var releaserB = await lockProvider.LockReadAsync(new[] { "Users" });

        // Assert
        Assert.NotNull(releaserA);
        Assert.NotNull(releaserB);

        releaserA?.Dispose();
        releaserB?.Dispose();
    }
}
