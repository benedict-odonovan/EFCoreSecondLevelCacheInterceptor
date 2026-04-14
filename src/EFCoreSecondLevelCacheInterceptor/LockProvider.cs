using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace EFCoreSecondLevelCacheInterceptor;

/// <summary>
///     Reader writer locking service
/// </summary>
public sealed class LockProvider : ILockProvider
{
    private readonly ConcurrentDictionary<string, AsyncReaderWriterLock> _keyedLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(value: 7);

    /// <summary>
    ///     Tries to enter keyed sync write locks in a deterministic order
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IDisposable? LockWrite(IReadOnlyCollection<string>? lockKeys, CancellationToken cancellationToken = default)
    {
        var keys = NormalizeLockKeys(lockKeys);
        var releasers = new List<IDisposable>(keys.Count);

        foreach (var key in keys)
        {
            var releaser = AcquireWriterLock(GetOrCreateKeyedLock(key), cancellationToken);
            if (releaser is null)
            {
                DisposeReleasers(releasers);
                return null;
            }

            releasers.Add(releaser);
        }

        return new CompositeLockReleaser(releasers);
    }

    /// <summary>
    ///     Tries to enter keyed async write locks in a deterministic order
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IDisposable?> LockWriteAsync(IReadOnlyCollection<string>? lockKeys,
        CancellationToken cancellationToken = default)
    {
        var keys = NormalizeLockKeys(lockKeys);
        var releasers = new List<IDisposable>(keys.Count);

        foreach (var key in keys)
        {
            var releaser = await AcquireWriterLockAsync(GetOrCreateKeyedLock(key), cancellationToken);
            if (releaser is null)
            {
                DisposeReleasers(releasers);
                return null;
            }

            releasers.Add(releaser);
        }

        return new CompositeLockReleaser(releasers);
    }

    /// <summary>
    ///     Tries to enter keyed sync read locks in a deterministic order (shared; multiple concurrent readers are allowed)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IDisposable? LockRead(IReadOnlyCollection<string>? lockKeys, CancellationToken cancellationToken = default)
    {
        var keys = NormalizeLockKeys(lockKeys);
        var releasers = new List<IDisposable>(keys.Count);

        foreach (var key in keys)
        {
            var releaser = AcquireReaderLock(GetOrCreateKeyedLock(key), cancellationToken);
            if (releaser is null)
            {
                DisposeReleasers(releasers);
                return null;
            }

            releasers.Add(releaser);
        }

        return new CompositeLockReleaser(releasers);
    }

    /// <summary>
    ///     Tries to enter keyed async read locks in a deterministic order (shared; multiple concurrent readers are allowed)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IDisposable?> LockReadAsync(IReadOnlyCollection<string>? lockKeys,
        CancellationToken cancellationToken = default)
    {
        var keys = NormalizeLockKeys(lockKeys);
        var releasers = new List<IDisposable>(keys.Count);

        foreach (var key in keys)
        {
            var releaser = await AcquireReaderLockAsync(GetOrCreateKeyedLock(key), cancellationToken);
            if (releaser is null)
            {
                DisposeReleasers(releasers);
                return null;
            }

            releasers.Add(releaser);
        }

        return new CompositeLockReleaser(releasers);
    }

    /// <summary>
    ///     Disposes the lock provider
    /// </summary>
    public void Dispose() => _keyedLocks.Clear();

    private AsyncReaderWriterLock GetOrCreateKeyedLock(string key)
        => _keyedLocks.GetOrAdd(key, _ => new AsyncReaderWriterLock());

    private IDisposable? AcquireWriterLock(AsyncReaderWriterLock rwLock, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            return rwLock.WriterLock(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async ValueTask<IDisposable?> AcquireWriterLockAsync(AsyncReaderWriterLock rwLock,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            return await rwLock.WriterLockAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private IDisposable? AcquireReaderLock(AsyncReaderWriterLock rwLock, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            return rwLock.ReaderLock(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async ValueTask<IDisposable?> AcquireReaderLockAsync(AsyncReaderWriterLock rwLock,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            return await rwLock.ReaderLockAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static List<string> NormalizeLockKeys(IReadOnlyCollection<string>? lockKeys)
    {
        if (lockKeys == null || lockKeys.Count == 0)
        {
            return new List<string>();
        }

        return lockKeys.Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void DisposeReleasers(List<IDisposable> releasers)
    {
        for (var i = releasers.Count - 1; i >= 0; i--)
        {
            releasers[i].Dispose();
        }
    }

    private sealed class CompositeLockReleaser(List<IDisposable> releasers) : IDisposable
    {
        private readonly List<IDisposable> _releasers = releasers;

        public void Dispose() => DisposeReleasers(_releasers);
    }
}
