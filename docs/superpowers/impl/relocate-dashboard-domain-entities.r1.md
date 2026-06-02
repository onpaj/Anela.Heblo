# Implementation: Relocate Dashboard Domain Entities to Domain Layer

## What was implemented

Moved `UserDashboardSettings`, `UserDashboardTile`, and `IUserDashboardSettingsRepository` from `Anela.Heblo.Xcc` to `Anela.Heblo.Domain/Features/Dashboard/`. Updated all consuming files' `using` directives. Updated the EF model snapshot's CLR type name strings. Deleted the original Xcc files.

This is a pure namespace/file relocation: no behavior, schema, DTO, or API surface changed.

## Files created/modified

**Created (new location):**
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs` — entity, namespace `Anela.Heblo.Domain.Features.Dashboard`, inherits `Entity<int>` via `using Anela.Heblo.Xcc.Domain`
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs` — entity, same namespace
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs` — interface, same namespace, no Xcc.Domain using (types are co-located)

**Deleted (original location):**
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs`
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs`
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs`

**Updated using directives (Application layer):**
- `SaveUserSettingsHandler.cs` — dropped `Xcc.Domain` + `Xcc.Services.Dashboard`, added `Domain.Features.Dashboard`
- `GetUserSettingsHandler.cs` — dropped `Xcc.Domain`, kept `Xcc.Services.Dashboard` (for `ITileRegistry`), added `Domain.Features.Dashboard`
- `EnableTileHandler.cs` — dropped `Xcc.Domain` + `Xcc.Services.Dashboard`, added `Domain.Features.Dashboard`
- `DisableTileHandler.cs` — dropped `Xcc.Services.Dashboard`, added `Domain.Features.Dashboard`

**Updated using directives (Persistence layer):**
- `UserDashboardSettingsConfiguration.cs` — `Xcc.Domain` → `Domain.Features.Dashboard`
- `UserDashboardTileConfiguration.cs` — `Xcc.Domain` → `Domain.Features.Dashboard`
- `UserDashboardSettingsRepository.cs` — both Xcc imports → `Domain.Features.Dashboard`
- `ApplicationDbContext.cs` — `Xcc.Domain` → `Domain.Features.Dashboard`
- `PersistenceModule.cs` — `Xcc.Services.Dashboard` → `Domain.Features.Dashboard`

**Updated model snapshot:**
- `Migrations/ApplicationDbContextModelSnapshot.cs` — 5 CLR type name strings updated from `Anela.Heblo.Xcc.Domain.UserDashboard*` to `Anela.Heblo.Domain.Features.Dashboard.UserDashboard*`

**Updated using directives (Test layer):**
- `SaveUserSettingsHandlerTests.cs` — both Xcc imports → `Domain.Features.Dashboard`
- `GetUserSettingsHandlerTests.cs` — `Xcc.Domain` removed, `Xcc.Services.Dashboard` kept (for `ITileRegistry`), added `Domain.Features.Dashboard`
- `EnableTileHandlerTests.cs` — both Xcc imports → `Domain.Features.Dashboard`
- `DisableTileHandlerTests.cs` — both Xcc imports → `Domain.Features.Dashboard`

**Not modified (no Dashboard entity references):**
- `GetAvailableTilesHandler.cs`, `GetTileDataHandler.cs`, `DashboardModule.cs` — use only tile registry types that stay in Xcc
- `DashboardControllerTests.cs`, `UserDashboardSettingsLockTests.cs`, `GetTileDataHandlerTests.cs` — no Dashboard entity types referenced
- Historical migration `.Designer.cs` files — left unchanged (frozen model state)
- All project `.csproj` files — no new project references needed

## Tests

All existing Dashboard handler tests updated with correct using directives:
- `test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/GetUserSettingsHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs`

No tests were added, deleted, or semantically modified.

## How to verify

```bash
cd backend

# 1. Stale reference check (must return 0 matches, excluding .Designer.cs)
grep -rn "Anela\.Heblo\.Xcc\.Domain\.UserDashboard\|Anela\.Heblo\.Xcc\.Services\.Dashboard\.IUserDashboardSettings" \
  --include="*.cs" src/Anela.Heblo.Application src/Anela.Heblo.Persistence test \
  | grep -v "\.Designer\.cs:"

# 2. Build
dotnet build

# 3. Format
dotnet format --verify-no-changes

# 4. Dashboard tests
dotnet test --filter "FullyQualifiedName~Dashboard"

# 5. (Optional) EF no-op probe — should report no changes
dotnet ef migrations add _Probe --no-build --dry-run \
  --project src/Anela.Heblo.Persistence \
  --startup-project src/Anela.Heblo.API 2>&1 | grep -i "no changes"
```

## Notes

- `GetUserSettingsHandler.cs` and `GetUserSettingsHandlerTests.cs` intentionally retain `using Anela.Heblo.Xcc.Services.Dashboard;` because they reference `ITileRegistry`, which stays in Xcc.
- The `Domain.csproj → Xcc.csproj` project reference (for `Entity<T>`) already existed. No new project references were added.
- `Xcc.csproj` has no reference to `Domain` — the forbidden direction was never introduced.

## PR Summary

Relocate Dashboard domain entities (`UserDashboardSettings`, `UserDashboardTile`, `IUserDashboardSettingsRepository`) from the `Anela.Heblo.Xcc` cross-cutting concerns project to `Anela.Heblo.Domain/Features/Dashboard/`, aligning the Dashboard module with the documented Clean Architecture convention used by every other feature module.

This is a pure compile-time relocation: no behavior, schema, DTO, or API surface changes. The EF model snapshot is updated to reflect the CLR type name change; no database migration is required.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/` — new folder with three files (entities + repository interface)
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs` — deleted
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs` — deleted
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs` — deleted
- 4 Application handlers — `using` directives updated
- 5 Persistence files — `using` directives updated
- `ApplicationDbContextModelSnapshot.cs` — 5 CLR type name strings updated
- 4 test files — `using` directives updated

## Status
DONE
