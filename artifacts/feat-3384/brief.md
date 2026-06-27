## Module
BackgroundJobs (adjacent: BackgroundRefresh infrastructure)

## Finding
`BackgroundRefreshController` (`backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs`) contains business logic directly in the controller without using MediatR, violating the documented rule that "Business logic must be in MediatR handlers, NOT in controllers."

Specific violations:

1. **Object mapping in the controller** — two private `MapToDto` methods (lines 145–178) convert Xcc domain objects (`RefreshTaskConfiguration`, `RefreshTaskExecutionLog`) to DTOs. This mapping is business logic that belongs in a handler.

2. **Filtering and sequential execution loop in `RunHydrationTier`** (lines 84–118):
   ```csharp
   var tasksInTier = _taskRegistry.GetRegisteredTasks()
       .Where(t => t.HydrationTier == tier && t.Enabled)
       .OrderBy(t => t.TaskId)
       .ToList();
   ...
   foreach (var task in tasksInTier)
   {
       await _taskRegistry.ForceRefreshAsync(task.TaskId, cancellationToken);
   }
   ```
   This is orchestration and business logic embedded in the controller layer.

3. **No MediatR at all** — the controller injects `IBackgroundRefreshTaskRegistry` directly rather than dispatching through `IMediator`, bypassing the architectural pattern used by every other controller in the project.

## Why it matters
The consistent pattern across all other controllers is `controller → MediatR → handler → service`. `BackgroundRefreshController` breaks this contract, making it untestable at the handler layer and inconsistent with the rest of the codebase. `RunHydrationTier`'s loop logic has no unit tests (handlers are testable in isolation; controllers typically are not).

## Suggested fix
Extract each endpoint into a MediatR use case under a new `BackgroundRefresh` module (or within the existing `BackgroundJobs` slice if the two are considered one concern):

- `GetBackgroundRefreshTasksQuery` / `GetBackgroundRefreshTasksHandler`
- `RunHydrationTierCommand` / `RunHydrationTierHandler` (moves the filter + loop into the handler)
- `ForceRefreshTaskCommand` / `ForceRefreshTaskHandler`

The controller then becomes a thin mediator dispatcher, consistent with all other controllers.

---
_Filed by daily arch-review routine on 2026-06-26._
