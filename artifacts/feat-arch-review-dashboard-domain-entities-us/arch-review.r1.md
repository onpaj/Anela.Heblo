I have what I need. The spec contains a hard architectural blocker I must call out. Writing the review now.

# Architecture Review: Relocate Dashboard Domain Entities from Xcc to Domain Layer

## Skip Design: true

Pure backend refactor. No new components, screens, or visual changes.

## Architectural Fit Assessment

The **goal** of the refactor aligns perfectly with the project's Clean Architecture conventions: every other feature module places its entities under `Anela.Heblo.Domain/Features/{Module}/`, and the Dashboard module is the lone exception. The brief and spec correctly identify the layering violation.

However, the **scope as written is not buildable**. Verified facts:

- `Anela.Heblo.Xcc.csproj` declares **no** `ProjectReference` — Xcc sits at the bottom of the dependency graph and references only `Microsoft.Extensions.*` NuGet packages.
- `Anela.Heblo.Domain.csproj` already references `Anela.Heblo.Xcc` (for `Entity<T>`, `IEntity<T>` in `Xcc/Domain/`).
- `IDashboardService` (Xcc, `IDashboardService.cs:7,8`) returns `UserDashboardSettings` and `DashboardService` (Xcc, `DashboardService.cs:10,29,45,52,76,104`) consumes `UserDashboardSettings`, `UserDashboardTile`, and `IUserDashboardSettingsRepository`.

Moving the three types into `Anela.Heblo.Domain` while leaving `DashboardService` / `IDashboardService` in `Anela.Heblo.Xcc` forces Xcc to reference Domain. Combined with the existing Domain → Xcc reference, this is a **circular project reference** and the solution will fail to compile. **The spec's claim that issue #1943 (DashboardService placement) is out of scope cannot stand — at least the two service files must move with the entities, or the refactor cannot succeed.**

A second non-obvious integration point: `Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` embeds the entity FQNs as string literals (`Anela.Heblo.Xcc.Domain.UserDashboardSettings` at lines 3506, 3530, 3916, 3918). EF Core compares this string against the runtime model's FQN; if they drift, the next `dotnet ef migrations add` produces a spurious migration even though no schema changed. The snapshot must be hand-edited in lockstep with the namespace change. Historical `*.Designer.cs` files are also frozen snapshots referencing the old FQN — they must stay frozen (per convention), but their string FQNs are decoupled from runtime type resolution so they can remain unchanged.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Xcc  (technical cross-cutting only; no Anela.Heblo.* refs)
├── Domain/                              [stays] Entity<T>, IEntity<T>
└── Services/Dashboard/                  [keep]  ITile, ITileRegistry, TileRegistry,
                                                 TileExtensions, TileRegistryExtensions,
                                                 TileData, TileSize, TileCategory,
                                                 DashboardOptions, Tiles/BackgroundTaskStatusTile
        (REMOVED — IDashboardService, DashboardService, IUserDashboardSettingsRepository)

Anela.Heblo.Domain  (references Xcc)
└── Features/Dashboard/                  [NEW]
        ├── UserDashboardSettings.cs        (was Xcc/Domain/)
        ├── UserDashboardTile.cs            (was Xcc/Domain/)
        ├── IUserDashboardSettingsRepository.cs   (was Xcc/Services/Dashboard/)
        └── IDashboardService.cs            (was Xcc/Services/Dashboard/  — spec amendment)

Anela.Heblo.Application  (references Domain, Xcc)
└── Features/Dashboard/
        ├── Services/DashboardService.cs    (was Xcc/Services/Dashboard/ — spec amendment)
        └── UseCases/...                    [unchanged behaviour, only usings update]

Anela.Heblo.Persistence  (references Domain, Xcc)
└── Dashboard/
        ├── UserDashboardSettingsRepository.cs       [usings update]
        ├── UserDashboardSettingsConfiguration.cs    [usings update]
        └── UserDashboardTileConfiguration.cs        [usings update]
└── Migrations/
        └── ApplicationDbContextModelSnapshot.cs     [edit FQN string literals in lockstep]
```

### Key Design Decisions

#### Decision 1: Expand scope to also relocate `IDashboardService` and `DashboardService`

**Options considered:**
1. Move only entities + repository interface as spec states (the brief's wording).
2. Also move `IDashboardService` and `DashboardService` out of Xcc in the same change.
3. Add `Xcc → Domain` project reference, accepting circularity attempt.

**Chosen approach:** Option 2.

**Rationale:** Option 1 produces a circular project reference and will not build (verified — `DashboardService.cs:29` returns `UserDashboardSettings`; `IDashboardService.cs:7` does the same; Xcc has no Anela.Heblo.* project references today, and Domain already references Xcc). Option 3 is forbidden by Clean Architecture and would still fail because `Domain → Xcc` already exists. Option 2 is the minimum-viable cycle break: it moves only the four files actually entangled with the Dashboard aggregate, leaving tile-registry plumbing (`ITile`, `ITileRegistry`, `TileData`, etc.) in Xcc because it has no dependency on the relocated types and is genuine technical scaffolding. This expansion subsumes the minimum subset of issue #1943 required to unblock this work; the spec's "issue #1943 is out of scope" rule must be relaxed accordingly.

#### Decision 2: Place `IDashboardService` in Domain, `DashboardService` in Application

**Options considered:**
1. Both in `Anela.Heblo.Application/Features/Dashboard/Services/`.
2. `IDashboardService` in `Anela.Heblo.Domain/Features/Dashboard/`, implementation in Application.

**Chosen approach:** Option 2.

**Rationale:** Matches the codebase's existing pattern where repository abstractions live in Domain (e.g., the other entries already in `Domain/Features/*`) and implementations live in their respective infrastructure project. `IDashboardService` is consumed by Application handlers (`SaveUserSettingsHandler`, `EnableTileHandler`) — placing its contract in Domain keeps Application → Domain dependency direction natural. `DashboardService` orchestrates Application use cases (locking, tile parallelism, defaulting) and belongs in Application.

#### Decision 3: Hand-edit `ApplicationDbContextModelSnapshot.cs` rather than generate a no-op migration

**Options considered:**
1. Edit the snapshot's four string-literal FQNs to match the new namespace.
2. Run `dotnet ef migrations add UpdateDashboardEntityNamespaces` and accept the no-op migration.

**Chosen approach:** Option 1.

**Rationale:** The spec's FR-6 forbids new migrations and the schema genuinely is unchanged. Option 2 produces an empty `Up`/`Down` whose only effect is updating the snapshot — pollution. The snapshot file is auto-generated but checked in; editing four strings is a legitimate fix and EF Core's runtime diff will then see no model change.

## Implementation Guidance

### Directory / Module Structure

**Files to move (new locations):**
```
backend/src/Anela.Heblo.Domain/Features/Dashboard/
    UserDashboardSettings.cs                       (from Xcc/Domain/)
    UserDashboardTile.cs                           (from Xcc/Domain/)
    IUserDashboardSettingsRepository.cs            (from Xcc/Services/Dashboard/)
    IDashboardService.cs                           (from Xcc/Services/Dashboard/) — spec amendment

backend/src/Anela.Heblo.Application/Features/Dashboard/Services/
    DashboardService.cs                            (from Xcc/Services/Dashboard/) — spec amendment
```

**Files to edit (using-statement updates only):**
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardSettingsRepository.cs` — replace `using Anela.Heblo.Xcc.Domain;` and `using Anela.Heblo.Xcc.Services.Dashboard;` with `using Anela.Heblo.Domain.Features.Dashboard;`.
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardSettingsConfiguration.cs` — same.
- `backend/src/Anela.Heblo.Persistence/Dashboard/UserDashboardTileConfiguration.cs` — same.
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — line 29 (`using Anela.Heblo.Xcc.Domain;`) currently serves both `Entity<T>` and the dashboard DbSets; verify whether `Entity<T>` is still needed and either keep both usings or replace with `using Anela.Heblo.Domain.Features.Dashboard;`.
- All Application handlers: `SaveUserSettingsHandler.cs`, `EnableTileHandler.cs`, `GetUserSettingsHandler.cs`, `DisableTileHandler.cs`, `GetTileDataHandler.cs`, `GetAvailableTilesHandler.cs`, plus `DashboardModule.cs` and any mapper / DTO file in `Application/Features/Dashboard/`.
- `backend/src/Anela.Heblo.API/Program.cs` and `Extensions/ServiceCollectionExtensions.cs` — DI registration namespace updates.
- All `backend/test/Anela.Heblo.Tests/Features/Dashboard/*Tests.cs` — `using` updates only.
- `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` — same.
- Any tile implementation files under `Application/Features/*/DashboardTiles/` that touch the moved types (`ITile` stays in Xcc, but verify each grep hit).
- `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` — replace the four `Anela.Heblo.Xcc.Domain.UserDashboard*` FQN string literals with `Anela.Heblo.Domain.Features.Dashboard.UserDashboard*`.

**Files NOT to touch:**
- `Persistence/Migrations/20251009061353_AddUserDashboardSettings.Designer.cs`, `20251009071426_RefactorDashboardTileStorage.Designer.cs`, `20251024072354_UpdateMaterialInventoryTileId.Designer.cs` — frozen historical snapshots. They contain string FQNs that EF does not use for runtime type resolution; leaving them alone preserves migration history convention.
- `Persistence/Migrations/20251009061353_AddUserDashboardSettings.cs`, `20251009071426_RefactorDashboardTileStorage.cs` — the `Up`/`Down` SQL has no namespace references (verified: only table names).
- `Xcc/Services/Dashboard/` other files: `ITile`, `ITileRegistry`, `TileRegistry`, `TileExtensions`, `TileRegistryExtensions`, `TileCategory`, `TileSize`, `TileData`, `DashboardOptions`, `Tiles/BackgroundTaskStatusTile` — none depend on `UserDashboardSettings`/`UserDashboardTile` (verify with grep before declaring done).

### Interfaces and Contracts

All relocated types keep their existing signatures verbatim. Namespace mapping:

| Before | After |
|---|---|
| `Anela.Heblo.Xcc.Domain.UserDashboardSettings` | `Anela.Heblo.Domain.Features.Dashboard.UserDashboardSettings` |
| `Anela.Heblo.Xcc.Domain.UserDashboardTile` | `Anela.Heblo.Domain.Features.Dashboard.UserDashboardTile` |
| `Anela.Heblo.Xcc.Services.Dashboard.IUserDashboardSettingsRepository` | `Anela.Heblo.Domain.Features.Dashboard.IUserDashboardSettingsRepository` |
| `Anela.Heblo.Xcc.Services.Dashboard.IDashboardService` | `Anela.Heblo.Domain.Features.Dashboard.IDashboardService` |
| `Anela.Heblo.Xcc.Services.Dashboard.DashboardService` | `Anela.Heblo.Application.Features.Dashboard.Services.DashboardService` |

The two relocated entities keep inheriting `Entity<int>` from `Anela.Heblo.Xcc.Domain.Entity<>` — they need `using Anela.Heblo.Xcc.Domain;` on the new files. Domain → Xcc is allowed (already present).

### Data Flow

Unchanged. The Dashboard request path is still:
```
DashboardController → MediatR → {Get|Save|Enable|Disable|GetTileData}Handler
                                       │
                                       ▼
                                IDashboardService (now Domain)
                                       │
                                       ▼
                          DashboardService (now Application)
                                       │
                                       ▼
                IUserDashboardSettingsRepository (now Domain)
                                       │
                                       ▼
                UserDashboardSettingsRepository (Persistence/Dashboard/)
                                       │
                                       ▼
                          ApplicationDbContext  → Postgres
```

Only the namespace declarations change; runtime behaviour, EF mapping, transactions, locking, and tile loading parallelism are untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Circular project reference if only entities/repo move (spec as-written produces this) | **Blocker** | Apply Spec Amendment 1 — also move `IDashboardService` to Domain and `DashboardService` to Application. |
| Spurious EF migration generated next time someone runs `dotnet ef migrations add` because `ApplicationDbContextModelSnapshot.cs` still contains old FQNs | High | Hand-edit the four FQN strings in the snapshot file as part of this PR; verify by running `dotnet ef migrations add Probe --no-build` in a scratch checkout and confirming an empty migration (then discard). |
| Historical `*.Designer.cs` snapshots reference old FQN — compile fails? | Low | Verified: Designer.cs files store entity names as **string literals**, not C# type references. They compile regardless of whether the type exists at that namespace at runtime. Leave them frozen. |
| Hidden callers missed by the brief's enumeration (DI registration in `Program.cs`, mappers, controller, tests, other Xcc tile primitives) | Medium | Run repository-wide grep for the three (now four) FQNs and `IUserDashboardSettingsRepository`, `UserDashboardSettings`, `UserDashboardTile`, `IDashboardService`, `DashboardService` symbols — verified that 187 files match the namespace patterns today; most are migration designers (leave frozen), but the controller, tests, and DI registrations must update. |
| `ApplicationDbContext.cs:29` (`using Anela.Heblo.Xcc.Domain;`) might be needed for `Entity<T>` independently of the dashboard types | Low | Inspect the file before removing the using; if `Entity<T>` or other Xcc.Domain types are referenced, keep the using and add the new one. |
| Removing empty Xcc directories causes git noise without value | Low | Acceptable; matches spec FR-5. Verify `Xcc/Domain/` still contains `Entity.cs`/`IEntity.cs` and is therefore not removed; `Xcc/Services/Dashboard/` still contains tile-registry files and is therefore not removed. |

## Specification Amendments

**Amendment 1 (required to unblock the build): Expand FR-3 / Out-of-Scope to cover `IDashboardService` and `DashboardService`.**
The spec's "Out of Scope" bullet `Issue #1943 (DashboardService placement) — explicitly out of scope` must be relaxed: the minimum subset required to break the circular dependency is moving `IDashboardService` to `Anela.Heblo.Domain/Features/Dashboard/` and `DashboardService` to `Anela.Heblo.Application/Features/Dashboard/Services/`. Without this, the relocation produces a `Xcc → Domain` reference on top of the existing `Domain → Xcc`, which the C# compiler will reject. The rest of issue #1943 (tile registry refactors, etc.) remains out of scope.

**Amendment 2: Add `ApplicationDbContextModelSnapshot.cs` to the FR-4 caller list.**
The spec enumerates handlers, configurations, and the repository implementation but omits the EF model snapshot. The snapshot embeds the entity FQNs as string literals at lines 3506, 3530, 3916, and 3918, and must be edited in lockstep to avoid a spurious next-migration drift. State explicitly that historical `*.Designer.cs` files are **not** to be modified.

**Amendment 3: Tighten FR-6's "no new migration" claim.**
Reword to: "No new EF Core migration is generated, **on the condition that** `ApplicationDbContextModelSnapshot.cs` is edited to use the new FQN strings; otherwise the next `dotnet ef migrations add` will emit a snapshot-only migration." Add an acceptance check: `dotnet ef migrations add Probe --no-build` in a scratch checkout yields an empty `Up`/`Down`.

**Amendment 4: Add a circular-reference acceptance check.**
After implementation, `Anela.Heblo.Xcc.csproj` MUST still contain zero `ProjectReference` entries to other `Anela.Heblo.*` projects. This is the architectural invariant that determines whether the refactor succeeded.

**Amendment 5: Clarify the using-statement strategy in `ApplicationDbContext.cs`.**
The file currently has `using Anela.Heblo.Xcc.Domain;` which serves both `Entity<T>` (if referenced) and the moved dashboard types. Implementer must verify which is needed and adjust accordingly rather than blindly replacing the using.

## Prerequisites

None — no infrastructure, configuration, migration, or external setup required. The change is entirely contained in source code, csproj layouts, and one auto-generated-but-checked-in snapshot file.

Pre-implementation verification checklist (do these once before starting):
1. Confirm `Anela.Heblo.Xcc.csproj` has no `Anela.Heblo.*` project references (verified: only NuGet packages today).
2. Confirm `Anela.Heblo.Domain.csproj` references `Anela.Heblo.Xcc` (verified: line 21).
3. Confirm `Anela.Heblo.Application.csproj` references both Domain and Xcc (verify before starting).
4. Run a baseline `dotnet build` + `dotnet test` to record the green-baseline state.