using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EFCoreSecondLevelCacheInterceptor;

/// <summary>
///     Stack Exchange Redis Cache Provider
/// </summary>
public class EFStackExchangeRedisCacheProvider(
    IOptions<EFCoreSecondLevelCacheSettings> cacheSettings,
    IEFDebugLogger logger,
    IEFDataSerializer dataSerializer) : IEFCacheServiceProvider
{
    private ConnectionMultiplexer? _redisConnection;

    private ConnectionMultiplexer RedisConnection => _redisConnection ??= GetRedisConnection().Result;

    /// <inheritdoc />
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

        var redisDb = RedisConnection.GetDatabase();
        var keyHash = cacheKey.KeyHash;

        foreach (var rootCacheKey in cacheKey.CacheDependencies)
        {
            if (string.IsNullOrWhiteSpace(rootCacheKey))
            {
                continue;
            }

            if (cachePolicy.CacheTimeout.HasValue)
            {
                var expiryTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                                 cachePolicy.CacheTimeout.Value.TotalMilliseconds;
                await redisDb.SortedSetAddAsync(rootCacheKey, keyHash, expiryTime);
            }
            else
            {
                await redisDb.SortedSetAddAsync(rootCacheKey, keyHash,
                    long.MaxValue - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
        }

        var data = dataSerializer.Serialize(value);
        await redisDb.StringSetAsync(keyHash, data, cachePolicy.CacheTimeout, When.Always);
    }

    /// <inheritdoc />
    public async Task ClearAllCachedEntries()
        => logger.NotifyCacheInvalidation(clearAllCachedEntries: true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc />
    public async Task<EFCachedData?> GetValue(EFCacheKey cacheKey, EFCachePolicy cachePolicy)
    {
        if (cacheKey is null)
        {
            throw new ArgumentNullException(nameof(cacheKey));
        }

        var redisDb = RedisConnection.GetDatabase();
        var maybeValue = await redisDb.StringGetAsync(cacheKey.KeyHash);

        if (!maybeValue.HasValue)
        {
            return null;
        }

        await ManageSlidingExpiration(cacheKey, cachePolicy, redisDb);

        return dataSerializer.Deserialize<EFCachedData>(maybeValue);
    }

    /// <inheritdoc />
    public async Task InvalidateCacheDependencies(EFCacheKey cacheKey)
    {
        if (cacheKey is null)
        {
            throw new ArgumentNullException(nameof(cacheKey));
        }

        var redisDb = RedisConnection.GetDatabase();

        foreach (var rootCacheKey in cacheKey.CacheDependencies)
        {
            if (string.IsNullOrWhiteSpace(rootCacheKey))
            {
                continue;
            }

            var dependencyKeys = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var item in redisDb.SortedSetScanAsync(rootCacheKey))
            {
                _ = dependencyKeys.Add(item.Element.ToString());
            }

            if (dependencyKeys.Count > 0)
            {
                await redisDb.KeyDeleteAsync([.. dependencyKeys]);
            }

            await redisDb.KeyDeleteAsync(rootCacheKey);
        }
    }

    private static async Task ManageSlidingExpiration(EFCacheKey cacheKey, EFCachePolicy cachePolicy, IDatabase redisDb)
    {
        if (cachePolicy.CacheExpirationMode == CacheExpirationMode.Sliding && cachePolicy.CacheTimeout != TimeSpan.Zero)
        {
            await redisDb.KeyExpireAsync(cacheKey.KeyHash, cachePolicy.CacheTimeout, CommandFlags.FireAndForget);
        }
    }

    private async Task<ConnectionMultiplexer> GetRedisConnection()
    {
        var options = cacheSettings.Value.AdditionalData as EFRedisCacheConfigurationOptions ??
                      throw new InvalidOperationException(
                          message: "Please call the UseStackExchangeRedisCacheProvider() method.");

        if (options.RedisConnectionString is not null)
        {
            return await ConnectionMultiplexer.ConnectAsync(options.RedisConnectionString);
        }

        if (options.ConfigurationOptions is not null)
        {
            return await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions);
        }

        throw new InvalidOperationException(
            message:
            "Please specify the `RedisConnectionString` or `ConfigurationOptions` by calling the UseStackExchangeRedisCacheProvider() method.");
    }
}