# Task: get-tasks-handler (r1)

## Summary
Created GetBackgroundRefreshTasks use case (query).

## Changes

### Created
- `UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksRequest.cs` — `IRequest<GetBackgroundRefreshTasksResponse>` with no fields
- `UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksResponse.cs` — `BaseResponse` subclass with `IReadOnlyList<RefreshTaskDto> Tasks`
- `UseCases/GetBackgroundRefreshTasks/GetBackgroundRefreshTasksHandler.cs` — queries `IBackgroundRefreshTaskRegistry.GetAllTasks()`, maps to DTOs

## Status: completed
