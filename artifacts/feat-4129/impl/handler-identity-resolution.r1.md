# Implementation: handler-identity-resolution

## What was implemented

`UpsertFlagOverrideHandler` now resolves the acting user's display name itself via
`ICurrentUserService.GetCurrentUser().GetDisplayName()`, instead of trusting a value
stamped onto the request by the controller. `UpsertFlagOverrideRequest.UpdatedBy` was
removed accordingly (FR-1, FR-2).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs` — removed the `UpdatedBy` property; the request now carries only `Key` and `IsEnabled`.
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs` — added an `ICurrentUserService` constructor dependency; `Handle()` now resolves `updatedBy` via `_currentUserService.GetCurrentUser().GetDisplayName()` and passes it to `_repo.UpsertAsync(...)`. Not-found branch and cache invalidation are unchanged.
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UpsertFlagOverrideHandlerTests.cs` (new) — 3 tests: authenticated user persists resolved display name, unauthenticated current-user persists the `"System"` fallback, unknown key returns `ResourceNotFound` and never calls the repo.
- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — minimal compile-fix only: removed the `UpdatedBy = updatedBy,` line from the `UpsertFlagOverrideRequest` object initializer in `Put()`, since the property no longer exists. This was required for the solution to build at all (`Anela.Heblo.Tests.csproj` has a direct `ProjectReference` to `Anela.Heblo.API.csproj`, so the whole solution must compile for even this task's own unit tests to run). The rest of `Put()` (the `User.Identity?.Name` resolution, the null-warning log, and the now-unused `updatedBy` local) is deliberately left untouched — that full simplification is explicitly task `controller-simplification`'s scope per the task plan, and is the very next unit. The unused local currently produces a CS0219 warning only (no `TreatWarningsAsErrors` in this repo), which the next task removes.

## Tests

- `UpsertFlagOverrideHandlerTests.cs` — all 3 new tests pass.
- Full `FeatureFlags` filter (`dotnet test --filter "FullyQualifiedName~FeatureFlags"`) — 13/13 pass (includes `FeatureFlagsControllerLintTests`, `FeatureFlagRegistryFrontendMirrorTests`, `HebloFeatureProviderTests`, and the 3 new tests). No regressions.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UpsertFlagOverrideHandlerTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlags"
```

## Notes

- Deviation from the task-context's literal file list: the task-context scoped this task to `UpsertFlagOverrideRequest.cs` + `UpsertFlagOverrideHandler.cs` + the new test only, and left the controller for the second task (`controller-simplification`). Because the test project references the API project directly, removing `UpdatedBy` from the request without touching the controller leaves the whole solution (and this task's own tests) unable to build. The one-line fix above (dropping the now-invalid initializer entry) is the minimum needed to keep the build green for this unit; it does not perform any of the broader `Put()` simplification that `controller-simplification` still needs to do (removing the `User.Identity` read, the warning log, and the unused `updatedBy` local remain that task's job).
- Constructor parameter order on `UpsertFlagOverrideHandler` is `(repo, cache, currentUserService)`, matching the task-context's test fixture exactly.
- No OpenAPI/TypeScript client regeneration needed — `UpdatedBy` was never on the client-facing `UpsertFlagOverrideBodyDto`.

## PR Summary
Moves `UpdatedBy` identity resolution for feature-flag overrides out of the controller and into `UpsertFlagOverrideHandler`, per ADR-005. The handler now injects `ICurrentUserService` and resolves the acting user's display name itself instead of trusting a value stamped on the request; the request DTO no longer carries an identity/audit field. Added unit tests covering the authenticated, unauthenticated-fallback, and unknown-key cases.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideRequest.cs` — dropped `UpdatedBy`
- `backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/UpsertFlagOverride/UpsertFlagOverrideHandler.cs` — resolves identity via `ICurrentUserService`
- `backend/test/Anela.Heblo.Tests/Features/FeatureFlags/UpsertFlagOverrideHandlerTests.cs` — new handler tests
- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — removed the now-invalid `UpdatedBy` initializer line (compile-fix only; full controller simplification is the next task)

## Status
DONE
