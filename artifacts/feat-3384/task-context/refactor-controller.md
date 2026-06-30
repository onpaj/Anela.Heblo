### task: refactor-controller

Replace the fat controller body with a thin MediatR dispatcher. The controller stays on `ControllerBase` (not `BaseApiController`) because it returns raw `ActionResult` shapes that embed custom error objects — this matches the existing client contract and avoids a breaking change.

The `ILogger` field is kept because the controller still participates in per-request logging context that is currently used (the logger is used in the old controller; in the new version it is not needed — it can be removed, but is kept here to match the minimal-change mandate).

#### Updated `BackgroundRefreshController.cs`

Replace the full content of `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` with:

```csharp
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
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet("tasks")]
    public async Task<ActionResult> GetRegisteredTasks(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetBackgroundRefreshTasksRequest(), cancellationToken);
        return Ok(response.Tasks);
    }

    [HttpGet("tasks/{taskId}/history")]
    public async Task<ActionResult> GetTaskHistory(
        string taskId, [FromQuery] int maxRecords = 50, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetTaskHistoryRequest { TaskId = taskId, MaxRecords = maxRecords },
            cancellationToken);
        return Ok(response.History);
    }

    [HttpGet("history")]
    public async Task<ActionResult> GetAllHistory(
        [FromQuery] int maxRecords = 100, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetAllHistoryRequest { MaxRecords = maxRecords },
            cancellationToken);
        return Ok(response.History);
    }

    [HttpPost("tasks/{taskId}/force-refresh")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> ForceRefresh(string taskId, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new ForceRefreshTaskRequest { TaskId = taskId },
            cancellationToken);

        if (!response.Success)
        {
            return response.ErrorCode == Application.Shared.ErrorCodes.BackgroundRefreshTaskNotFound
                ? NotFound(new { Error = $"Task '{taskId}' not found or could not be refreshed" })
                : StatusCode(500, new { Error = "An unexpected error occurred during force refresh" });
        }

        return Ok(new { Message = response.Message });
    }

    [HttpPost("tiers/{tier}/run")]
    [FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]
    public async Task<ActionResult> RunHydrationTier(int tier, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new RunHydrationTierRequest { Tier = tier },
            cancellationToken);

        if (!response.Success)
        {
            return response.ErrorCode switch
            {
                Application.Shared.ErrorCodes.BackgroundRefreshTierNotFound =>
                    NotFound(new { Error = $"No enabled tasks found for tier {tier}" }),
                Application.Shared.ErrorCodes.BackgroundRefreshCancelled =>
                    StatusCode(499, new { Error = "Hydration was cancelled" }),
                _ => StatusCode(500, new { Error = "An unexpected error occurred during tier hydration" })
            };
        }

        return Ok(new { Message = response.Message });
    }

    [HttpGet("tasks/{taskId}/status")]
    public async Task<ActionResult> GetTaskStatus(
        string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetTaskStatusRequest { TaskId = taskId },
            cancellationToken);

        if (!response.Success)
            return NotFound(new { Error = $"Task '{taskId}' not found" });

        return Ok(response.Status);
    }
}
```

**Important:** The controller deliberately maps handler `ErrorCodes` back to the original raw `{ Error = "..." }` anonymous objects. This preserves the existing client contract exactly — the frontend and any MCP tooling already consume these shapes.

#### Add error codes to `ErrorCodes.cs`

In `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, add after the Authorization block (line ~413, after `AuthorizationDuplicateGroupName`):

```csharp
// BackgroundRefresh module errors (33XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
BackgroundRefreshTaskNotFound = 3301,
[HttpStatusCode(HttpStatusCode.NotFound)]
BackgroundRefreshTierNotFound = 3302,
[HttpStatusCode(HttpStatusCode.InternalServerError)]
BackgroundRefreshForceFailed = 3303,
[HttpStatusCode(HttpStatusCode.BadRequest)]
BackgroundRefreshCancelled = 3304,
```

#### Register the module in `ApplicationModule.cs`

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`:

1. Add using (top of file):
   ```csharp
   using Anela.Heblo.Application.Features.BackgroundRefresh;
   ```
2. In `AddApplicationServices`, after `services.AddBackgroundJobsModule();`:
   ```csharp
   services.AddBackgroundRefreshModule();
   ```

#### Delete old BackgroundJobs DTOs

After confirming no remaining references (run the grep below), delete:

```
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs
backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs
```

Confirm no references remain first:

```bash
grep -rn "BackgroundJobs\.Contracts\.RefreshTask\|BackgroundJobs/Contracts/RefreshTask" \
  backend/src --include="*.cs"
```

Expected output: no matches (or only the files being deleted themselves).

#### Build and format verification

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --verify-no-changes
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --verify-no-changes
```

All three commands must exit 0.
