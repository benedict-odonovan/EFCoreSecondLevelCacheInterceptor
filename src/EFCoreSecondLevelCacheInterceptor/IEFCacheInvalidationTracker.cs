using System.Collections.Generic;

namespace EFCoreSecondLevelCacheInterceptor;

/// <summary>
///     Tracks a per-dependency invalidation generation counter so that a concurrent SELECT query
///     that completes after a cache invalidation does not re-insert stale data.
/// </summary>
public interface IEFCacheInvalidationTracker
{
    /// <summary>
    ///     Returns a snapshot of the current generation counter for each of the supplied dependency
    ///     keys. A key that has never been invalidated gets a generation of zero.
    /// </summary>
    Dictionary<string, long> TakeSnapshot(IEnumerable<string> keys);

    /// <summary>
    ///     Returns <c>true</c> when at least one dependency key in the snapshot has been invalidated
    ///     since the snapshot was taken.
    /// </summary>
    bool IsSnapshotStale(IReadOnlyDictionary<string, long> snapshot);

    /// <summary>
    ///     Increments the generation counter for each supplied key. Called when cache entries for
    ///     those dependencies are invalidated.
    /// </summary>
    void IncrementGenerations(IEnumerable<string> keys);
}
