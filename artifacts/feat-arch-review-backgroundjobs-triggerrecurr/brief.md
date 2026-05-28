## Module
BackgroundJobs

## Finding
`TriggerRecurringJobHandler` uses `ErrorCodes.RecurringJobNotFound` (1901) for three semantically distinct failure conditions:

| Line | Actual condition | Error code used |
|------|-----------------|-----------------|
| 39 | Job not registered in DI (genuinely not found) | `RecurringJobNotFound` ✅ |
| 57 | Job is found but disabled, `ForceDisabled=false` | `RecurringJobNotFound` ❌ |
| 74 | Job found and enabled, but `IHangfireJobEnqueuer.EnqueueJob` returned `null` | `RecurringJobNotFound` ❌ |

File: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/TriggerRecurringJob/TriggerRecurringJobHandler.cs`

The controller maps `!response.Success` → HTTP 404 (`NotFound`):

```csharp
// RecurringJobsController.cs lines 116-119
if (!response.Success)
{
    return NotFound(response);
}
```

So a disabled job or a failed enqueue both surface to the API consumer as `404 Not Found` with error code 1901, which is factually incorrect.

## Why it matters
API consumers (frontend, monitoring tools) cannot distinguish "job doesn't exist" from "job is disabled" or "Hangfire failed to enqueue". Correct HTTP semantics would be:
- Job disabled → `409 Conflict` or `422 Unprocessable Entity`
- Enqueue failure → `500 Internal Server Error`

Misusing a single error code also makes error monitoring ambiguous.

## Suggested fix
Add two new error codes to `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`:

```csharp
RecurringJobDisabled = 1903,
RecurringJobEnqueueFailed = 1904,
```

Return `RecurringJobDisabled` when the job is found-but-disabled, and `RecurringJobEnqueueFailed` when `EnqueueJob` returns `null`. Update the controller to check `response.ErrorCode` and return the appropriate HTTP status for each case.

---
_Filed by daily arch-review routine on 2026-05-27._