# Implementation: Relocate Dashboard Domain Entities from Xcc to Domain Layer

## What was implemented

Pure namespace/file relocation of three Dashboard-specific types from `Anela.Heblo.Xcc` to `Anela.Heblo.Domain/Features/Dashboard/`. No behavior, schema, API, or test logic changed. All call sites updated atomically.

## Files created/modified

### New files (Domain layer)
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs` — moved from Xcc; namespace `Anela.Heblo.Domain.Features.Dashboard`; still inherits `Entity<int>` from Xcc
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs` — moved from Xcc; same namespace
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs` — moved from Xcc; same namespace; no Xcc.Domain using needed (co-located types)

### Deleted (original Xcc files)
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs` — deleted
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs` — deleted
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs` — deleted

### Updated `using` directives (Application layer)
- `UseCases/SaveUserSettings/SaveUserSettingsHandler.cs` — swapped Xcc.Domain → Domain.Features.Dashboard
- `UseCases/GetUserSettings/GetUserSettingsHandler.cs` — added Domain.Features.Dashboard; retained Xcc.Services.Dashboard for `ITileRegistry`
- `UseCases/EnableTile/EnableTileHandler.cs` — swapped Xcc.Domain → Domain.Features.Dashboard
- `UseCases/DisableTile/DisableTileHandler.cs` — swapped Xcc.Domain → Domain.Features.Dashboard
- `UseCases/GetAvailableTiles/GetAvailableTilesHandler.cs` — no entity reference; unchanged
- `UseCases/GetTileData/GetTileDataHandler.cs` — no entity reference; unchanged
- `DashboardModule.cs` — no entity reference; unchanged (keeps Xcc.Services.Dashboard for `RegisterTile<>`)

### Updated `using` directives (Persistence layer)
- `Dashboard/UserDashboardSettingsRepository.cs` — swapped to Domain.Features.Dashboard
- `Dashboard/UserDashboardSettingsConfiguration.cs` — swapped to Domain.Features.Dashboard
- `Dashboard/UserDashboardTileConfiguration.cs` — swapped to Domain.Features.Dashboard
- `ApplicationDbContext.cs` — using Domain.Features.Dashboard for DbSet properties
- `PersistenceModule.cs` — using Domain.Features.Dashboard for DI registration

### Updated model snapshot (arch-review amendment)
- `Migrations/ApplicationDbContextModelSnapshot.cs` — 5 CLR type-name strings updated from `Anela.Heblo.Xcc.Domain.UserDashboard*` to `Anela.Heblo.Domain.Features.Dashboard.UserDashboard*`; historical `.Designer.cs` migration files left unchanged

### Updated tests
- `Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs` — swapped to Domain.Features.Dashboard
- `Tests/Features/Dashboard/GetUserSettingsHandlerTests.cs` — added Domain.Features.Dashboard; retained Xcc.Services.Dashboard for `ITileRegistry` mock
- `Tests/Features/Dashboard/EnableTileHandlerTests.cs` — swapped to Domain.Features.Dashboard
- `Tests/Features/Dashboard/DisableTileHandlerTests.cs` — swapped to Domain.Features.Dashboard

### Project references unchanged
- `Anela.Heblo.Domain.csproj` — existing `→ Anela.Heblo.Xcc` reference satisfies `Entity<int>`; no new entries
- `Anela.Heblo.Xcc.csproj` — no reference to Domain added

## Tests

Existing Dashboard handler test suite covers all relocated types:
- `SaveUserSettingsHandlerTests` — exercises `UserDashboardSettings`, `UserDashboardTile`, `IUserDashboardSettingsRepository`
- `GetUserSettingsHandlerTests` — exercises provisioning, `ITileRegistry`, `IUserDashboardSettingsRepository`
- `EnableTileHandlerTests` — exercises enable path, tile creation
- `DisableTileHandlerTests` — exercises disable path

No new tests written (spec FR-7 / Out of Scope: existing tests are sufficient).

## How to verify

```bash
# From backend/
dotnet build                                          # must produce 0 errors
dotnet format --verify-no-changes                    # must report clean
dotnet test --filter "FullyQualifiedName~Dashboard"  # 100% pass

# Stale-reference check (must return 0 lines):
grep -rn "Anela\.Heblo\.Xcc\.Domain\.UserDashboard\|Anela\.Heblo\.Xcc\.Services\.Dashboard\.IUserDashboard" \
  backend/ --include="*.cs" | grep -v "\.Designer\.cs"
```

## Notes

- `ApplicationDbContextModelSnapshot.cs` updated per arch-review Decision 3 — required to prevent phantom EF migration on next `dotnet ef migrations add`.
- `GetUserSettingsHandler.cs` retains `using Anela.Heblo.Xcc.Services.Dashboard;` because it depends on `ITileRegistry` and `TileMetadata`, which stay in Xcc by design.
- `DashboardModule.cs`, `GetAvailableTilesHandler.cs`, `GetTileDataHandler.cs` were not modified — they never referenced the moved types directly.

## PR Summary

Relocated `UserDashboardSettings`, `UserDashboardTile`, and `IUserDashboardSettingsRepository` from `Anela.Heblo.Xcc` into `Anela.Heblo.Domain/Features/Dashboard/` to fix the only module that placed feature-specific domain types in the cross-cutting concerns project.

This is a pure compile-time relocation: no behavior change, no schema change, no API change, no new project references. The `Anela.Heblo.Domain → Anela.Heblo.Xcc` edge that satisfies `Entity<int>` inheritance already existed. The `ApplicationDbContextModelSnapshot.cs` is updated so the next `dotnet ef migrations add` produces no phantom rename migration.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Dashboard/` — new folder with 3 relocated files
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs` — deleted
- `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs` — deleted
- `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/` — 2 handlers updated (`SaveUserSettings`, `GetUserSettings`, `EnableTile`, `DisableTile`)
- `backend/src/Anela.Heblo.Persistence/` — 5 files updated (repository, 2 EF configs, DbContext, PersistenceModule)
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — 5 CLR type-name strings updated
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/` — 4 test files updated

## Status
DONE
