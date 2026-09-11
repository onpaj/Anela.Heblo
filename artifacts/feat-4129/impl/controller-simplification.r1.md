# Implementation: controller-simplification

## What was implemented

`FeatureFlagsController.Put()` is now a straight-through expression-bodied dispatcher,
matching the style already used by `Get`, `GetAdmin`, and `Delete` in the same file. The
identity-resolution code left over from before task `handler-identity-resolution` (the
`User.Identity?.Name` read, the null-warning log, and the now-unused `updatedBy` local) is
removed entirely (FR-3).

## Files modified

- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — `Put()` body
  replaced with the exact expression-bodied dispatcher from the task-context's Step 2
  snippet. No other method in the file was touched.

## Tests

- `dotnet build Anela.Heblo.sln` — 0 errors, 93 pre-existing warnings (none new; no
  remaining reference to `User.Identity`, `Logger.LogWarning` or `updatedBy` in this file).
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlagsControllerLintTests"` — PASS (1/1), confirming the reflection-based lint test still targets `Put` correctly after the body change.
- `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlags" --no-build` — PASS (13/13), no regressions across `FeatureFlagsControllerLintTests`, `FeatureFlagRegistryFrontendMirrorTests`, `HebloFeatureProviderTests`, and `UpsertFlagOverrideHandlerTests`.
- `dotnet format Anela.Heblo.sln --verify-no-changes --include backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — no formatting violations.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FeatureFlags"
```

## Notes

- This completes the ADR-005 convergence for `FeatureFlagsController`: identity is now
  resolved exclusively inside `UpsertFlagOverrideHandler` (task 1), and the controller no
  longer references `User.Identity` anywhere in this file.
- No OpenAPI/TypeScript client regeneration needed — the controller's route, verbs, and
  response DTOs are unchanged; only the method body was simplified.

## PR Summary
Removes the leftover identity-resolution code from `FeatureFlagsController.Put()` now that
`UpsertFlagOverrideHandler` resolves `UpdatedBy` itself (task `handler-identity-resolution`).
`Put()` is now a straight-through expression-bodied dispatcher consistent with the other
actions in this controller, completing the ADR-005 compliance fix for issue #4129.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/FeatureFlagsController.cs` — simplified `Put()` to an expression-bodied dispatcher; removed `User.Identity` read, warning log, and unused `updatedBy` local.

## Status
DONE
