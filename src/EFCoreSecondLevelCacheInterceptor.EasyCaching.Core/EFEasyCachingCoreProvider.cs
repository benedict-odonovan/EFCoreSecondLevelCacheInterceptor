using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EasyCaching.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EFCoreSecondLevelCacheInterceptor;

/// <summary>
///     Using ICacheManager as a cache service.
/// </summary>
public class EFEasyCachingCoreProvider : IEFCacheServiceProvider
{
    private readonly IEFCacheKeyPrefixProvider _cacheKeyPrefixProvider;
    private readonly EFCoreSecondLevelCacheSettings _cacheSettings;
    private readonly ILogger<EFEasyCachingCoreProvider> _easyCachingCoreProviderLogger;
    private readonly IEFDebugLogger _logger;

    private readonly ConcurrentDictionary<string, IEasyCachingProviderBase> _providers = new(StringComparer.Ordinal);

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Using EasyCachingCore as a cache service.
    /// </summary>
    public EFEasyCachingCoreProvider(IOptions<EFCoreSecondLevelCacheSettings> cacheSettings,
        IServiceProvider serviceProvider,
        IEFDebugLogger logger,
        IEFCacheKeyPrefixProvider cacheKeyPrefixProvider,
        ILogger<EFEasyCachingCoreProvider> easyCachingCoreProviderLogger)
    {
        if (cacheSettings == null)
        {
            throw new ArgumentNullException(nameof(cacheSettings));
        }

        _cacheSettings = cacheSettings.Value;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
        _cacheKeyPrefixProvider = cacheKeyPrefixProvider;
        _easyCachingCoreProviderLogger = easyCachingCoreProviderLogger;
    }

    /// <summary>
    ///     Adds a new item to the cache.
    /// </summary>
    /// <param name="cacheKey">key</param>
    /// <param name="value">value</param>
    /// <param name="cachePolicy">Defines the expiration mode of the cache item.</param>
    public async Task InsertValue(EFCacheKey cacheKey, EFCachedData? value, EFCachePolicy cachePolicy)
    {
        if (cacheKey is null)
        {
            throw new ArgumentNullException(nameof(cacheKey));
        }

        if (cachePolicy is null)
        {
            throw new ArgumentNullException(nameof(cachePolicy));
        }

        value ??= new EFCachedData
        {
            IsNull = true
        };

        var easyCachingProvider = GetEasyCachingProvider(cacheKey);

        var keyHash = cacheKey.KeyHash;
        var timeout = cachePolicy.CacheTimeout ?? TimeSpan.MaxValue;

        foreach (var rootCacheKey in cacheKey.CacheDependencies)
        {
            if (string.IsNullOrWhiteSpace(rootCacheKey))
            {
                continue;
            }

            var items = await easyCachingProvider.GetAsync<HashSet<string>>(rootCacheKey);

            if (items.IsNull)
            {
                await easyCachingProvider.SetAsync(rootCacheKey, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    keyHash
                }, timeout);
            }
            else
            {
                items.Value.Add(keyHash);
                await easyCachingProvider.SetAsync(rootCacheKey, items.Value, timeout);
            }
        }

        // We don't support Sliding Expiration at this time. -> https://github.com/dotnetcore/EasyCaching/issues/113
        await easyCachingProvider.SetAsync(keyHash, value, timeout);
    }

    /// <summary>
    ///     Removes the cached entries added by this library.
    /// </summary>
    public async Task ClearAllCachedEntries()
    {
        var easyCachingProvider = GetEasyCachingProvider(cacheKey: null);

        switch (easyCachingProvider)
        {
            case IHybridCachingProvider hcp:
                // IHybridCachingProvider doesn't have a `Flush()` method:
                // https://github.com/dotnetcore/EasyCaching/issues/351
                var cacheKeyPrefix = _cacheKeyPrefixProvider.GetCacheKeyPrefix();

                if (string.IsNullOrWhiteSpace(cacheKeyPrefix))
                {
                    throw new InvalidOperationException(
                        message: "Please specify a CacheKeyPrefix by calling the `.UseCacheKeyPrefix(...)` method.");
                }

                await hcp.RemoveByPrefixAsync(cacheKeyPrefix);

                break;
            case IEasyCachingProvider ecp:
                await ecp.FlushAsync();

                break;
        }

        _logger.NotifyCacheInvalidation(clearAllCachedEntries: true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Gets a cached entry by key.
    /// </summary>
    /// <param name="cacheKey">key to find</param>
    /// <returns>cached value</returns>
    /// <param name="cachePolicy">Defines the expiration mode of the cache item.</param>
    public async Task<EFCachedData?> GetValue(EFCacheKey cacheKey, EFCachePolicy cachePolicy)
    {
        if (cacheKey is null)
        {
            throw new ArgumentNullException(nameof(cacheKey));
        }

        var easyCachingProvider = GetEasyCachingProvider(cacheKey);

        return (await easyCachingProvider.GetAsync<EFCachedData>(cacheKey.KeyHash)).Value;
    }

    /// <summary>
    ///     Invalidates all the cache entries which are dependent on any of the specified root keys.
    /// </summary>
    /// <param name="cacheKey">Stores information of the computed key of the input LINQ query.</param>
    public async Task InvalidateCacheDependencies(EFCacheKey cacheKey)
    {
        if (cacheKey is null)
        {
            throw new ArgumentNullException(nameof(cacheKey));
        }

        var easyCachingProvider = GetEasyCachingProvider(cacheKey);

        foreach (var rootCacheKey in cacheKey.CacheDependencies)
        {
            if (string.IsNullOrWhiteSpace(rootCacheKey))
            {
                continue;
            }

            var cachedValue = await easyCachingProvider.GetAsync<EFCachedData>(cacheKey.KeyHash);
            var dependencyKeys = await easyCachingProvider.GetAsync<HashSet<string>>(rootCacheKey);

            if (AreRootCacheKeysExpired(cachedValue, dependencyKeys))
            {
                if (_logger.IsLoggerEnabled)
                {
                    var message =
                        $"Invalidated all of the cache entries due to early expiration of a root cache key[{rootCacheKey}].";

                    _easyCachingCoreProviderLogger.LogDebug(CacheableEventId.QueryResultInvalidated, message);

                    _logger.NotifyCacheableEvent(CacheableLogEventId.QueryResultInvalidated, message, commandText: "",
                        cacheKey);
                }

                await ClearAllCachedEntries();

                return;
            }

            await ClearDependencyValues(dependencyKeys, cacheKey);
            await easyCachingProvider.RemoveAsync(rootCacheKey);
        }
    }

    private async Task ClearDependencyValues(CacheValue<HashSet<string>> dependencyKeys, EFCacheKey cacheKey)
    {
        if (dependencyKeys.IsNull)
        {
            return;
        }

        var easyCachingProvider = GetEasyCachingProvider(cacheKey);

        foreach (var dependencyKey in dependencyKeys.Value)
        {
            await easyCachingProvider.RemoveAsync(dependencyKey);
        }
    }

    private static bool AreRootCacheKeysExpired(CacheValue<EFCachedData> cachedValue,
        CacheValue<HashSet<string>> dependencyKeys)
        => !cachedValue.IsNull && dependencyKeys.IsNull;

    private IEasyCachingProviderBase GetEasyCachingProvider(EFCacheKey? cacheKey)
    {
        string providerName;

        if (_cacheSettings.ProviderName is not null)
        {
            providerName = _cacheSettings.ProviderName;
        }
        else if (_cacheSettings.CacheProviderName is not null)
        {
            providerName = _cacheSettings.CacheProviderName(_serviceProvider, cacheKey);
        }
        else
        {
            throw new InvalidOperationException(
                message: "Please set the ProviderName or CacheProviderName of the CacheSettings");
        }

        return _providers.GetOrAdd(providerName, name =>
        {
            if (_cacheSettings.IsHybridCache)
            {
                var hybridFactory = _serviceProvider.GetRequiredService<IHybridProviderFactory>();

                return hybridFactory.GetHybridCachingProvider(name);
            }

            var providerFactory = _serviceProvider.GetRequiredService<IEasyCachingProviderFactory>();

            return providerFactory.GetCachingProvider(name);
        });
    }
}