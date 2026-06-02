I have enough context to ground the review. The spec's plan is correct, but I identified one missed file: `ApplicationDbContextModelSnapshot.cs` contains the entity CLR type name strings (`"Anela.Heblo.Xcc.Domain.UserDashboardSettings"` etc.) and must be updated, otherwise EF will report a phantom pending model change on the next migration. I'll flag this in Specification Amendments.

```markdown
# Architecture Review: Relocate Dashboard Domain Entities from Xcc to Domain Layer

## Skip Design: true

This is a backend-only namespace/file relocation with no API, schema, DTO, or UI surface change.

## Architectural Fit Assessment

The change strengthens conformance to the project's documented Clean Architecture + Vertical Slice layering. Every other module in the codebase (`BackgroundJobs`, `Users`, `Catalog`, `Logistics`, `Manufacture`, ...) places its feature-specific entities and repository interfaces in `Anela.Heblo.Domain/Features/{Feature}/`. `Dashboard` is the lone outlier with entities and a feature-specific repository contract sitting in `Anela.Heblo.Xcc` — which `docs/architecture/development_guidelines.md` and `CLAUDE.md` explicitly reserve for *technical* cross-cutting concerns.

Integration points (all internal, all C#):

- **Domain → Xcc dependency direction.** Already correct: `Anela.Heblo.Domain.csproj` references `Anela.Heblo.Xcc.csproj`. The relocated `UserDashboardSettings`/`UserDashboardTile` can keep inheriting `Anela.Heblo.Xcc.Domain.Entity<int>` with zero new project reference.
- **Application/Persistence consumers.** Already reference `Anela.Heblo.Domain`. They only need their `using` lines flipped from `Anela.Heblo.Xcc.Domain` / `Anela.Heblo.Xcc.Services.Dashboard` to `Anela.Heblo.Domain.Features.Dashboard`. Other Xcc imports in the same files (`ITile`, `ITileRegistry`, `TileMetadata`, `TileSize`, `TileCategory`, `TileData`, `DashboardOptions`, `RegisterTile<>()` extension) **must remain** — those types stay in Xcc by design.
- **EF model snapshot.** `Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` hardcodes the CLR type name strings (verified: 5 occurrences referencing `Anela.Heblo.Xcc.Domain.UserDashboardSettings`/`UserDashboardTile`). This file is the live model fingerprint used by `dotnet ef migrations add`; if not updated the next migration command will emit a spurious rename diff.

The Xcc → Domain back-reference is correctly forbidden by the spec (FR-5) and not needed: nothing in Xcc depends on the moved types — `Xcc/Services/Dashboard/Tiles/BackgroundTaskStatusTile.cs` and `XccModule.cs` only touch the tile registry and `DashboardOptions`, which stay put.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.API                                                          │
│   DashboardController ──► MediatR                                        │
└──────────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application/Features/Dashboard                               │
│   UseCases/*Handler.cs        (consume IUserDashboardSettingsRepository) │
│   Infrastructure/IUserDashboardSettingsLock                              │
│   Contracts/UserDashboardSettingsDto, UserDashboardTileDto               │
│   DashboardModule.cs                                                     │
└──────────────────────────────────────────────────────────────────────────┘
                  │ uses                                  │ uses
                  ▼                                       ▼
┌────────────────────────────────────┐    ┌─────────────────────────────────┐
│ Anela.Heblo.Domain/Features/       │    │ Anela.Heblo.Xcc                 │
│   Dashboard/   ◄── NEW FOLDER      │    │   Domain/Entity<T>, IEntity<T>  │
│     UserDashboardSettings.cs       │───►│   Services/Dashboard/           │
│     UserDashboardTile.cs           │    │     ITile, ITileRegistry,       │
│     IUserDashboardSettingsRepo.cs  │    │     TileRegistry, TileMetadata, │
└────────────────────────────────────┘    │     TileSize, TileCategory,     │
                  ▲                       │     TileData, DashboardOptions, │
                  │ implements            │     XccModule, Tiles/           │
                  │                       └─────────────────────────────────┘
┌────────────────────────────────────┐
│ Anela.Heblo.Persistence/Dashboard  │
│   UserDashboardSettingsConfig      │
│   UserDashboardTileConfig          │
│   UserDashboardSettingsRepository  │
└────────────────────────────────────┘
```

The arrow from `Domain/Features/Dashboard` to `Xcc` (for `Entity<int>`) is the *only* permitted cross-project link and already exists.

### Key Design Decisions

#### Decision 1: Keep `Entity<int>` in Xcc; move only Dashboard-specific types
**Options considered:**
- (A) Move `Entity<T>` / `IEntity<T>` to `Anela.Heblo.Domain/Shared/` together with the Dashboard entities.
- (B) Leave `Entity<T>` / `IEntity<T>` in `Anela.Heblo.Xcc.Domain` and only move feature-specific types.

**Chosen approach:** (B).

**Rationale:** `Entity<T>` is a technical base type used by entities across all modules; it is genuinely cross-cutting and matches the documented purpose of Xcc ("technical concerns only"). Relocating it expands the blast radius of this change to every entity in the solution for no architectural gain. Domain → Xcc is already an accepted dependency (ADR-002: generic repository in Xcc), and the same direction satisfies the inheritance requirement here. The brief and spec scope this work narrowly to Dashboard; widening it would violate the "surgical changes" rule in `CLAUDE.md`.

#### Decision 2: Keep tile registry infrastructure (`ITile`, `ITileRegistry`, `TileRegistry`, `TileMetadata`, `TileSize`, `TileCategory`, `TileData`, `DashboardOptions`) in Xcc
**Options considered:**
- (A) Move the tile registry infrastructure to `Application/Features/Dashboard/Infrastructure/`.
- (B) Keep it in `Xcc/Services/Dashboard/`.

**Chosen approach:** (B). This is consistent with the spec ("Out of Scope") and aligns with how the tile registry is actually used: 13+ modules across `Application/Features/*/DashboardTiles/` register tiles into it via `RegisterTile<>()` (Catalog, Manufacture, Purchase, Logistics, BackgroundJobs, DataQuality, Analytics, WeatherForecast, ...). Tile registration is a plugin extensibility mechanism — a technical concern. Only the *user's stored dashboard state* (which tiles they enabled, in what order) is feature-specific domain data, and that is exactly what's being moved.

**Rationale:** Distinguishes "platform/extensibility mechanism" (Xcc) from "feature-owned domain state" (Domain).

#### Decision 3: Update `ApplicationDbContextModelSnapshot.cs` in the same commit
**Options considered:**
- (A) Manually edit the snapshot strings to the new fully-qualified type names.
- (B) Generate a no-op EF migration (`dotnet ef migrations add RelocateDashboardEntityNamespace`) and let the tooling regenerate the snapshot.
- (C) Leave the snapshot stale.

**Chosen approach:** (A).

**Rationale:** The spec asserts (FR-6) that no migration is required because table/column schema is unchanged. (C) is wrong because EF would emit a phantom migration on the next `dotnet ef migrations add` due to entity CLR type name change. (B) creates a useless empty migration that pollutes history and contradicts FR-6. (A) is the smallest, most honest change: the type rename is purely metadata; the snapshot is the authoritative metadata record and must be kept in sync. The strings to update are the 5 occurrences in `ApplicationDbContextModelSnapshot.cs` only; historical `*.Designer.cs` files must be left alone (they encode historical model state).

## Implementation Guidance

### Directory / Module Structure

**Create:**
```
backend/src/Anela.Heblo.Domain/Features/Dashboard/
├── UserDashboardSettings.cs              (moved from Xcc/Domain/)
├── UserDashboardTile.cs                  (moved from Xcc/Domain/)
└── IUserDashboardSettingsRepository.cs   (moved from Xcc/Services/Dashboard/)
```

**Delete:**
```
backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs
backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs
backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs
```

**Untouched (stay in Xcc):**
```
backend/src/Anela.Heblo.Xcc/Domain/Entity.cs
backend/src/Anela.Heblo.Xcc/Domain/IEntity.cs
backend/src/Anela.Heblo.Xcc/Services/Dashboard/{ITile,ITileRegistry,TileRegistry,
   TileRegistryExtensions,TileMetadata,TileExtensions,TileData,TileSize,
   TileCategory,DashboardOptions,Tiles/}.cs
backend/src/Anela.Heblo.Xcc/XccModule.cs
```

### Interfaces and Contracts

After move, the three types resolve under namespace `Anela.Heblo.Domain.Features.Dashboard`:

```csharp
// File: backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs
using Anela.Heblo.Xcc.Domain;          // for Entity<int>
namespace Anela.Heblo.Domain.Features.Dashboard;
public class UserDashboardSettings : Entity<int> { /* unchanged surface */ }

// File: backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs
using Anela.Heblo.Xcc.Domain;          // for Entity<int>
namespace Anela.Heblo.Domain.Features.Dashboard;
public class UserDashboardTile : Entity<int> { /* unchanged surface */ }

// File: backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs
namespace Anela.Heblo.Domain.Features.Dashboard;
public interface IUserDashboardSettingsRepository
{
    Task<UserDashboardSettings?> GetByUserIdAsync(string userId);
    Task<UserDashboardSettings> AddAsync(UserDashboardSettings settings);
    Task UpdateAsync(UserDashboardSettings settings);
    Task DeleteAsync(string userId);
}
```

No method signatures, properties, or default values change.

### Data Flow

Identical to current behavior. For `SaveUserSettings`:

1. `DashboardController` receives HTTP request → MediatR `SaveUserSettingsRequest`.
2. `SaveUserSettingsHandler` (in `Application/Features/Dashboard/UseCases/SaveUserSettings/`) acquires `IUserDashboardSettingsLock`, calls `IUserDashboardSettingsRepository.GetByUserIdAsync(userId)` / `UpdateAsync` / `AddAsync`.
3. `UserDashboardSettingsRepository` (in `Persistence/Dashboard/`) writes to `ApplicationDbContext.UserDashboardSettings` `DbSet<UserDashboardSettings>`.
4. EF Core applies `UserDashboardSettingsConfiguration` + `UserDashboardTileConfiguration` mappings to tables `public.UserDashboardSettings` and `public.UserDashboardTiles`.

The only change: in steps 2–4 the CLR types resolve from `Anela.Heblo.Domain.Features.Dashboard` instead of `Anela.Heblo.Xcc.Domain`.

### Call-site updates (verified non-exhaustive list)

`using` line updates only — no logic change. Confirmed via repo-wide grep for `Anela.Heblo.Xcc.Domain` and `Anela.Heblo.Xcc.Services.Dashboard`:

**Source files that must flip the using (Application + Persistence):**
- `Application/Features/Dashboard/DashboardModule.cs` — currently imports `Anela.Heblo.Xcc.Services.Dashboard` (drop only if no other Xcc.Services.Dashboard type is referenced; keep the import otherwise — verify).
- `Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsHandler.cs`
- `Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsHandler.cs`
- `Application/Features/Dashboard/UseCases/EnableTile/EnableTileHandler.cs`
- `Application/Features/Dashboard/UseCases/DisableTile/DisableTileHandler.cs`
- `Application/Features/Dashboard/UseCases/GetAvailableTiles/GetAvailableTilesHandler.cs` (referenced repository for tile order/visibility)
- `Application/Features/Dashboard/UseCases/GetTileData/GetTileDataHandler.cs`
- `Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsRequest.cs`
- `Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsResponse.cs`
- `Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsLock.cs`
- `Application/Features/Dashboard/Infrastructure/UserDashboardSettingsLock.cs`
- `Application/Features/Dashboard/Contracts/UserDashboardSettingsDto.cs`
- `Application/Features/Dashboard/Contracts/UserDashboardTileDto.cs`
- `Persistence/Dashboard/UserDashboardSettingsConfiguration.cs`
- `Persistence/Dashboard/UserDashboardTileConfiguration.cs`
- `Persistence/Dashboard/UserDashboardSettingsRepository.cs` (drop the `Anela.Heblo.Xcc.Domain` AND `Anela.Heblo.Xcc.Services.Dashboard` imports; add `Anela.Heblo.Domain.Features.Dashboard`)
- `Persistence/PersistenceModule.cs` (verify DI registration of `IUserDashboardSettingsRepository`)
- `Persistence/ApplicationDbContext.cs` (the `DbSet<UserDashboardSettings>` property + any `using` line)

**API layer:**
- `API/Controllers/DashboardController.cs` — verify; does not need to reference the entity directly, but check the `using` block.

**Tests:**
- `test/Anela.Heblo.Tests/Features/Dashboard/SaveUserSettingsHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/GetUserSettingsHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/EnableTileHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/DisableTileHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/GetTileDataHandlerTests.cs`
- `test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsLockTests.cs`
- `test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs`

**EF Migration snapshot (see Decision 3):**
- `Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — replace 5 occurrences of `"Anela.Heblo.Xcc.Domain.UserDashboardSettings"` / `"Anela.Heblo.Xcc.Domain.UserDashboardTile"` with `"Anela.Heblo.Domain.Features.Dashboard.UserDashboardSettings"` / `"Anela.Heblo.Domain.Features.Dashboard.UserDashboardTile"`.

**Do NOT modify:**
- `Persistence/Migrations/20251009061353_AddUserDashboardSettings.Designer.cs` and other historical `.Designer.cs` files. These freeze model state at the time of each migration; rewriting history is wrong.
- `Persistence/Migrations/20251009061353_AddUserDashboardSettings.cs` and `20251009071426_RefactorDashboardTileStorage.cs` migration bodies — they operate on table names (strings), not CLR types.
- Tile registration call sites in `Catalog`/`Manufacture`/`Purchase`/`Logistics`/etc. — they import `Anela.Heblo.Xcc.Services.Dashboard` for `ITile`, `RegisterTile<>()`, `TileMetadata`, etc., and never reference `UserDashboardSettings` or `IUserDashboardSettingsRepository`.

### Validation steps

1. `grep -rn "Anela\.Heblo\.Xcc\.Domain\.UserDashboard\|Anela\.Heblo\.Xcc\.Services\.Dashboard\.IUserDashboardSettingsRepository" backend/` → must return zero matches (excluding deleted files and historical `.Designer.cs` snapshots).
2. `dotnet build` from `backend/` → zero errors, no new warnings.
3. `dotnet format` → clean.
4. `dotnet test --filter "FullyQualifiedName~Dashboard"` → 100% pass on existing tests.
5. `dotnet ef migrations add _ProbeRelocation --no-build --dry-run` *(optional sanity check — should report "no changes"; if it would generate a non-empty migration, the snapshot update in step Decision 3 was missed).* Discard the probe.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ApplicationDbContextModelSnapshot.cs` not updated → next developer's `dotnet ef migrations add` emits a phantom rename migration | High | Update the 5 string occurrences in the snapshot as part of the same PR (Decision 3). Add a verification step to the PR description. |
| Hidden `using Anela.Heblo.Xcc.Domain;` left behind in a file that no longer references any Xcc.Domain type → harmless warning, but `dotnet format` may flip it | Low | Run `dotnet format` after edits; CI catches it. Acceptable cleanup as part of the move. |
| File over-cleans `using Anela.Heblo.Xcc.Services.Dashboard;` while still needing it for `ITile`/`TileMetadata`/`DashboardOptions`/etc. → build break | Medium | When touching each file, search it for `ITile`, `TileMetadata`, `TileSize`, `TileCategory`, `TileData`, `ITileRegistry`, `DashboardOptions`, `RegisterTile`, `TileExtensions` — if any present, keep the Xcc.Services.Dashboard import. |
| Test seed data uses `new Anela.Heblo.Xcc.Domain.UserDashboardSettings(...)` fully-qualified anywhere | Low | The grep in step 1 of validation catches this. |
| Other adapter/module project (`backend/src/Adapters/*`) references the moved types | Low | Grep shows no Adapter project touches `IUserDashboardSettingsRepository` or `UserDashboard*`. Confirmed clean. |
| `InternalsVisibleTo` mismatch — the moved interface relies on internal visibility | None | All three moved types are `public`; no internals involved. |
| Future per-module DbContext split (ADR-001 Phase 2) needs these in Domain | Resolved positively | This change is a prerequisite for that future split and removes a known blocker. |

## Specification Amendments

1. **Add an explicit acceptance criterion under FR-6 for `ApplicationDbContextModelSnapshot.cs`.** The spec's current FR-6 wording ("No new EF Core migration is generated for this change") is satisfiable in two ways: stale snapshot or updated snapshot. Only the updated-snapshot option keeps the codebase healthy. Recommended additional bullet:
   > - `Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` is updated in this PR so that all CLR type-name strings referring to `UserDashboardSettings`/`UserDashboardTile` use the new namespace `Anela.Heblo.Domain.Features.Dashboard`. Historical `*.Designer.cs` migration files are left unchanged. After the change, `dotnet ef migrations add Probe --dry-run` produces no diff.

2. **Extend FR-4's "known call sites" list** with the following files that the current spec does not mention but that the codebase grep surfaces. (Spec already disclaims the list is non-exhaustive, so this is a hint, not a requirement change.):
   - `Application/Features/Dashboard/UseCases/SaveUserSettings/SaveUserSettingsRequest.cs`
   - `Application/Features/Dashboard/UseCases/GetUserSettings/GetUserSettingsResponse.cs`
   - `Application/Features/Dashboard/Infrastructure/IUserDashboardSettingsLock.cs`
   - `Application/Features/Dashboard/Infrastructure/UserDashboardSettingsLock.cs`
   - `Application/Features/Dashboard/Contracts/UserDashboardSettingsDto.cs`
   - `Application/Features/Dashboard/Contracts/UserDashboardTileDto.cs`
   - `Persistence/PersistenceModule.cs`
   - `Persistence/ApplicationDbContext.cs`
   - `test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsLockTests.cs`
   - `test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs`

3. **Clarify FR-5 about Domain → Xcc direction.** Suggested wording:
   > The existing `Anela.Heblo.Domain → Anela.Heblo.Xcc` project reference satisfies the `Entity<int>` inheritance requirement. This direction is consistent with ADR-002 (generic repository in Xcc) and remains the only permitted edge between these two projects. The reverse direction (`Xcc → Domain`) must not be introduced and is verified by the absence of `<ProjectReference Include="..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />` in `Anela.Heblo.Xcc.csproj`.

No other amendments. The spec is otherwise complete, correctly scoped, and accurately reflects the codebase state.

## Prerequisites

None. All of the following already exist:

- `Anela.Heblo.Domain.csproj` already references `Anela.Heblo.Xcc.csproj`.
- `Anela.Heblo.Xcc.csproj` already exposes `Entity<T>` and `IEntity<T>` as `public`.
- `Anela.Heblo.Domain/Features/` already exists with 50+ peer feature folders demonstrating the pattern (`BackgroundJobs/`, `Users/`, `Purchase/`, etc.) — the new `Dashboard/` subfolder simply follows.
- All consumer projects (`Application`, `Persistence`, `Tests`) already reference `Anela.Heblo.Domain`.

The change is self-contained, atomic, and can be implemented in a single PR with the validation steps above.
```