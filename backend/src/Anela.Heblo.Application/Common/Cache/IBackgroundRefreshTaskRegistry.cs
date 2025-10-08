namespace Anela.Heblo.Application.Common.Cache;

public interface IBackgroundRefreshTaskRegistry
{
    void RegisterTask(
        string taskId,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod,
        RefreshTaskConfiguration configuration);

    void RegisterTask(
        string taskId,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod,
        string configurationKey);

    Task ForceRefreshAsync(string taskId, CancellationToken cancellationToken = default);

    IReadOnlyList<RefreshTaskConfiguration> GetRegisteredTasks();

    IReadOnlyList<RefreshTaskExecutionLog> GetExecutionHistory(string? taskId = null, int maxRecords = 100);

    RefreshTaskExecutionLog? GetLastExecution(string taskId);
}