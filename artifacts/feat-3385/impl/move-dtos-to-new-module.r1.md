# Implementation: move-dtos-to-new-module

## What was implemented

Moved three RefreshTask DTOs from `BackgroundJobs.Contracts` to `BackgroundRefresh.Contracts`, updating only the namespace declaration. All properties, field types, and class structures are identical to the originals.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskDto.cs` — RefreshTaskDto class, namespace updated to BackgroundRefresh.Contracts
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs` — RefreshTaskStatusDto class, namespace updated to BackgroundRefresh.Contracts
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs` — RefreshTaskExecutionLogDto class, namespace updated to BackgroundRefresh.Contracts
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs` — deleted

## Tests

None required.

## How to verify

1. Confirm new files exist under `BackgroundRefresh/Contracts/` with namespace `Anela.Heblo.Application.Features.BackgroundRefresh.Contracts`.
2. Confirm old files under `BackgroundJobs/Contracts/` no longer exist.
3. Run `dotnet build` from the backend solution root to verify compilation succeeds (callers of these DTOs will need their own namespace updates in subsequent tasks).

## Notes

No deviations. Only the namespace line was changed; all class members are identical to the originals.

## Status

DONE
