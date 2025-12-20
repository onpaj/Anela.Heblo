using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public class TierBasedHydrationOrchestrator : BackgroundService
{
    private readonly ILogger<TierBasedHydrationOrchestrator> _logger;
    private readonly BackgroundRefreshTaskRegistry _taskRegistry;
    private readonly TaskCompletionSource _hydrationCompleted = new();

    public TierBasedHydrationOrchestrator(
        ILogger<TierBasedHydrationOrchestrator> logger,
        BackgroundRefreshTaskRegistry taskRegistry)
    {
        _logger = logger;
        _taskRegistry = taskRegistry;
    }

    public Task WaitForHydrationCompletionAsync() => _hydrationCompleted.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("🚀 Starting tier-based hydration process");

            await ExecuteHydrationTiersAsync(stoppingToken);

            _logger.LogInformation("✅ Tier-based hydration completed (partial success allowed)");
            _hydrationCompleted.SetResult();
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Tier-based hydration was cancelled");
            _hydrationCompleted.SetCanceled();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Critical error during tier-based hydration");
            // Even on critical error, we complete the hydration to allow periodic refresh to start
            // Individual task failures are already logged in ExecuteTierAsync
            _hydrationCompleted.SetResult();
        }
    }

    private async Task ExecuteHydrationTiersAsync(CancellationToken cancellationToken)
    {
        var allTasks = _taskRegistry.GetAllRegisteredTasks()
            .Where(task => task.Configuration.Enabled)
            .ToList();

        if (!allTasks.Any())
        {
            _logger.LogInformation("No enabled tasks found for hydration");
            return;
        }

        // Group tasks by hydration tier
        var tasksByTier = allTasks
            .GroupBy(task => task.Configuration.HydrationTier)
            .OrderBy(group => group.Key)
            .ToList();

        _logger.LogInformation("Found {TierCount} hydration tiers with {TaskCount} total tasks",
            tasksByTier.Count, allTasks.Count);

        foreach (var tierGroup in tasksByTier)
        {
            var tier = tierGroup.Key;
            var tierTasks = tierGroup.ToList();

            await ExecuteTierAsync(tier, tierTasks, cancellationToken);
        }
    }

    private async Task ExecuteTierAsync(int tier, List<BackgroundRefreshTaskRegistry.RegisteredTask> tierTasks, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔄 Starting hydration tier {Tier} with {TaskCount} tasks",
            tier, tierTasks.Count);

        var tierStartTime = DateTime.UtcNow;

        // Execute all tasks in this tier concurrently with resilient error handling
        var tierExecutionTasks = tierTasks.Select(task => ExecuteTierTaskAsync(task, cancellationToken)).ToArray();

        // Wait for all tasks to complete (even if some fail)
        var results = await Task.WhenAll(tierExecutionTasks.Select(async task =>
        {
            try
            {
                await task;
                return (Success: true, Exception: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Success: false, Exception: ex);
            }
        }));

        var tierDuration = DateTime.UtcNow - tierStartTime;
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        if (failureCount > 0)
        {
            _logger.LogWarning("⚠️ Completed hydration tier {Tier} in {Duration}ms with {SuccessCount}/{TotalCount} tasks successful, {FailureCount} failed",
                tier, tierDuration.TotalMilliseconds, successCount, tierTasks.Count, failureCount);

            // Log individual failures
            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i].Success)
                {
                    _logger.LogError(results[i].Exception, "Task '{TaskId}' failed in tier {Tier}",
                        tierTasks[i].Configuration.TaskId, tier);
                }
            }
        }
        else
        {
            _logger.LogInformation("✅ Completed hydration tier {Tier} in {Duration}ms - all {TaskCount} tasks successful",
                tier, tierDuration.TotalMilliseconds, tierTasks.Count);
        }

        // Tier is considered successful even if some tasks failed (partial success)
        // This allows other tiers to proceed and periodic refresh to start
    }

    private async Task ExecuteTierTaskAsync(BackgroundRefreshTaskRegistry.RegisteredTask task, CancellationToken cancellationToken)
    {
        var taskId = task.Configuration.TaskId;

        try
        {
            _logger.LogInformation("▶️ Executing hydration task '{TaskId}' (tier {Tier})",
                taskId, task.Configuration.HydrationTier);

            var taskStartTime = DateTime.UtcNow;

            // Apply initial delay if configured
            if (task.Configuration.InitialDelay > TimeSpan.Zero)
            {
                _logger.LogDebug("⏱️ Applying initial delay of {Delay} for task '{TaskId}'",
                    task.Configuration.InitialDelay, taskId);
                await Task.Delay(task.Configuration.InitialDelay, cancellationToken);
            }

            // Execute the task
            await _taskRegistry.ExecuteTaskAsync(task, cancellationToken, isForceRefresh: false);

            var taskDuration = DateTime.UtcNow - taskStartTime;
            _logger.LogInformation("✅ Completed hydration task '{TaskId}' in {Duration}ms",
                taskId, taskDuration.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Hydration task '{TaskId}' was cancelled", taskId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to execute hydration task '{TaskId}'", taskId);
            throw;
        }
    }
}