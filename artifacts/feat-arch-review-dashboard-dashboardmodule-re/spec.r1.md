# Specification: Move Dashboard Tile Registrations to Owning Modules

## Summary
Refactor `DashboardModule.cs` to remove direct registrations of tiles owned by `BackgroundJobs` and `DataQuality` modules. Each owning module will register its own tiles, restoring the module-boundary pattern used everywhere else in the codebase.

## Background
The repository follows a Vertical Slice / Clean Architecture pattern where each feature module owns its own DI registrations, including any dashboard tiles it contributes. `CatalogModule`, `LogisticsModule`, `ManufactureModule`, and `PurchaseModule` all follow this convention.

`DashboardModule.cs` (lines 1–23) is the sole exception: it imports namespaces from `BackgroundJobs` and `DataQuality` and registers three tiles that belong to those modules:

- `DataQualityStatusTile` — owned by DataQuality
- `DqtYesterdayStatusTile` — owned by DataQuality
- `FailedJobsTile` — owned by BackgroundJobs

This creates inbound coupling from `Dashboard` to `BackgroundJobs` and `DataQuality`, violating module boundary rules documented in `docs/architecture/development_guidelines.md`. It also means adding/removing tiles in those modules requires editing `DashboardModule`, and removing either module would leave a dangling import in Dashboard.

This refactor is a pure relocation of registration calls — no behavioral change, no logic change, no new tiles.

## Functional Requirements

### FR-1: Move `FailedJobsTile` registration to `BackgroundJobsModule`
The `services.RegisterTile<FailedJobsTile>()` call must be relocated from `DashboardModule.cs` into `BackgroundJobsModule.cs` so that BackgroundJobs owns its own tile registration.

**Acceptance criteria:**
- `BackgroundJobsModule.cs` calls `services.RegisterTile<FailedJobsTile>()` inside its module registration method.
- The corresponding call is removed from `DashboardModule.cs`.
- The `using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;` directive is removed from `DashboardModule.cs` if no longer referenced.
- `FailedJobsTile` continues to appear on the dashboard at runtime (registration occurs during application startup as before).

### FR-2: Move DataQuality tile registrations to `DataQualityModule`
The `services.RegisterTile<DataQualityStatusTile>()` and `services.RegisterTile<DqtYesterdayStatusTile>()` calls must be relocated from `DashboardModule.cs` into `DataQualityModule.cs`.

**Acceptance criteria:**
- `DataQualityModule.cs` calls both `services.RegisterTile<DataQualityStatusTile>()` and `services.RegisterTile<DqtYesterdayStatusTile>()` inside its module registration method.
- Both calls are removed from `DashboardModule.cs`.
- The `using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;` directive is removed from `DashboardModule.cs` if no longer referenced.
- Both tiles continue to appear on the dashboard at runtime.

### FR-3: `DashboardModule` retains ownership only of dashboard-intrinsic registrations
After the refactor, `DashboardModule.cs` must not contain any `using` directive or registration referring to types defined under `Features/BackgroundJobs/**` or `Features/DataQuality/**`.

**Acceptance criteria:**
- A grep for `BackgroundJobs` or `DataQuality` inside `DashboardModule.cs` returns no matches.
- `DashboardModule.cs` still registers any tiles or services that are genuinely owned by the Dashboard module itself (if any exist after removal).

### FR-4: Tile discovery and ordering on the dashboard are unchanged
The dashboard UI must show the same set of tiles in the same order as before the refactor. The registration mechanism (`RegisterTile<T>()`) is the only path used; no new mechanism is introduced.

**Acceptance criteria:**
- Manual or automated check of the dashboard page after the refactor lists `FailedJobsTile`, `DataQualityStatusTile`, and `DqtYesterdayStatusTile` exactly as before.
- If any existing test asserts the set or order of registered tiles, it continues to pass without modification.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. DI container registrations occur once at startup; relocating them between modules does not change runtime behavior.

### NFR-2: Security
No security impact. No auth, data access, or external integration is involved.

### NFR-3: Maintainability
The change restores the project's standard module-ownership pattern, reducing coupling and aligning `DashboardModule` with the rule documented in `docs/architecture/development_guidelines.md`. Adding or removing a tile inside `BackgroundJobs` or `DataQuality` after the refactor must not require any edit to `DashboardModule`.

### NFR-4: Backward compatibility
No public API, DTO, or persisted-data change. The refactor is internal to the Application layer's DI wiring.

## Data Model
No data model changes.

## API / Interface Design
No API surface changes. The `RegisterTile<T>()` extension method (existing) is the only interface used, and its behavior is unchanged.

Affected files (expected):
- `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` — remove three `RegisterTile` calls and two `using` directives.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — add one `RegisterTile<FailedJobsTile>()` call and the needed `using` directive.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` — add two `RegisterTile<...>()` calls and the needed `using` directive.

## Dependencies
- `RegisterTile<T>` DI extension (existing infrastructure under the Dashboard module). The recipient modules already reference whichever assembly defines this extension or can reference it through the same path `DashboardModule` currently uses. If `RegisterTile` lives in the Dashboard namespace, `BackgroundJobsModule` and `DataQualityModule` will add a `using` for that namespace — this is acceptable because tile contracts are a Dashboard-provided extension point and downstream modules depend on Dashboard's contracts, not the reverse.

## Out of Scope
- Renaming, restructuring, or modifying the tile classes themselves (`FailedJobsTile`, `DataQualityStatusTile`, `DqtYesterdayStatusTile`).
- Changing the `RegisterTile<T>()` extension method or how tiles are discovered/rendered.
- Touching tile registrations in `CatalogModule`, `LogisticsModule`, `ManufactureModule`, `PurchaseModule` — they already follow the correct pattern.
- Introducing a new auto-discovery mechanism (e.g., assembly scanning) for tiles.
- Adding new tiles or removing existing ones.
- UI/styling changes to the dashboard.

## Open Questions
None.

## Status: COMPLETE