# Implementation: create-adapter-project

## What was implemented

Extracted `OrgChartService` from the Application layer's `Infrastructure` sub-folder into a new dedicated class-library project `Anela.Heblo.Adapters.OrgChart`. The DI registration (`AddHttpClient<IOrgChartService, OrgChartService>`) was moved from `OrgChartModule.cs` (Application layer) into a new extension class `OrgChartAdapterServiceCollectionExtensions` in the adapter project. `Program.cs` now calls `AddOrgChartAdapter` alongside the other adapters. The old `Infrastructure/` directory in the Application layer has been removed.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/Anela.Heblo.Adapters.OrgChart.csproj` — new class-library project targeting net8.0, referencing Microsoft.Extensions.Http, Microsoft.Extensions.Options.ConfigurationExtensions, and the Application project
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartService.cs` — moved from Application/Features/OrgChart/Infrastructure/; only the namespace changed to `Anela.Heblo.Adapters.OrgChart`; all logic preserved verbatim
- `backend/src/Adapters/Anela.Heblo.Adapters.OrgChart/OrgChartAdapterServiceCollectionExtensions.cs` — new DI extension class; registers `IOrgChartService` via `AddHttpClient`
- `backend/src/Anela.Heblo.Application/Features/OrgChart/OrgChartModule.cs` — removed Infrastructure using, removed `AddHttpClient` call, kept options registration
- `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/OrgChartService.cs` — DELETED
- `backend/src/Anela.Heblo.Application/Features/OrgChart/Infrastructure/` — DELETED (directory)
- `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — added ProjectReference to new adapter
- `backend/src/Anela.Heblo.API/Program.cs` — added `using Anela.Heblo.Adapters.OrgChart;` and `AddOrgChartAdapter` call after `AddMicrosoft365Adapter`
- `Anela.Heblo.sln` — added project block, build configuration entries, and NestedProjects entry for new adapter (GUID `{C7A82E5F-3D91-4B06-A2F8-9E0C1D5B4738}`)
- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` — updated using from `Anela.Heblo.Application.Features.OrgChart.Infrastructure` to `Anela.Heblo.Adapters.OrgChart`
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — added ProjectReference to new adapter (required by OrgChartServiceTests)

## Tests

- `backend/test/Anela.Heblo.Tests/Features/OrgChart/OrgChartServiceTests.cs` — existing tests for OrgChartService; updated only the using directive to point at the new namespace. No new tests added — pure structural refactor.

## How to verify

```bash
cd /home/user/worktrees/feature-3192-orgchart-service-to-adapter
dotnet build Anela.Heblo.sln     # should succeed with 0 errors
dotnet format Anela.Heblo.sln --verify-no-changes   # should exit 0
```

## Notes

- The existing `OrgChartServiceTests.cs` in `Anela.Heblo.Tests` referenced the old `Infrastructure` namespace; it was updated to use `Anela.Heblo.Adapters.OrgChart`. The test project's csproj was also updated with a direct ProjectReference to the new adapter.
- No runtime behaviour changes: the same `AddHttpClient<IOrgChartService, OrgChartService>()` registration is used; the `OrgChartOptions` binding remains in `OrgChartModule.AddOrgChartServices`.

## PR Summary

Extracts the HttpClient-based `OrgChartService` from the Application layer into a new dedicated `Anela.Heblo.Adapters.OrgChart` project, satisfying Clean Architecture's requirement that the Application layer must not carry infrastructure (HTTP) dependencies. No runtime behaviour change.

### Changes

- `Anela.Heblo.Adapters.OrgChart/` — new adapter project (csproj, OrgChartService.cs, OrgChartAdapterServiceCollectionExtensions.cs)
- `OrgChartModule.cs` — removed `AddHttpClient` call and `Infrastructure` using
- `Infrastructure/OrgChartService.cs` — deleted
- `Program.cs` — registers `AddOrgChartAdapter`
- `Anela.Heblo.API.csproj` — references new adapter
- `Anela.Heblo.sln` — includes new project
- `OrgChartServiceTests.cs` / `Anela.Heblo.Tests.csproj` — updated to new namespace/reference

## Status
DONE
