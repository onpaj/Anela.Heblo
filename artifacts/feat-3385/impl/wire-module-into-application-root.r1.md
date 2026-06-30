# Implementation: wire-module-into-application-root

## What was implemented
Added `BackgroundRefreshModule` registration to the application composition root (`ApplicationModule.cs`). This includes both the using directive and the service registration call, placed immediately after the existing `BackgroundJobs` entries to maintain ordering consistency.

## Files created/modified
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — added `using Anela.Heblo.Application.Features.BackgroundRefresh;` after the `BackgroundJobs` using, and added `services.AddBackgroundRefreshModule();` immediately after `services.AddBackgroundJobsModule();` in `AddApplicationServices`.

## Tests
None required.

## How to verify
Run: dotnet build backend/Anela.Heblo.sln

## Notes
No deviations. The using directive was inserted in alphabetical order between `BackgroundJobs` and `Bank`, which is also the natural position after the existing `BackgroundJobs` using line.

## Status
DONE
