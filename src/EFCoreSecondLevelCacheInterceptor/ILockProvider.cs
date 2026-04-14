using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EFCoreSecondLevelCacheInterceptor;

/// <summary>
///     Reader writer locking service
/// </summary>
public interface ILockProvider : IDisposable
{
    /// <summary>
    ///     Tries to enter keyed sync write locks in a deterministic order
    /// </summary>
    IDisposable? LockWrite(IReadOnlyCollection<string>? lockKeys, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Tries to enter keyed async write locks in a deterministic order
    /// </summary>
    ValueTask<IDisposable?> LockWriteAsync(IReadOnlyCollection<string>? lockKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Tries to enter keyed sync read locks in a deterministic order (shared; multiple concurrent readers are allowed)
    /// </summary>
    IDisposable? LockRead(IReadOnlyCollection<string>? lockKeys, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Tries to enter keyed async read locks in a deterministic order (shared; multiple concurrent readers are allowed)
    /// </summary>
    ValueTask<IDisposable?> LockReadAsync(IReadOnlyCollection<string>? lockKeys,
        CancellationToken cancellationToken = default);
}
