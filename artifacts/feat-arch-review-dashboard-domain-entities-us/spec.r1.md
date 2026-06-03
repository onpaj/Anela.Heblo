# Specification: Relocate Dashboard Domain Entities from Xcc to Domain Layer

## Summary
The `UserDashboardSettings` and `UserDashboardTile` entities, plus the `IUserDashboardSettingsRepository` interface, currently live in the `Anela.Heblo.Xcc` cross-cutting concerns project. They are Dashboard-feature-specific and must be relocated to `Anela.Heblo.Domain/Features/Dashboard/` to align with Clean Architecture layering and the module conventions used by every other feature in the codebase.

## Background
Per `docs/architecture/filesystem.md` and `docs/architecture/development_guidelines.md`, feature-specific domain entities and repository interfaces belong in `Anela.Heblo.Domain/Features/{Module}/`. The `Xcc` project is reserved for technical cross-cutting concerns (e.g. `Entity<T>`, `IEntity<T>`, tile registry infrastructure). The Dashboard module is the only module that places its feature-specific entities and repository contract in `Xcc`, which:

- Violates layering (domain feature types polluting cross-cutting infrastructure).
- Contradicts the documented rule "Use Xcc for technical concerns only" (CLAUDE.md).
- Couples the Application layer to `Xcc.Domain` for what should be Domain-layer dependencies.
- Creates avoidable rework when ADR-001 Phase 2 introduces per-module DbContexts, which will require these entities to be in their canonical Domain location anyway.

This change is a pure code relocation: no behavior changes, no schema changes, no API contract changes.

## Functional Requirements

### FR-1: Move `UserDashboardSettings` entity to Domain layer
Move `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs` to `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs`. Change the namespace from `Anela.Heblo.Xcc.Domain` to `Anela.Heblo.Domain.Features.Dashboard`. The class continues to inherit from `Entity<int>` (which remains in `Anela.Heblo.Xcc.Domain` because it is a technical base type), so the moved file must add `using Anela.Heblo.Xcc.Domain;`.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs` with namespace `Anela.Heblo.Domain.Features.Dashboard`.
- Original file `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs` is deleted.
- Public surface (properties, navigation collection, types, default values) is unchanged.
- Class still inherits from `Anela.Heblo.Xcc.Domain.Entity<int>`.

### FR-2: Move `UserDashboardTile` entity to Domain layer
Move `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs` to `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs`. Change the namespace from `Anela.Heblo.Xcc.Domain` to `Anela.Heblo.Domain.Features.Dashboard`. Add `using Anela.Heblo.Xcc.Domain;` for the `Entity<int>` base class.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs` with namespace `Anela.Heblo.Domain.Features.Dashboard`.
- Original file `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs` is deleted.
- Public surface (properties, navigation reference, defaults) is unchanged.
- Class still inherits from `Anela.Heblo.Xcc.Domain.Entity<int>`.

### FR-3: Move `IUserDashboardSettingsRepository` interface to Domain layer
Move `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs` to `backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs`. Change the namespace from `Anela.Heblo.Xcc.Services.Dashboard` to `Anela.Heblo.Domain.Features.Dashboard`. The `using Anela.Heblo.Xcc.Domain;` import is removed because the entity type is now in the same namespace.

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs` with namespace `Anela.Heblo.Domain.Features.Dashboard`.
- Original file at `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs` is deleted.
- Method signatures (`GetByUserIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`) are unchanged.

### FR-4: Update `using` directives in consuming code
Every file that imports `Anela.Heblo.Xcc.Domain` or `Anela.Heblo.Xcc.Services.Dashboard` to reach the relocated types must be updated to import `Anela.Heblo.Domain.Features.Dashboard` instead. Files that import `Anela.Heblo.Xcc.Services.Dashboard` only for other types (`ITile`, `ITileRegistry`, `TileSize`, `TileCategory`, `TileData`, `TileMetadata`, `DashboardOptions`, etc.) must retain that import — those types stay in Xcc.

Files known to require updates (non-exhaustive — implementer must locate all callers):

- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetAvailableTiles/GetAvailableTilesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs`
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardSettingsConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardTileConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardSettingsRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/GetUserSettingsHandlerTests.cs`
- Any other test or source file referencing `UserDashboardSettings`, `UserDashboardTile`, or `IUserDashboardSettingsRepository`.

**Acceptance criteria:**
- Solution-wide search shows no remaining references to `Anela.Heblo.Xcc.Domain.UserDashboardSettings`, `Anela.Heblo.Xcc.Domain.UserDashboardTile`, or `Anela.Heblo.Xcc.Services.Dashboard.IUserDashboardSettingsRepository`.
- All consumers compile using the new `Anela.Heblo.Domain.Features.Dashboard` namespace.
- Imports for unrelated Xcc types (tile registry, tile metadata, etc.) remain intact in the same files.

### FR-5: Preserve project references
The `Anela.Heblo.Domain` project must remain a clean Domain project — no new dependency on `Anela.Heblo.Application` or `Anela.Heblo.Persistence`. The existing `Anela.Heblo.Domain → Anela.Heblo.Xcc` reference (already used for shared technical types) is sufficient to satisfy `Entity<int>`. The `Anela.Heblo.Xcc → Anela.Heblo.Domain` direction must not be introduced.

**Acceptance criteria:**
- `Anela.Heblo.Domain.csproj` has no new `<ProjectReference>` entries beyond what already exists.
- `Anela.Heblo.Xcc.csproj` does not reference `Anela.Heblo.Domain`.
- Solution builds with no circular reference errors.

### FR-6: Persistence and EF Core mapping continue to work
Entity Framework Core configurations in `backend/src/Anela.Heblo.Persistence/Dashboard/` must continue to map the relocated entities to the same database tables/columns with the same constraints. No EF migration is required because table schema and CLR property layout are unchanged.

**Acceptance criteria:**
- `UserDashboardSettingsConfiguration` and `UserDashboardTileConfiguration` reference the relocated types and compile.
- Running the existing handler/repository tests against the in-memory or real EF provider produces unchanged behavior.
- No new EF Core migration is generated for this change.

### FR-7: Behavior parity verified by existing tests
All existing Dashboard handler tests (`SaveUserSettingsHandlerTests`, `GetUserSettingsHandlerTests`, `EnableTileHandlerTests`, `DisableTileHandlerTests`, `GetAvailableTilesHandlerTests`, `GetTileDataHandlerTests`, repository tests) must pass without behavioral modification. Test files may have their `using` statements updated, but assertions and test data setup must be unchanged in intent.

**Acceptance criteria:**
- `dotnet build` succeeds with zero errors and no new warnings introduced by this change.
- `dotnet format` reports clean.
- `dotnet test` for the Dashboard test classes passes 100%.
- No test was deleted, skipped, or weakened.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. Move is compile-time only.

### NFR-2: Security
No security surface change. Authorization, user-scoping (`UserId` filtering in the repository), and data exposure remain identical.

### NFR-3: Backward Compatibility
- No public HTTP API change.
- No database schema change.
- No DTO/contract change.
- No frontend change.
- Type-fork callers in the same solution are updated atomically in the same commit/PR.

### NFR-4: Maintainability
The relocation aligns Dashboard with the rest of the codebase, lowering cognitive load for new contributors and removing a known violation flagged by the daily arch-review routine. After this change, locating Dashboard domain types follows the same mental model as every other module.

## Data Model

No data-model change. For reference, the entities being relocated are:

- `UserDashboardSettings : Entity<int>` — per-user dashboard configuration root.
  - `UserId : string`
  - `LastModified : DateTime`
  - `Tiles : ICollection<UserDashboardTile>` (navigation)
- `UserDashboardTile : Entity<int>` — per-tile visibility/order for a user.
  - `UserId : string`
  - `TileId : string`
  - `IsVisible : bool`
  - `DisplayOrder : int`
  - `LastModified : DateTime`
  - `DashboardSettings : UserDashboardSettings` (navigation back-reference)
- `IUserDashboardSettingsRepository` — async CRUD: `GetByUserIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`.

Relationship: one `UserDashboardSettings` has many `UserDashboardTile`. Persisted via EF Core configurations in `Anela.Heblo.Persistence/Dashboard/`.

## API / Interface Design

No external interface changes.

Internal C# namespaces change as follows:

| Type | From | To |
|------|------|----|
| `UserDashboardSettings` | `Anela.Heblo.Xcc.Domain` | `Anela.Heblo.Domain.Features.Dashboard` |
| `UserDashboardTile` | `Anela.Heblo.Xcc.Domain` | `Anela.Heblo.Domain.Features.Dashboard` |
| `IUserDashboardSettingsRepository` | `Anela.Heblo.Xcc.Services.Dashboard` | `Anela.Heblo.Domain.Features.Dashboard` |

Types remaining in Xcc (unchanged): `Entity<T>`, `IEntity<T>`, `ITile`, `ITileRegistry`, `TileRegistry`, `TileMetadata`, `TileData`, `TileSize`, `TileCategory`, `TileExtensions`, `TileRegistryExtensions`, `DashboardOptions`, `XccModule`, and built-in tile implementations under `Xcc/Services/Dashboard/Tiles/`.

## Dependencies

- `Anela.Heblo.Domain` project (target location). Folder `Features/Dashboard/` must be created (does not currently exist).
- `Anela.Heblo.Xcc` project (source location and continuing host of `Entity<T>` plus tile registry infrastructure).
- `Anela.Heblo.Domain` already references `Anela.Heblo.Xcc` for technical base types; no new project reference required.
- Consumers in `Anela.Heblo.Application`, `Anela.Heblo.Persistence`, and the test assemblies — all updated in the same change set.

## Out of Scope

- Moving non-feature-specific Dashboard infrastructure out of Xcc (`ITile`, `ITileRegistry`, `TileRegistry`, `TileMetadata`, `TileSize`, `TileCategory`, `DashboardOptions`). These are technical cross-cutting infrastructure for the pluggable tile system and remain in Xcc.
- Refactoring or renaming the entities, the repository interface, or its methods.
- Changes to handlers' business logic, locking (`IUserDashboardSettingsLock`), or transaction semantics.
- EF Core schema changes or any database migration.
- Frontend changes; the OpenAPI client is unaffected because DTOs (which live in `Application/Features/Dashboard/Contracts/`) do not move.
- Resolving issue #1943 (`DashboardService` decoupling). The brief references it as motivation; the actual codebase has no class named `DashboardService` — handlers consume `IUserDashboardSettingsRepository` directly. This relocation makes that future decoupling cleaner but does not perform it.
- Adding new tests. Existing tests already cover the entities and repository contract.

## Open Questions

None.

## Status: COMPLETE