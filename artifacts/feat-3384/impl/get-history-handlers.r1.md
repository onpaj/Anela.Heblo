# Task: get-history-handlers (r1)

## Summary
Created GetTaskHistory and GetAllHistory use cases (queries).

## Changes

### Created
- `UseCases/GetTaskHistory/GetTaskHistoryRequest.cs` — `IRequest<GetTaskHistoryResponse>` with `string TaskName`
- `UseCases/GetTaskHistory/GetTaskHistoryResponse.cs` — `BaseResponse` with `IReadOnlyList<RefreshTaskExecutionLogDto> History`
- `UseCases/GetTaskHistory/GetTaskHistoryHandler.cs` — queries `IBackgroundRefreshTaskRegistry.GetTaskHistory(taskName)`, returns NotFound if null
- `UseCases/GetAllHistory/GetAllHistoryRequest.cs` — `IRequest<GetAllHistoryResponse>` with no fields
- `UseCases/GetAllHistory/GetAllHistoryResponse.cs` — `BaseResponse` with `IReadOnlyList<RefreshTaskExecutionLogDto> History`
- `UseCases/GetAllHistory/GetAllHistoryHandler.cs` — queries `IBackgroundRefreshTaskRegistry.GetAllHistory()`, maps to DTOs

## Status: completed
