using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskHandler : IRequestHandler<ForceRefreshTaskRequest, ForceRefreshTaskResponse>
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    private readonly ILogger<ForceRefreshTaskHandler> _logger;

    public ForceRefreshTaskHandler(IBackgroundRefreshTaskRegistry taskRegistry, ILogger<ForceRefreshTaskHandler> logger)
    {
        _taskRegistry = taskRegistry;
        _logger = logger;
    }

    public async Task<ForceRefreshTaskResponse> Handle(ForceRefreshTaskRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Force refresh requested for task '{TaskId}' by user", request.TaskId);
            await _taskRegistry.ForceRefreshAsync(request.TaskId, cancellationToken);
            return new ForceRefreshTaskResponse { Success = true };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Force refresh failed for task '{TaskId}': {Error}", request.TaskId, ex.Message);
            return new ForceRefreshTaskResponse { NotFound = true, ErrorMessage = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during force refresh of task '{TaskId}'", request.TaskId);
            return new ForceRefreshTaskResponse { Success = false, ErrorMessage = "An unexpected error occurred during force refresh" };
        }
    }
}
