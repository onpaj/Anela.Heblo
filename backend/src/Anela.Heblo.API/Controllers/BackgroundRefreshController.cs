using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetAllHistory;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetBackgroundRefreshTasks;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskHistory;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.GetTaskStatus;
using Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Admin_Administration)]
[ApiController]
[Route("api/[controller]")]
public class BackgroundRefreshController : ControllerBase
{
    private readonly IMediator _mediator;

    public BackgroundRefreshController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("tasks")]
    public async Task<ActionResult<IEnumerable<RefreshTaskDto>>> GetRegisteredTasks(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBackgroundRefreshTasksRequest(), cancellationToken);
        return Ok(result.Tasks);
    }

    [HttpGet("tasks/{taskId}/history")]
    public async Task<ActionResult<IEnumerable<RefreshTaskExecutionLogDto>>> GetTaskHistory(
        string taskId,
        [FromQuery] int maxRecords = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetTaskHistoryRequest { TaskId = taskId, MaxRecords = maxRecords }, cancellationToken);
        return Ok(result.History);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<RefreshTaskExecutionLogDto>>> GetAllHistory(
        [FromQuery] int maxRecords = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAllHistoryRequest { MaxRecords = maxRecords }, cancellationToken);
        return Ok(result.History);
    }

    [HttpPost("tasks/{taskId}/force-refresh")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> ForceRefresh(string taskId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ForceRefreshTaskRequest { TaskId = taskId }, cancellationToken);
        if (result.NotFound) return NotFound(new { Error = result.ErrorMessage });
        if (!result.Success) return StatusCode(500, new { Error = result.ErrorMessage });
        return Ok(new { Message = $"Task '{taskId}' refresh initiated successfully" });
    }

    [HttpPost("tiers/{tier}/run")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> RunHydrationTier(int tier, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RunHydrationTierRequest { Tier = tier }, cancellationToken);
        if (result.NotFound) return NotFound(new { Error = result.ErrorMessage });
        if (result.Cancelled) return StatusCode(499, new { Error = "Hydration was cancelled" });
        if (!result.Success) return StatusCode(500, new { Error = result.ErrorMessage });
        return Ok(new { Message = $"Tier {tier} hydration completed ({result.TaskCount} tasks)" });
    }

    [HttpGet("tasks/{taskId}/status")]
    public async Task<ActionResult<RefreshTaskStatusDto>> GetTaskStatus(string taskId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTaskStatusRequest { TaskId = taskId }, cancellationToken);
        if (!result.Found) return NotFound(new { Error = $"Task '{taskId}' not found" });
        return Ok(result.Status);
    }
}
