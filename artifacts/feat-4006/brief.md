## Module / File
`backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksHandler.cs`

## Coverage
Line coverage: 12.5% (filter threshold: 60%)

## What's not tested
The private `MapToDto` method contains two conditional branches that control what gets returned to the UI:

1. **NextScheduledRun calculation** — set only when `task.Enabled == true` AND `lastExecution.CompletedAt != null`. Three of the four combinations (disabled task, enabled task with null CompletedAt, enabled task with no lastExecution at all) produce `null`, but none are asserted.
2. **LastExecution mapping** — only populated when `lastExecution != null`. The null branch is untested.

## Why it matters
If the compound condition regresses — e.g. `NextScheduledRun` gets populated for a disabled task, or omitted when both conditions are met — the background-tasks dashboard would silently show wrong "next run" timestamps. Since this is purely a mapping layer, the failure would be invisible until a user notices stale or missing times in the UI.

## Suggested approach
Unit tests for the `Handle` method using a mock `IBackgroundRefreshTaskRegistry`. Cover:
- Disabled task → `NextScheduledRun` is null
- Enabled task, no `lastExecution` → `NextScheduledRun` is null, `LastExecution` is null
- Enabled task, `lastExecution.CompletedAt` is null → `NextScheduledRun` is null
- Enabled task, `lastExecution.CompletedAt` set → `NextScheduledRun` equals `CompletedAt + RefreshInterval`
- Enabled task, `lastExecution` present → `LastExecution` is mapped (non-null)

Estimated effort: ~1 h.

---
_Filed by weekly coverage-gap routine on 2026-08-31. Based on CI run #33077392747 (ba8f5eef168e0058dae1787bf6bb9f53fdcdf472)._