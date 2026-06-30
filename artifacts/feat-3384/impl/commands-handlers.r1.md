# Task: commands-handlers (r1)

## Summary
Created ForceRefreshTask and RunHydrationTier command use cases.

## Changes

### Created
- `UseCases/ForceRefreshTask/ForceRefreshTaskRequest.cs` — `IRequest<ForceRefreshTaskResponse>` with `string TaskName`
- `UseCases/ForceRefreshTask/ForceRefreshTaskResponse.cs` — `BaseResponse` with `bool NotFound`, `string? ErrorMessage`
- `UseCases/ForceRefreshTask/ForceRefreshTaskHandler.cs` — calls `IBackgroundRefreshTaskRegistry.ForceRefreshAsync(taskName)`, handles not-found and error cases
- `UseCases/RunHydrationTier/RunHydrationTierRequest.cs` — `IRequest<RunHydrationTierResponse>` with `string TierName`, `bool Force`
- `UseCases/RunHydrationTier/RunHydrationTierResponse.cs` — `BaseResponse` with `bool NotFound`, `bool Cancelled`, `string? ErrorMessage`, `int TaskCount`
- `UseCases/RunHydrationTier/RunHydrationTierHandler.cs` — calls `IBackgroundRefreshTaskRegistry.RunHydrationTierAsync(tierName, force)`, captures task count

## Status: completed
