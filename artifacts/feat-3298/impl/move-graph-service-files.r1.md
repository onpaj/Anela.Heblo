# Implementation: move-graph-service-files

## What was implemented

Moved `GraphService` and `MockGraphService` concrete implementations from the Application project into the `Anela.Heblo.Adapters.Microsoft365` adapter project. The interface (`IGraphService`) was intentionally left in the Application project under `Features/UserManagement/Services/`. DI registrations were also migrated from `UserManagementModule.cs` (Application) to `Microsoft365AdapterServiceCollectionExtensions.cs` (Adapter) to keep the dependency direction clean: Adapter -> Application, never the reverse.

## Files created/modified

- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/GraphService.cs` — real Microsoft Graph implementation, namespace changed to `Anela.Heblo.Adapters.Microsoft365.UserManagement`, added `using Anela.Heblo.Application.Features.UserManagement.Services;` for `IGraphService`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/MockGraphService.cs` — mock implementation, same namespace change, added same using directive
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` — added `IGraphService` DI registration (both mock and real branches), added `using` for new `UserManagement` sub-namespace and `IGraphService` interface, aligned `"UseMockAuth"` string literal to use `ConfigurationConstants.USE_MOCK_AUTH`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — removed concrete `GraphService`/`MockGraphService` registrations and redundant `AddHttpClient("MicrosoftGraph")` call; removed now-unused `using` for `Services` namespace; left a comment pointing to the adapter layer for `IGraphService` registration

**Deleted:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/MockGraphService.cs`

**Untouched:**
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/IGraphService.cs` — still at original location

## Tests

No test files exist for `GraphService` or `MockGraphService` in this codebase; none were touched.

## How to verify

```bash
# Build the adapter project (must succeed with 0 errors)
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj

# Confirm IGraphService is NOT moved
ls backend/src/Anela.Heblo.Application/Features/UserManagement/Services/
# Expected: IGraphService.cs only

# Confirm concrete files are in the adapter project
ls backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/UserManagement/
# Expected: GraphService.cs  MockGraphService.cs
```

## Notes

- The DI registration migration was a necessary side effect: after the move, `UserManagementModule.cs` (Application) could no longer reference the concrete types without creating an inward dependency from Application -> Adapter, which would invert the intended dependency direction. The `IGraphService` registrations were therefore moved into `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()` which is already called from `Program.cs` after `AddUserManagement()`.
- The `AddHttpClient("MicrosoftGraph")` call that was in `UserManagementModule` was redundant — it was already registered in the adapter's `else` branch. It was removed from the Application module.
- `"UseMockAuth"` string literal in the adapter extensions was aligned to use `ConfigurationConstants.USE_MOCK_AUTH` (same value; consistency improvement only).

## PR Summary

Moves the two concrete Graph service implementations (`GraphService`, `MockGraphService`) out of the Application layer and into the `Anela.Heblo.Adapters.Microsoft365` adapter project where they architecturally belong — they depend on Microsoft Identity Web and make HTTP calls to Graph API, which are infrastructure concerns. The interface (`IGraphService`) remains in Application to preserve the inward-facing contract used by use case handlers.

DI registrations for both implementations were migrated from `UserManagementModule` (Application) to `Microsoft365AdapterServiceCollectionExtensions` (Adapter) to keep the dependency arrow pointing the right way.

### Changes

- `UserManagement/GraphService.cs` (created in Adapter) — real Graph implementation with updated namespace
- `UserManagement/MockGraphService.cs` (created in Adapter) — mock implementation with updated namespace
- `Microsoft365AdapterServiceCollectionExtensions.cs` — registers `IGraphService` (mock or real) based on auth config
- `UserManagementModule.cs` — removed concrete type registrations; now delegates IGraphService wiring to the adapter layer
- `Services/GraphService.cs` (deleted from Application)
- `Services/MockGraphService.cs` (deleted from Application)

## Status
DONE
