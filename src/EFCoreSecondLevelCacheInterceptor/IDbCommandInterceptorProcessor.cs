using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace EFCoreSecondLevelCacheInterceptor;

/// <summary>
///     Helps process SecondLevelCacheInterceptor
/// </summary>
public interface IDbCommandInterceptorProcessor
{
    /// <summary>
    ///     Reads data from cache or cache it and then returns the result
    /// </summary>
    Task<T> ProcessExecutedCommands<T>(DbCommand command, DbContext? context, T result);

    /// <summary>
    ///     Adds command's data to the cache
    /// </summary>
    Task<T> ProcessExecutingCommands<T>(DbCommand command, DbContext? context, T result);
}