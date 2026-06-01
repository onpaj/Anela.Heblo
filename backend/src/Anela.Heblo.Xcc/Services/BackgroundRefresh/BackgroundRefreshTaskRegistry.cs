using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public class BackgroundRefreshTaskRegistry : IBackgroundRefreshTaskRegistry
{
    private readonly ILogger<BackgroundRefreshTaskRegistry> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, RegisteredTask> _registeredTasks = new();
    private readonly ConcurrentQueue<RefreshTaskExecutionLog> _executionHistory = new();
    private readonly object _historyLock = new();

    public BackgroundRefreshTaskRegistry(
        ILogger<BackgroundRefreshTaskRegistry> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IOptions<BackgroundRefreshTaskRegistrySetup> setup)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;

        // Initialize tasks from setup configuration
        InitializeTasksFromSetup(setup.Value);
    }

    private void InitializeTasksFromSetup(BackgroundRefreshTaskRegistrySetup setup)
    {
        _logger.LogInformation("Initializing {TaskCount} background refresh tasks from setup", setup.TaskRegistrations.Count);

        foreach (var taskInfo in setup.TaskRegistrations)
        {
            if (taskInfo.Configuration != null)
            {
                RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod, taskInfo.Configuration);
            }
            RegisterTask(taskInfo.TaskId, taskInfo.RefreshMethod);
        }

        _logger.LogInformation("Successfully initialized {RegisteredCount} background refresh tasks", _registeredTasks.Count);
    }

    public void RegisterTask(
        string taskId,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod,
        RefreshTaskConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task ID cannot be null or empty", nameof(taskId));

        if (refreshMethod == null)
            throw new ArgumentNullException(nameof(refreshMethod));

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        var registeredTask = new RegisteredTask(taskId, refreshMethod, configuration);

        _registeredTasks.AddOrUpdate(taskId, registeredTask, (_, _) => registeredTask);

        _logger.LogInformation("✅ Registered background refresh task '{TaskId}' with interval {RefreshInterval} (enabled: {Enabled})",
            taskId, configuration.RefreshInterval, configuration.Enabled);
    }

    public void RegisterTask(
        string taskId,
        Func<IServiceProvider, CancellationToken, Task> refreshMethod)
    {
        var configuration = RefreshTaskConfiguration.FromAppSettings(_configuration, taskId);
        RegisterTask(taskId, refreshMethod, configuration);
    }

    public async Task ForceRefreshAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_registeredTasks.TryGetValue(taskId, out var registeredTask))
        {
            throw new InvalidOperationException($"Task with ID '{taskId}' is not registered");
        }

        _logger.LogInformation("Force refreshing task '{TaskId}'", taskId);

        await ExecuteTaskAsync(registeredTask, cancellationToken, isForceRefresh: true);
    }

    public IReadOnlyList<RefreshTaskConfiguration> GetRegisteredTasks()
    {
        return _registeredTasks.Values
            .Select(task => task.Configuration)
            .ToList();
    }

    public IReadOnlyList<RefreshTaskExecutionLog> GetExecutionHistory(string? taskId = null, int maxRecords = 100)
    {
        lock (_historyLock)
        {
            var history = _executionHistory.ToList();

            if (!string.IsNullOrEmpty(taskId))
            {
                history = history.Where(log => log.TaskId == taskId).ToList();
            }

            return history
                .OrderByDescending(log => log.StartedAt)
                .Take(maxRecords)
                .ToList();
        }
    }

    public RefreshTaskExecutionLog? GetLastExecution(string taskId)
    {
        lock (_historyLock)
        {
            return _executionHistory
                .Where(log => log.TaskId == taskId)
                .OrderByDescending(log => log.StartedAt)
                .FirstOrDefault();
        }
    }

    internal async Task ExecuteTaskAsync(RegisteredTask registeredTask, CancellationToken cancellationToken, bool isForceRefresh = false)
    {
        var taskId = registeredTask.Configuration.TaskId;
        var startedAt = DateTime.UtcNow;

        var executionLog = new RefreshTaskExecutionLog
        {
            TaskId = taskId,
            StartedAt = startedAt,
            Status = RefreshTaskExecutionStatus.Running,
            Metadata = new Dictionary<string, object>
            {
                ["IsForceRefresh"] = isForceRefresh
            }
        };

        AddToExecutionHistory(executionLog);

        try
        {
            _logger.LogInformation("🚀 Executing refresh task '{TaskId}' (force: {IsForceRefresh})", taskId, isForceRefresh);

            using var scope = _serviceProvider.CreateScope();
            await registeredTask.RefreshMethod(scope.ServiceProvider, cancellationToken);

            var completedLog = executionLog with
            {
                CompletedAt = DateTime.UtcNow,
                Status = RefreshTaskExecutionStatus.Completed
            };

            ReplaceInExecutionHistory(executionLog, completedLog);

            _logger.LogInformation("✅ Successfully completed refresh task '{TaskId}' in {Duration}ms",
                taskId, completedLog.Duration?.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            var cancelledLog = executionLog with
            {
                CompletedAt = DateTime.UtcNow,
                Status = RefreshTaskExecutionStatus.Cancelled
            };

            ReplaceInExecutionHistory(executionLog, cancelledLog);

            _logger.LogDebug("Refresh task '{TaskId}' was cancelled", taskId);
            throw;
        }
        catch (Exception ex)
        {
            var failedLog = executionLog with
            {
                CompletedAt = DateTime.UtcNow,
                Status = RefreshTaskExecutionStatus.Failed,
                ErrorMessage = ex.Message
            };

            ReplaceInExecutionHistory(executionLog, failedLog);

            _logger.LogError(ex, "Failed to execute refresh task '{TaskId}'", taskId);
            throw;
        }
    }

    internal IEnumerable<RegisteredTask> GetAllRegisteredTasks()
    {
        return _registeredTasks.Values;
    }


    private void AddToExecutionHistory(RefreshTaskExecutionLog log)
    {
        lock (_historyLock)
        {
            _executionHistory.Enqueue(log);

            // Keep only the last 1000 entries to prevent memory issues
            while (_executionHistory.Count > 1000)
            {
                _executionHistory.TryDequeue(out _);
            }
        }
    }

    private void ReplaceInExecutionHistory(RefreshTaskExecutionLog oldLog, RefreshTaskExecutionLog newLog)
    {
        lock (_historyLock)
        {
            var tempList = new List<RefreshTaskExecutionLog>();

            // Dequeue all items and replace the matching one
            while (_executionHistory.TryDequeue(out var log))
            {
                if (log.TaskId == oldLog.TaskId && log.StartedAt == oldLog.StartedAt)
                {
                    tempList.Add(newLog);
                }
                else
                {
                    tempList.Add(log);
                }
            }

            // Re-enqueue all items
            foreach (var log in tempList)
            {
                _executionHistory.Enqueue(log);
            }
        }
    }

    internal record RegisteredTask(
        string TaskId,
        Func<IServiceProvider, CancellationToken, Task> RefreshMethod,
        RefreshTaskConfiguration Configuration);
}