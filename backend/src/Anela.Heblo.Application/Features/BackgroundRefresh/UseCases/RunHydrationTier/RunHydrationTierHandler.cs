using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierHandler : IRequestHandler<RunHydrationTierRequest, RunHydrationTierResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<RunHydrationTierHandler> _logger;

    public RunHydrationTierHandler(IBackgroundRefreshTaskRegistry taskRegistry, ILogger<RunHydrationTierHandler> logger)
    {
        _taskRegistry = taskRegistry;
        _logger = logger;
    }

    public async Task<RunHydrationTierResponse> Handle(RunHydrationTierRequest request, CancellationToken cancellationToken)
    {
        var tasksInTier = _taskRegistry.GetRegisteredTasks()
            .Where(t => t.HydrationTier == request.Tier && t.Enabled)
            .OrderBy(t => t.TaskId)
            .ToList();

        if (tasksInTier.Count == 0)
            return new RunHydrationTierResponse { NotFound = true, ErrorMessage = $"No enabled tasks found for tier {request.Tier}" };

        _logger.LogInformation("Manual hydration of tier {Tier} requested ({TaskCount} tasks)", request.Tier, tasksInTier.Count);

        try
        {
            foreach (var task in tasksInTier)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _taskRegistry.ForceRefreshAsync(task.TaskId, cancellationToken);
            }

            return new RunHydrationTierResponse { TaskCount = tasksInTier.Count };
        }
        catch (OperationCanceledException)
        {
            return new RunHydrationTierResponse { Cancelled = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual hydration of tier {Tier} failed", request.Tier);
            return new RunHydrationTierResponse { Success = false, ErrorMessage = "An unexpected error occurred during tier hydration" };
        }
    }
}
