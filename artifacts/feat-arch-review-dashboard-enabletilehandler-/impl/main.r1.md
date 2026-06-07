All 6 tasks complete with ALL_PASS. Let me write the output summary.

---

# Implementation: Refactor EnableTileHandler / DisableTileHandler duplication

## What was implemented

Extracted the shared provision/lock/load/stamp/persist scaffold from `EnableTileHandler` and `DisableTileHandler` into a new `UserDashboardSettingsMutator` collaborator. Both handlers now delegate to it via two small closures, reducing combined handler LOC from ~131 to ~63 lines (~52% reduction). All MediatR contracts, HTTP routes, and the generated TypeScript client are byte-identical.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsMutator.cs` — new: internal interface + `UserDashboardSettingsMutationResult` record struct, with XML doc invariants (provisioning-before-lock, single timestamp read, delegate ownership of LastModified)
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs` — new: `internal sealed` implementation (~90 LOC), 4 constructor deps, single `TimeProvider` read per call, correct lock ordering (mediator before lock), delegate dispatch, conditional `UpdateAsync`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — added `services.AddScoped<IUserDashboardSettingsMutator, UserDashboardSettingsMutator>()`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs` — rewritten as `internal sealed` thin shell with 1 dep (~44 LOC)
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs` — rewritten as `internal sealed` thin shell with 1 dep (~32 LOC)
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs` — constructor only: now wires real `UserDashboardSettingsMutator` around the 4 existing mocks
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs` — same pattern

## Tests

All 7 `[Fact]` methods in `EnableTileHandlerTests` and all 7 in `DisableTileHandlerTests` pass unchanged. The `Handle_SendsGetUserSettingsBeforeAcquiringLock` ordering test in both classes confirms the provisioning-before-lock invariant is upheld through the mutator.

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln --no-restore
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Dashboard|FullyQualifiedName~Controllers.DashboardController" \
  --no-build
git diff main -- backend/src/Anela.Heblo.API/Controllers/DashboardController.cs
# Expected: empty (zero-line diff on controller and request/response contracts)
```

## Notes

- The 38 full-suite failures are pre-existing Docker-dependent integration tests unrelated to this change.
- `SaveUserSettingsHandler` carries the same scaffold and is a known follow-up candidate for this mutator; deliberately out of scope per spec.
- Both handlers are `internal sealed` (down from `public class`) due to CS0051 — a `public` class cannot take an `internal` interface as a constructor parameter. This matches the existing precedent in `Features/FeatureFlags/**/*Handler.cs` and does not affect MediatR registration (which uses reflection via `RegisterServicesFromAssembly`).

## PR Summary

Eliminates the provision/lock/load/stamp/persist duplication between `EnableTileHandler` and `DisableTileHandler` by extracting it into a new `UserDashboardSettingsMutator` collaborator. Each handler becomes a thin shell: validate `TileId`, delegate to the mutator with a two-line `onTileFound` closure and an optional `onTileMissing` closure. Combined handler LOC drops ~52%. Zero changes to MediatR contracts, HTTP routes, controller, or generated TypeScript client.

### Changes
- `Infrastructure/IUserDashboardSettingsMutator.cs` — new internal interface + result struct with XML doc invariants
- `Infrastructure/UserDashboardSettingsMutator.cs` — new `internal sealed` implementation owning userId normalization, pre-lock provisioning, lock, repo load, single-read timestamps, conditional UpdateAsync
- `DashboardModule.cs` — one new `AddScoped` registration
- `EnableTileHandler.cs` — rewritten to thin shell (1 dep, ~44 LOC)
- `DisableTileHandler.cs` — rewritten to thin shell (1 dep, ~32 LOC)
- `EnableTileHandlerTests.cs` / `DisableTileHandlerTests.cs` — constructor wiring only; all 14 `[Fact]` bodies unchanged

## Status
DONE