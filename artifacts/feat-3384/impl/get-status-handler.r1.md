# Task: get-status-handler (r1)

## Summary
Created GetTaskStatus use case (query).

## Changes

### Created
- `UseCases/GetTaskStatus/GetTaskStatusRequest.cs` — `IRequest<GetTaskStatusResponse>` with `string TaskName`
- `UseCases/GetTaskStatus/GetTaskStatusResponse.cs` — `BaseResponse` with `bool Found`, `RefreshTaskStatusDto? Status`
- `UseCases/GetTaskStatus/GetTaskStatusHandler.cs` — queries `IBackgroundRefreshTaskRegistry.GetTaskStatus(taskName)`, returns Found=false if not registered

## Status: completed
