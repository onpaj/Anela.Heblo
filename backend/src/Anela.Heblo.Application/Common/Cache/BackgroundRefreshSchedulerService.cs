using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Common.Cache;

public class BackgroundRefreshSchedulerService : BackgroundService
{
    private readonly ILogger<BackgroundRefreshSchedulerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public BackgroundRefreshSchedulerService(
        ILogger<BackgroundRefreshSchedulerService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background refresh scheduler service starting");

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Give the application time to fully start

        using var scope = _serviceProvider.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IBackgroundRefreshTaskRegistry>();

        var registeredTasks = registry.GetRegisteredTasks();
        _logger.LogInformation("Found {TotalCount} registered background refresh tasks", registeredTasks.Count);

        // Start individual task loops
        var taskLoops = new List<Task>();
        foreach (var taskConfig in registeredTasks)
        {
            if (taskConfig.Enabled)
            {
                var taskLoop = RunTaskLoop(taskConfig, stoppingToken);
                taskLoops.Add(taskLoop);
                _logger.LogInformation("Started task loop for '{TaskId}'", taskConfig.TaskId);
            }
            else
            {
                _logger.LogInformation("Skipping disabled task '{TaskId}'", taskConfig.TaskId);
            }
        }

        _logger.LogInformation("Successfully started {Count} background refresh task loops", taskLoops.Count);

        // Wait for all task loops to complete
        await Task.WhenAll(taskLoops);

        _logger.LogInformation("Background refresh scheduler service stopping");
    }

    private async Task RunTaskLoop(RefreshTaskConfiguration taskConfig, CancellationToken stoppingToken)
    {
        var taskId = taskConfig.TaskId;
        
        try
        {
            // Initial delay
            if (taskConfig.InitialDelay > TimeSpan.Zero)
            {
                _logger.LogDebug("Task '{TaskId}' waiting {InitialDelay} before first execution", taskId, taskConfig.InitialDelay);
                await Task.Delay(taskConfig.InitialDelay, stoppingToken);
            }

            // Main execution loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Executing scheduled task '{TaskId}'", taskId);
                    
                    using var scope = _serviceProvider.CreateScope();
                    var registry = scope.ServiceProvider.GetRequiredService<IBackgroundRefreshTaskRegistry>();
                    await registry.ForceRefreshAsync(taskId, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break; // Exit loop on cancellation
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing scheduled task '{TaskId}'", taskId);
                }

                // Wait for next execution
                try
                {
                    await Task.Delay(taskConfig.RefreshInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break; // Exit loop on cancellation
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Task loop for '{TaskId}' was cancelled", taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in task loop for '{TaskId}'", taskId);
        }
    }

    public override void Dispose()
    {
        _cancellationTokenSource.Dispose();
        base.Dispose();
    }
}