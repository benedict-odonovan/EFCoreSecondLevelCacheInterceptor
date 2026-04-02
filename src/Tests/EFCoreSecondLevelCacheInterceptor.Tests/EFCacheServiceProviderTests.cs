using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EFCoreSecondLevelCacheInterceptor.Tests;

[TestClass]
public class EFCacheServiceProviderTests
{
    [TestMethod]
    [DataRow(TestCacheProvider.BuiltInInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreInMemory)]
    [DataRow(TestCacheProvider.EasyCachingCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreHybrid)]
    public virtual async Task TestCacheInvalidationWithTwoRoots(TestCacheProvider cacheProvider)
    {
        var cacheService = await EFServiceProvider.GetCacheServiceProvider(cacheProvider);

        var efCachePolicy = new EFCachePolicy().Timeout(TimeSpan.FromMinutes(minutes: 10))
            .ExpirationMode(CacheExpirationMode.Absolute);

        var key1 = new EFCacheKey(new HashSet<string>
        {
            "entity1.model",
            "entity2.model"
        })
        {
            KeyHash = "EF_key1"
        };

        await cacheService.InsertValue(key1, new EFCachedData
        {
            Scalar = "value1"
        }, efCachePolicy);

        var key2 = new EFCacheKey(new HashSet<string>
        {
            "entity1.model",
            "entity2.model"
        })
        {
            KeyHash = "EF_key2"
        };

        await cacheService.InsertValue(key2, new EFCachedData
        {
            Scalar = "value2"
        }, efCachePolicy);

        var value1 = await cacheService.GetValue(key1, efCachePolicy);
        Assert.IsNotNull(value1);

        var value2 = await cacheService.GetValue(key2, efCachePolicy);
        Assert.IsNotNull(value2);

        await cacheService.InvalidateCacheDependencies(new EFCacheKey(new HashSet<string>
        {
            "entity2.model"
        })
        {
            KeyHash = "EF_key1"
        });

        value1 = await cacheService.GetValue(key1, efCachePolicy);
        Assert.IsNull(value1);

        value2 = await cacheService.GetValue(key2, efCachePolicy);
        Assert.IsNull(value2);
    }

    [TestMethod]
    [DataRow(TestCacheProvider.BuiltInInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreInMemory)]
    [DataRow(TestCacheProvider.EasyCachingCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreHybrid)]
    public virtual async Task TestCacheInvalidationWithOneRoot(TestCacheProvider cacheProvider)
    {
        var cacheService = await EFServiceProvider.GetCacheServiceProvider(cacheProvider);

        var efCachePolicy = new EFCachePolicy().Timeout(TimeSpan.FromMinutes(minutes: 10))
            .ExpirationMode(CacheExpirationMode.Absolute);

        var key1 = new EFCacheKey(new HashSet<string>
        {
            "entity1"
        })
        {
            KeyHash = "EF_key1"
        };

        await cacheService.InsertValue(key1, new EFCachedData
        {
            Scalar = "value1"
        }, efCachePolicy);

        var key2 = new EFCacheKey(new HashSet<string>
        {
            "entity1"
        })
        {
            KeyHash = "EF_key2"
        };

        await cacheService.InsertValue(key2, new EFCachedData
        {
            Scalar = "value2"
        }, efCachePolicy);

        var value1 = await cacheService.GetValue(key1, efCachePolicy);
        Assert.IsNotNull(value1);

        var value2 = await cacheService.GetValue(key2, efCachePolicy);
        Assert.IsNotNull(value2);

        await cacheService.InvalidateCacheDependencies(new EFCacheKey(new HashSet<string>
        {
            "entity1"
        })
        {
            KeyHash = "EF_key2"
        });

        value1 = await cacheService.GetValue(key1, efCachePolicy);
        Assert.IsNull(value1);

        value2 = await cacheService.GetValue(key2, efCachePolicy);
        Assert.IsNull(value2);
    }

    [TestMethod]
    [DataRow(TestCacheProvider.BuiltInInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreInMemory)]
    [DataRow(TestCacheProvider.EasyCachingCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreHybrid)]
    public virtual async Task TestObjectCacheInvalidationWithOneRoot(TestCacheProvider cacheProvider)
    {
        var cacheService = await EFServiceProvider.GetCacheServiceProvider(cacheProvider);

        var efCachePolicy = new EFCachePolicy().Timeout(TimeSpan.FromMinutes(minutes: 10))
            .ExpirationMode(CacheExpirationMode.Absolute);

        const string rootCacheKey = "EFSecondLevelCache.Core.AspNetCoreSample.DataLayer.Entities.Product";

        await cacheService.InvalidateCacheDependencies(new EFCacheKey(new HashSet<string>
        {
            rootCacheKey
        })
        {
            KeyHash = "EF_key1"
        });

        var key11888622 = new EFCacheKey(new HashSet<string>
        {
            rootCacheKey
        })
        {
            KeyHash = "11888622"
        };

        var val11888622 = await cacheService.GetValue(key11888622, efCachePolicy);
        Assert.IsNull(val11888622);

        await cacheService.InsertValue(key11888622, new EFCachedData
        {
            Scalar = "Test1"
        }, efCachePolicy);

        var key44513A63 = new EFCacheKey(new HashSet<string>
        {
            rootCacheKey
        })
        {
            KeyHash = "44513A63"
        };

        var val44513A63 = await cacheService.GetValue(key44513A63, efCachePolicy);
        Assert.IsNull(val44513A63);

        await cacheService.InsertValue(key44513A63, new EFCachedData
        {
            Scalar = "Test1"
        }, efCachePolicy);

        await cacheService.InvalidateCacheDependencies(new EFCacheKey(new HashSet<string>
        {
            rootCacheKey
        })
        {
            KeyHash = "44513A63"
        });

        val11888622 = await cacheService.GetValue(key11888622, efCachePolicy);
        Assert.IsNull(val11888622);

        val44513A63 = await cacheService.GetValue(key44513A63, efCachePolicy);
        Assert.IsNull(val44513A63);
    }

    [TestMethod]
    [DataRow(TestCacheProvider.BuiltInInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreInMemory)]
    [DataRow(TestCacheProvider.EasyCachingCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreHybrid)]
    public virtual async Task TestCacheInvalidationWithSimilarRoots(TestCacheProvider cacheProvider)
    {
        var cacheService = await EFServiceProvider.GetCacheServiceProvider(cacheProvider);

        var efCachePolicy = new EFCachePolicy().Timeout(TimeSpan.FromMinutes(minutes: 10))
            .ExpirationMode(CacheExpirationMode.Absolute);

        var key1 = new EFCacheKey(new HashSet<string>
        {
            "entity1",
            "entity2"
        })
        {
            KeyHash = "EF_key1"
        };

        await cacheService.InsertValue(key1, new EFCachedData
        {
            Scalar = "value1"
        }, efCachePolicy);

        var key2 = new EFCacheKey(new HashSet<string>
        {
            "entity2"
        })
        {
            KeyHash = "EF_key2"
        };

        await cacheService.InsertValue(key2, new EFCachedData
        {
            Scalar = "value2"
        }, efCachePolicy);

        var value1 = await cacheService.GetValue(key1, efCachePolicy);
        Assert.IsNotNull(value1);

        var value2 = await cacheService.GetValue(key2, efCachePolicy);
        Assert.IsNotNull(value2);

        await cacheService.InvalidateCacheDependencies(new EFCacheKey(new HashSet<string>
        {
            "entity2"
        })
        {
            KeyHash = "EF_key2"
        });

        value1 = await cacheService.GetValue(key1, efCachePolicy);
        Assert.IsNull(value1);

        value2 = await cacheService.GetValue(key2, efCachePolicy);
        Assert.IsNull(value2);
    }

    [TestMethod]
    [DataRow(TestCacheProvider.BuiltInInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreInMemory)]
    [DataRow(TestCacheProvider.EasyCachingCoreRedis)]
    [DataRow(TestCacheProvider.EasyCachingCoreHybrid)]
    public virtual async Task TestInsertingNullValues(TestCacheProvider cacheProvider)
    {
        var cacheService = await EFServiceProvider.GetCacheServiceProvider(cacheProvider);

        var efCachePolicy = new EFCachePolicy().Timeout(TimeSpan.FromMinutes(minutes: 10))
            .ExpirationMode(CacheExpirationMode.Absolute);

        var key1 = new EFCacheKey(new HashSet<string>
        {
            "entity1",
            "entity2"
        })
        {
            KeyHash = "EF_key1"
        };

        await cacheService.InsertValue(key1, value: null, efCachePolicy);

        var value1 = await cacheService.GetValue(key1, efCachePolicy);
        Assert.IsNotNull(value1);
        Assert.IsTrue(value1.IsNull, $"value1 is `{value1}`");
    }

    [TestMethod]
    [DataRow(TestCacheProvider.EasyCachingCoreInMemory)]
    [DataRow(TestCacheProvider.CacheManagerCoreInMemory)]
    [DataRow(TestCacheProvider.BuiltInInMemory)]
    public virtual async Task TestConcurrentCacheInsertAndInvalidation(TestCacheProvider cacheProvider)
    {
        const string rootKey = "entity1";
        var cacheService = await EFServiceProvider.GetCacheServiceProvider(cacheProvider);

        var efCachePolicy = new EFCachePolicy().Timeout(TimeSpan.FromMinutes(minutes: 10))
            .ExpirationMode(CacheExpirationMode.Absolute);

        await Task.WhenAll(Task.Run(InsertValues), Task.Run(InvalidateCacheDependencies));

        async Task InsertValues()
        {
            for (var i = 0; i < 10000; i++)
            {
                var key = new EFCacheKey(new HashSet<string>
                {
                    rootKey
                })
                {
                    KeyHash = $"EF_key{i}"
                };

                await cacheService.InsertValue(key, new EFCachedData
                {
                    Scalar = $"value{i}"
                }, efCachePolicy);
            }
        }

        async Task InvalidateCacheDependencies()
        {
            var defaultKey = new EFCacheKey(new HashSet<string>
            {
                rootKey
            })
            {
                KeyHash = "EF_key"
            };

            await cacheService.InsertValue(defaultKey, new EFCachedData
            {
                Scalar = "value"
            }, efCachePolicy);

            for (var i = 0; i < 5000; i++)
            {
                await cacheService.InvalidateCacheDependencies(defaultKey);
            }
        }
    }
}