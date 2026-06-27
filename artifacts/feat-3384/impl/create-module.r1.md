# Task: create-module (r1)

## Summary
Created the BackgroundRefresh module structure with contracts, module registration, and moved DTOs.

## Changes

### Created
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/BackgroundRefreshModule.cs` — module registration (no-op since MediatR handlers are auto-scanned)
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskDto.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs`

### Deleted
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskDto.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskStatusDto.cs`
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs`

### Modified
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — registered `AddBackgroundRefreshModule()`

## Status: completed
