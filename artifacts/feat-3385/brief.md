## Module
BackgroundJobs

## Finding
Three DTOs live in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/` but they serve `BackgroundRefreshController` — a different controller backed by the Xcc `IBackgroundRefreshTaskRegistry` — not any BackgroundJobs use case:

- `RefreshTaskDto.cs`
- `RefreshTaskStatusDto.cs`
- `RefreshTaskExecutionLogDto.cs`

None of the BackgroundJobs MediatR handlers (`GetRecurringJobsListHandler`, `TriggerRecurringJobHandler`, `UpdateRecurringJobCronHandler`, `UpdateRecurringJobStatusHandler`) reference these types. Their only consumer is `BackgroundRefreshController.cs` (lines 26, 39, 62, 122, 134, 154–178).

The development guidelines state: "DTO objects for API (Request, Response) live in `contracts/` of the specific module." Placing BackgroundRefresh DTOs inside the BackgroundJobs module violates this ownership rule — BackgroundJobs now exposes contracts it does not own.

## Why it matters
A developer adding a new BackgroundJobs use case reads `BackgroundJobs/Contracts/` and finds three unrelated types. Any change to the BackgroundRefresh HTTP contract (e.g., adding a field to `RefreshTaskDto`) requires modifying the BackgroundJobs module, coupling two independent concerns. If BackgroundRefresh ever moves to its own module, the ownership is already wrong.

## Suggested fix
Move the three DTOs to where their single consumer is wired:
- Option A (preferred): Create a `BackgroundRefresh` module (`Application/Features/BackgroundRefresh/Contracts/`) and move the DTOs there. This also addresses the companion issue about `BackgroundRefreshController` needing MediatR handlers.
- Option B (minimal): If no full module is warranted, keep the DTOs alongside the Xcc infrastructure they represent — e.g., in a `Xcc/Services/BackgroundRefresh/Dto/` folder so the controller imports them from the same layer as `IBackgroundRefreshTaskRegistry`.

Either way, remove the three files from `BackgroundJobs/Contracts/`.

---
_Filed by daily arch-review routine on 2026-06-26._
