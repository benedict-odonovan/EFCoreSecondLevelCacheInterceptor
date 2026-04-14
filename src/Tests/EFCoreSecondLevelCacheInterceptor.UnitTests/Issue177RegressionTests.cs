using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Assert = Xunit.Assert;

namespace EFCoreSecondLevelCacheInterceptor.UnitTests;

/// <summary>
///     Regression tests for https://github.com/VahidN/EFCoreSecondLevelCacheInterceptor/issues/177
///     Race condition: a SELECT query that started before an UPDATE completes after the UPDATE has
///     already invalidated the cache. The SELECT then writes its stale result back to the cache,
///     so subsequent reads return pre-UPDATE data.
/// </summary>
public class Issue177RegressionTests
{
    /// <summary>
    ///     Demonstrates that the raw <see cref="IEFCacheServiceProvider"/> has no built-in
    ///     protection: InsertValue always succeeds regardless of prior invalidation.
    ///     The fix lives one layer up in <see cref="DbCommandInterceptorProcessor"/>.
    /// </summary>
    [Fact]
    public void Issue177_RawCacheService_AcceptsInsertAfterInvalidation()
    {
        var cacheService = CreateCacheServiceProvider();

        var cachePolicy = new EFCachePolicy()
            .ExpirationMode(CacheExpirationMode.Absolute)
            .Timeout(TimeSpan.FromMinutes(5));

        var cacheKey = new EFCacheKey(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Products" })
        {
            KeyHash = "select-products-query-hash"
        };

        var staleData = new EFCachedData { NonQuery = 99 };

        cacheService.InsertValue(cacheKey, staleData, cachePolicy);
        cacheService.InvalidateCacheDependencies(cacheKey);

        // The raw service has no generation tracking so the stale insert succeeds.
        cacheService.InsertValue(cacheKey, staleData, cachePolicy);
        Assert.NotNull(cacheService.GetValue(cacheKey, cachePolicy));
    }

    /// <summary>
    ///     Verifies the fix: <see cref="DbCommandInterceptorProcessor"/> snapshots the
    ///     invalidation generation when a cache miss is detected, and skips InsertValue in
    ///     ProcessExecutedCommands when a cache invalidation occurred in the interim.
    ///
    ///     Timeline simulated:
    ///     t=1  ProcessExecutingCommands (SELECT) → cache miss → generation snapshot taken
    ///     t=2  InvalidateCacheDependencies (UPDATE) → generation incremented
    ///     t=3  ProcessExecutedCommands (SELECT result arrives) → stale detected → InsertValue NOT called
    /// </summary>
    [Fact]
    public void Issue177_Processor_SkipsCachingStaleResultAfterInvalidation()
    {
        var tracker = new EFCacheInvalidationTracker();
        var (processor, cacheServiceMock, cacheKeyProviderMock, cachePolicyParserMock,
            sqlCommandsProcessorMock, cacheServiceCheckMock) = CreateProcessor(tracker);

        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EF_Products" };
        var efCacheKey = new EFCacheKey(dependencies) { KeyHash = "query-hash" };
        var cachePolicy = new EFCachePolicy().ExpirationMode(CacheExpirationMode.Absolute)
            .Timeout(TimeSpan.FromMinutes(5));

        var commandMock = new Mock<DbCommand>();
        commandMock.Protected().Setup<DbTransaction>("DbTransaction").Returns((DbTransaction)null!);
        var command = commandMock.Object;
        var context = Mock.Of<DbContext>();

        cachePolicyParserMock
            .Setup(p => p.GetEFCachePolicy(It.IsAny<string>(), null))
            .Returns((cachePolicy, false));

        sqlCommandsProcessorMock
            .Setup(p => p.IsCrudCommand(It.IsAny<string>()))
            .Returns(false);

        cacheKeyProviderMock
            .Setup(p => p.GetEFCacheKey(command, context, cachePolicy))
            .Returns(efCacheKey);

        cacheServiceCheckMock
            .Setup(c => c.IsCacheServiceAvailable())
            .Returns(true);

        cacheServiceMock
            .Setup(c => c.GetValue(efCacheKey, cachePolicy))
            .Returns((EFCachedData?)null);

        // t=1: SELECT executing — cache miss → snapshot taken (generation = 0 for EF_Products)
        processor.ProcessExecutingCommands(command, context, 0);

        // t=2: UPDATE completes → generation incremented (simulates EFCacheDependenciesProcessor)
        tracker.IncrementGenerations(dependencies);

        // t=3: SELECT result arrives — processor should detect stale snapshot and skip InsertValue
        processor.ProcessExecutedCommands(command, context, 42);

        cacheServiceMock.Verify(c => c.InsertValue(It.IsAny<EFCacheKey>(), It.IsAny<EFCachedData?>(),
            It.IsAny<EFCachePolicy>()), Times.Never);
    }

    /// <summary>
    ///     Verifies the normal (non-racing) path: when no invalidation occurs between
    ///     ProcessExecutingCommands and ProcessExecutedCommands, InsertValue IS called.
    /// </summary>
    [Fact]
    public void Issue177_Processor_CachesResultWhenNoInvalidationOccurred()
    {
        var tracker = new EFCacheInvalidationTracker();
        var (processor, cacheServiceMock, cacheKeyProviderMock, cachePolicyParserMock,
            sqlCommandsProcessorMock, cacheServiceCheckMock) = CreateProcessor(tracker);

        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EF_Products" };
        var efCacheKey = new EFCacheKey(dependencies) { KeyHash = "query-hash" };
        var cachePolicy = new EFCachePolicy().ExpirationMode(CacheExpirationMode.Absolute)
            .Timeout(TimeSpan.FromMinutes(5));

        var commandMock = new Mock<DbCommand>();
        commandMock.Protected().Setup<DbTransaction>("DbTransaction").Returns((DbTransaction)null!);
        var command = commandMock.Object;
        var context = Mock.Of<DbContext>();

        cachePolicyParserMock
            .Setup(p => p.GetEFCachePolicy(It.IsAny<string>(), null))
            .Returns((cachePolicy, false));

        sqlCommandsProcessorMock
            .Setup(p => p.IsCrudCommand(It.IsAny<string>()))
            .Returns(false);

        cacheKeyProviderMock
            .Setup(p => p.GetEFCacheKey(command, context, cachePolicy))
            .Returns(efCacheKey);

        cacheServiceCheckMock
            .Setup(c => c.IsCacheServiceAvailable())
            .Returns(true);

        cacheServiceMock
            .Setup(c => c.GetValue(efCacheKey, cachePolicy))
            .Returns((EFCachedData?)null);

        // t=1: SELECT executing — cache miss
        processor.ProcessExecutingCommands(command, context, 0);

        // No invalidation between t=1 and t=2

        // t=2: SELECT result arrives — processor should call InsertValue
        processor.ProcessExecutedCommands(command, context, 42);

        cacheServiceMock.Verify(c => c.InsertValue(efCacheKey,
            It.Is<EFCachedData>(d => d.NonQuery == 42), cachePolicy), Times.Once);
    }

    private static (IDbCommandInterceptorProcessor Processor,
        Mock<IEFCacheServiceProvider> CacheServiceMock,
        Mock<IEFCacheKeyProvider> CacheKeyProviderMock,
        Mock<IEFCachePolicyParser> CachePolicyParserMock,
        Mock<IEFSqlCommandsProcessor> SqlCommandsProcessorMock,
        Mock<IEFCacheServiceCheck> CacheServiceCheckMock) CreateProcessor(IEFCacheInvalidationTracker tracker)
    {
        var cacheServiceMock = new Mock<IEFCacheServiceProvider>();
        var cacheDependenciesMock = new Mock<IEFCacheDependenciesProcessor>();
        var cacheKeyProviderMock = new Mock<IEFCacheKeyProvider>();
        var cachePolicyParserMock = new Mock<IEFCachePolicyParser>();
        var sqlCommandsProcessorMock = new Mock<IEFSqlCommandsProcessor>();
        var cacheServiceCheckMock = new Mock<IEFCacheServiceCheck>();
        var loggerMock = new Mock<IEFDebugLogger>();
        var cacheSettingsMock = new Mock<IOptions<EFCoreSecondLevelCacheSettings>>();
        cacheSettingsMock.SetupGet(x => x.Value).Returns(new EFCoreSecondLevelCacheSettings
        {
            AllowCachingWithExplicitTransactions = true
        });

        // InvalidateCacheDependencies returns false → command is treated as a read (no invalidation path)
        cacheDependenciesMock
            .Setup(d => d.InvalidateCacheDependencies(It.IsAny<string>(), It.IsAny<EFCacheKey>()))
            .Returns(false);

        var ignoreCachingProcessor = new DbCommandIgnoreCachingProcessor(
            cachePolicyParserMock.Object,
            sqlCommandsProcessorMock.Object,
            cacheSettingsMock.Object,
            loggerMock.Object,
            new Mock<ILogger<DbCommandIgnoreCachingProcessor>>().Object);

        var processor = new DbCommandInterceptorProcessor(
            loggerMock.Object,
            new Mock<ILogger<DbCommandInterceptorProcessor>>().Object,
            cacheServiceMock.Object,
            cacheDependenciesMock.Object,
            cacheKeyProviderMock.Object,
            cacheSettingsMock.Object,
            cacheServiceCheckMock.Object,
            ignoreCachingProcessor,
            tracker);

        return (processor, cacheServiceMock, cacheKeyProviderMock, cachePolicyParserMock,
            sqlCommandsProcessorMock, cacheServiceCheckMock);
    }

    private static IEFCacheServiceProvider CreateCacheServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddSingleton<IMemoryCacheChangeTokenProvider, EFMemoryCacheChangeTokenProvider>();

        var serviceProvider = services.BuildServiceProvider();
        var memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();
        var changeTokenProvider = serviceProvider.GetRequiredService<IMemoryCacheChangeTokenProvider>();

        return new EFMemoryCacheServiceProvider(memoryCache, changeTokenProvider, new Mock<IEFDebugLogger>().Object);
    }
}
