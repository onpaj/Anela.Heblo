using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public class TierBasedHydrationOrchestrator : BackgroundService
{
    private readonly ILogger<TierBasedHydrationOrchestrator> _logger;
    private readonly BackgroundRefreshTaskRegistry _taskRegistry;
    private readonly int _readinessTier;
    private readonly TaskCompletionSource _hydrationCompleted = new();
    private readonly TaskCompletionSource _readinessReached = new();

    public TierBasedHydrationOrchestrator(
        ILogger<TierBasedHydrationOrchestrator> logger,
        BackgroundRefreshTaskRegistry taskRegistry,
        IOptions<BackgroundServicesOptions> options)
    {
        _logger = logger;
        _taskRegistry = taskRegistry;
        _readinessTier = options.Value.ReadinessTier;
    }

    /// <summary>
    /// Completes when all tiers have finished hydrating (used to gate periodic scheduling).
    /// </summary>
    public Task WaitForHydrationCompletionAsync() => _hydrationCompleted.Task;

    /// <summary>
    /// Completes once all tiers at/below <see cref="BackgroundServicesOptions.ReadinessTier"/> have
    /// finished. Higher tiers keep hydrating in the background — used to gate /health/ready.
    /// </summary>
    public Task WaitForReadinessAsync() => _readinessReached.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("🚀 Starting tier-based hydration process (readiness tier {ReadinessTier})", _readinessTier);

            await ExecuteHydrationTiersAsync(stoppingToken);

            _logger.LogInformation("✅ Tier-based hydration completed successfully");
            _readinessReached.TrySetResult();
            _hydrationCompleted.SetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Tier-based hydration failed");
            _readinessReached.TrySetException(ex);
            _hydrationCompleted.SetException(ex);
            throw;
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

        // Highest tier that must complete before the app is considered ready. int.MinValue when no
        // tier is required (e.g. ReadinessTier = 0) — readiness is then signaled immediately.
        var maxRequiredTier = tasksByTier
            .Select(group => group.Key)
            .Where(tier => tier <= _readinessTier)
            .DefaultIfEmpty(int.MinValue)
            .Max();

        if (maxRequiredTier == int.MinValue)
        {
            _logger.LogInformation("No hydration tiers at/below readiness tier {ReadinessTier} - app is ready immediately", _readinessTier);
            _readinessReached.TrySetResult();
        }

        foreach (var tierGroup in tasksByTier)
        {
            var tier = tierGroup.Key;
            var tierTasks = tierGroup.ToList();

            if (tier > _readinessTier)
            {
                // Optional tier: keep hydrating in the background but never fail readiness or crash the host.
                try
                {
                    await ExecuteTierAsync(tier, tierTasks, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Optional hydration tier {Tier} (> readiness tier {ReadinessTier}) failed - continuing", tier, _readinessTier);
                }

                continue;
            }

            // Required tier: failures propagate and block readiness.
            await ExecuteTierAsync(tier, tierTasks, cancellationToken);

            if (tier == maxRequiredTier)
            {
                _logger.LogInformation("✅ Readiness reached - all tiers at/below readiness tier {ReadinessTier} completed", _readinessTier);
                _readinessReached.TrySetResult();
            }
        }
    }

    private async Task ExecuteTierAsync(int tier, List<BackgroundRefreshTaskRegistry.RegisteredTask> tierTasks, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔄 Starting hydration tier {Tier} with {TaskCount} tasks",
            tier, tierTasks.Count);

        var tierStartTime = DateTime.UtcNow;

        // Execute all tasks in this tier concurrently
        var tierExecutionTasks = tierTasks.Select(task => ExecuteTierTaskAsync(task, cancellationToken)).ToArray();

        try
        {
            await Task.WhenAll(tierExecutionTasks);

            var tierDuration = DateTime.UtcNow - tierStartTime;
            _logger.LogInformation("✅ Completed hydration tier {Tier} in {Duration}ms",
                tier, tierDuration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var tierDuration = DateTime.UtcNow - tierStartTime;
            _logger.LogError(ex, "❌ Failed to complete hydration tier {Tier} after {Duration}ms",
                tier, tierDuration.TotalMilliseconds);
            throw;
        }
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