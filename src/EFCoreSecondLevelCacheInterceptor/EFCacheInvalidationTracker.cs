using System.Collections.Concurrent;
using System.Collections.Generic;

namespace EFCoreSecondLevelCacheInterceptor;

/// <summary>
///     Tracks a per-dependency invalidation generation counter.
/// </summary>
public sealed class EFCacheInvalidationTracker : IEFCacheInvalidationTracker
{
    private readonly ConcurrentDictionary<string, long> _generations =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Dictionary<string, long> TakeSnapshot(IEnumerable<string> keys)
    {
        var snapshot = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            snapshot[key] = _generations.GetOrAdd(key, 0L);
        }

        return snapshot;
    }

    /// <inheritdoc />
    public bool IsSnapshotStale(IReadOnlyDictionary<string, long> snapshot)
    {
        foreach (var kvp in snapshot)
        {
            if (_generations.TryGetValue(kvp.Key, out var current) && current != kvp.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void IncrementGenerations(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            _generations.AddOrUpdate(key, 1L, (_, v) => v + 1);
        }
    }
}
