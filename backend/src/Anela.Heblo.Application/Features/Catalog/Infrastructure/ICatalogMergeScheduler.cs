namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public interface ICatalogMergeScheduler : IDisposable
{
    /// <summary>
    /// Gets whether a merge operation is currently in progress
    /// </summary>
    bool IsMergeInProgress { get; }

    /// <summary>
    /// Sets the callback function that will be executed when a merge is triggered
    /// </summary>
    /// <param name="mergeCallback">Async function to execute the merge operation</param>
    void SetMergeCallback(Func<CancellationToken, Task> mergeCallback);

    /// <summary>
    /// Schedules a merge operation for the specified data source with debouncing
    /// </summary>
    /// <param name="dataSource">Name of the data source that was invalidated</param>
    void ScheduleMerge(string dataSource);

    /// <summary>
    /// Gets the timestamp of the last completed merge operation
    /// </summary>
    /// <returns>DateTime of last merge completion</returns>
    DateTime GetLastMergeTime();

    /// <summary>
    /// Indicates whether there is a merge operation scheduled but not yet executed
    /// </summary>
    /// <returns>True if merge is scheduled but not yet executed</returns>
    bool HasPendingMerge();

    /// <summary>
    /// Waits for the currently running merge operation to complete, if any
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when the current merge finishes</returns>
    Task WaitForCurrentMergeAsync(CancellationToken cancellationToken = default);
}