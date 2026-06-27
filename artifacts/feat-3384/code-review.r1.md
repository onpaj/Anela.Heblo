# Code Review — feat-3384: BackgroundRefreshController MediatR Refactor

Reviewer: Claude Code (r1)
Date: 2026-06-27

---

## Summary

The primary goal — extracting business logic from `BackgroundRefreshController` into dedicated MediatR use cases — has been achieved correctly. The controller is now a pure dispatcher. Module registration, handler patterns, and the `IRequestHandler<TRequest, TResponse>` structure all follow codebase conventions. Six issues are noted below; one is a correctness deviation from the plan that is actually an improvement.

---

## Findings

### F-1 — IMPORTANT: `ForceRefreshTaskResponse` and `RunHydrationTierResponse` declare a redundant `ErrorMessage` property

**Files:**
- `UseCases/ForceRefreshTask/ForceRefreshTaskResponse.cs` line 8
- `UseCases/RunHydrationTier/RunHydrationTierResponse.cs` line 9

Both response classes inherit `BaseResponse` (which carries `ErrorCodes? ErrorCode` and `Dictionary<string, string>? Params`) and then add a free-text `string? ErrorMessage`. The `BaseResponse` contract is designed for structured error codes, not raw strings. Adding a parallel `ErrorMessage` property bypasses the structured error channel and results in two different error-communication mechanisms on the same response object.

The controller reads `result.ErrorMessage` when deciding the 500 body. This means the `ErrorCode` / `Params` fields on `BaseResponse` are always `null` on error paths in these handlers, making `BaseResponse` partially vestigial for the command responses.

**Fix options (pick one):**
1. Use `BaseResponse.Params` instead: set `ErrorCode = ErrorCodes.Exception` and place the message in `Params["ErrorMessage"]`. The controller already has the `FullError()` helper available.
2. Drop `ErrorMessage` from the response and have the controller construct its error body directly from the caught exception message passed through a structured `ErrorCodes` value. This matches the pattern in `UpdateRecurringJobCronResponse` / `TriggerRecurringJobResponse`.

Either fix eliminates the dual-channel problem. The `GetTaskHistory`, `GetAllHistory`, and `GetBackgroundRefreshTasks` query responses do not have this issue.

---

### F-2 — IMPORTANT: Plan deviation — Response classes inherit `BaseResponse` but plan specified plain classes

The original spec (`task-plan.r1.md`, tasks `commands-handlers` and others) specified plain response classes with a self-contained `bool Success` property — for example:

```csharp
public class ForceRefreshTaskResponse
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public string? ErrorMessage { get; init; }
}
```

The implementation instead inherits `BaseResponse` across all six response classes.

**Assessment:** This deviation is an improvement, not a problem. The `ErrorHandlingTests.ResponseClass_ShouldInheritFromBaseResponse` arch test (`backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs` lines 31–38) scans all `*Response` classes in the application assembly and asserts `IsSubclassOf(typeof(BaseResponse))`. Had the plan been followed literally, all six new response classes would have failed this arch test at the next test run. The implementer was correct to override the plan.

No action required other than acknowledging this deviation was justified and that F-1 above (the `ErrorMessage` duplication it introduced) needs resolution.

---

### F-3 — IMPORTANT: `GetTaskHistory` returns HTTP 200 with an empty list for an unknown `taskId`

**File:** `UseCases/GetTaskHistory/GetTaskHistoryHandler.cs`

`IBackgroundRefreshTaskRegistry.GetExecutionHistory(taskId, maxRecords)` filters the in-memory log by `taskId` and returns an empty list when no matching records exist — it does not distinguish between "this task exists but has never run" and "this task does not exist." `GetTaskHistoryHandler` propagates this behaviour without checking task registration, so `GET /api/BackgroundRefresh/tasks/{unknownId}/history` returns `200 []` rather than `404`.

By contrast, `GetTaskStatusHandler` explicitly calls `GetRegisteredTasks().FirstOrDefault(t => t.TaskId == request.TaskId)` and signals `Found = false`, producing a `404`. The behaviour is inconsistent between sibling endpoints.

**Fix:** Add a pre-flight existence check in `GetTaskHistoryHandler`:

```csharp
var isRegistered = _taskRegistry.GetRegisteredTasks().Any(t => t.TaskId == request.TaskId);
if (!isRegistered)
    return Task.FromResult(new GetTaskHistoryResponse { Found = false });
```

Add a corresponding `bool Found { get; set; } = true;` property to `GetTaskHistoryResponse` and handle it in the controller with `NotFound(...)`, mirroring the `GetTaskStatus` pattern. The same fix should be applied to `GetAllHistory` is not needed — its endpoint has no per-task scope.

---

### F-4 — IMPORTANT: `RunHydrationTierRequest.Tier` has no validation; `Tier = 0` is accepted silently

**File:** `UseCases/RunHydrationTier/RunHydrationTierRequest.cs` line 7

`int Tier { get; init; }` defaults to `0` if the caller omits the route segment or passes a non-integer. The handler treats `Tier = 0` as a valid tier number, queries for it, finds nothing, and returns a `NotFound` response with the message "No enabled tasks found for tier 0" rather than a validation error.

Valid tier values are determined by `HydrationTier` on registered tasks (observed values: `1` in `RefreshTaskConfiguration`). Tier `0` is not a real tier.

**Fix:** Add a `[Range(1, int.MaxValue)]` attribute on the `Tier` property in `RunHydrationTierRequest`, or add a FluentValidation validator (`RuleFor(x => x.Tier).GreaterThan(0)`). Either approach integrates with the existing `ValidationResultBehavior` pipeline.

---

### F-5 — IMPORTANT: `RefreshTaskStatusDto.Description` is always `null`

**File:** `UseCases/GetTaskStatus/GetTaskStatusHandler.cs` lines 23–29

`RefreshTaskStatusDto` declares `string? Description { get; init; }` but `GetTaskStatusHandler` never sets it. `RefreshTaskConfiguration` (the source object) has no `Description` property, so there is no available value to populate this field. The property was defined on the DTO but has no data source.

This is a dead field on the wire contract. Either:
- Remove `Description` from `RefreshTaskStatusDto` (cleanest), or
- Add a `Description` property to `RefreshTaskConfiguration` in Xcc if a description is actually needed.

Leaving it in silently serialises as `"description": null` in every API response, creating confusion for API consumers.

---

## Advisory

### A-1 — `MapToDto` for `RefreshTaskExecutionLog` is duplicated across three handlers

**Files:**
- `GetTaskHistoryHandler.cs` lines 25–35
- `GetAllHistoryHandler.cs` lines 25–35
- `GetTaskStatusHandler.cs` lines 34–44

All three are byte-for-byte identical. The same logic also appears inside `GetBackgroundRefreshTasksHandler.MapToExecutionLogDto`. Extracting this to a shared static class (e.g. `BackgroundRefresh/Contracts/RefreshTaskExecutionLogDtoMapper.cs`) would make future field additions (or a Status formatting change) a single-point edit. This is not a correctness issue since the logic is identical and the handlers are in separate use case folders, but it is a maintenance surface.

---

### A-2 — DTO accessor style diverges from the `BackgroundJobs` sibling module

**Files:** All three `Contracts/*.cs` files

The existing `BackgroundJobs` DTOs use `{ get; set; }` with `= string.Empty` defaults (e.g. `RecurringJobDto.cs`). The new `BackgroundRefresh` DTOs use `{ get; init; }` with `required` modifiers. Both compile and serialise correctly; `init` with `required` is more modern and prevents accidental mutation post-construction. However, the style inconsistency is worth noting if the codebase moves toward a uniform DTO style. No action required for this PR.

---

### A-3 — `GetBackgroundRefreshTasksResponse.Tasks` and the history `History` properties use `{ get; set; }` on the response object

**Files:**
- `GetBackgroundRefreshTasksResponse.cs` line 8
- `GetTaskHistoryResponse.cs` line 8
- `GetAllHistoryResponse.cs` line 8

These response properties are `set`-able, while `GetTaskStatusResponse.Found` and `.Status` are also `set`-able. The existing BackgroundJobs responses (`GetRecurringJobsListResponse`) also use `{ get; set; }`. This is consistent with codebase convention — noted only because the DTOs inside them use `init`. No change needed.

---

### A-4 — `ForceRefreshTaskHandler` catches `InvalidOperationException` to detect "task not found"

**File:** `UseCases/ForceRefreshTask/ForceRefreshTaskHandler.cs` lines 26–30

The handler distinguishes "task not found" from general failure by catching `InvalidOperationException`. This works because `BackgroundRefreshTaskRegistry.ForceRefreshAsync` throws `new InvalidOperationException($"Task with ID '{taskId}' is not registered")`. The coupling is implicit — nothing in the interface contract documents this. If the registry ever changes to a dedicated `TaskNotFoundException`, this catch block will silently stop catching "not found" and instead route to the generic 500 path. Consider adding a comment on the catch block citing the registry source, or checking existence before calling `ForceRefreshAsync` (using `GetRegisteredTasks().Any(...)`) to make the intent explicit.

---

## Checklist

| Item | Status |
|------|--------|
| Controller is a pure MediatR dispatcher | Pass |
| All 6 use cases implemented | Pass |
| DTOs moved to `BackgroundRefresh.Contracts` | Pass |
| Old DTO files removed from `BackgroundJobs.Contracts` | Pass |
| `AddBackgroundRefreshModule()` registered in `ApplicationModule` | Pass |
| All response classes inherit `BaseResponse` | Pass |
| No `bool Success` redeclared on response classes | Pass |
| `IReadOnlyList<T>` initialised with `= []` | Pass |
| No records used for DTOs | Pass |
| `ErrorMessage` duplicates `BaseResponse` error channel (F-1) | Fail |
| `GetTaskHistory` returns 200 for unknown task (F-3) | Fail |
| `RunHydrationTierRequest.Tier` unvalidated (F-4) | Fail |
| `RefreshTaskStatusDto.Description` always null (F-5) | Fail |
