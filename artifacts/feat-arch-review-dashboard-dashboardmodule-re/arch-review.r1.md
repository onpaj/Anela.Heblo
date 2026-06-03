# Architecture Review: Move Dashboard Tile Registrations to Owning Modules

## Skip Design: true

## Architectural Fit Assessment

This refactor strengthens the existing architecture — it does not introduce a new pattern, it restores the one already followed by every other module in the codebase.

Verified facts on disk:
- `RegisterTile<T>()` is a `IServiceCollection` extension method defined in `Anela.Heblo.Xcc.Services.Dashboard.TileRegistryExtensions` (`backend/src/Anela.Heblo.Xcc/Services/Dashboard/TileRegistryExtensions.cs:16`). It lives in the **Xcc cross-cutting layer**, not inside the Dashboard feature module.
- Every other feature module that owns tiles already imports `Anela.Heblo.Xcc.Services.Dashboard` and calls `RegisterTile<T>()` from its own module file: `CatalogModule.cs:112-117`, `LogisticsModule.cs:29-32`, `ManufactureModule.cs:63-66`, `PurchaseModule.cs:26`, `AnalyticsModule.cs:40`, `WeatherForecastModule.cs:11`, plus `XccModule.cs:26` for its own `BackgroundTaskStatusTile`.
- `DashboardModule.cs` currently violates this by registering three foreign tiles (`DataQualityStatusTile`, `DqtYesterdayStatusTile`, `FailedJobsTile`) on behalf of other modules.
- `DashboardModule.cs:20` legitimately keeps `PurchaseOrdersInTransitTile` — this is a Dashboard-owned tile (lives under `Features/Dashboard/Tiles`) and stays put. The spec is silent on it but its retention is implied by FR-3; calling that out explicitly is the only ambiguity worth removing.

The spec's worry that recipient modules might need a new dependency is unfounded. `Anela.Heblo.Xcc` is the shared cross-cutting layer every Application module already references. Adding `using Anela.Heblo.Xcc.Services.Dashboard;` to `BackgroundJobsModule` and `DataQualityModule` introduces zero new project-level coupling.

## Proposed Architecture

### Component Overview

```
              Anela.Heblo.Xcc.Services.Dashboard
              (ITileRegistry, RegisterTile<T>, ITile contracts)
                        ▲           ▲           ▲
                        │           │           │
              ┌─────────┴───┐   ┌───┴──────┐  ┌─┴──────────────┐
              │ Dashboard   │   │ Background│  │ DataQuality    │
              │ Module      │   │ JobsModule│  │ Module         │
              │             │   │           │  │                │
              │ Owns:       │   │ Owns:     │  │ Owns:          │
              │  • Purchase │   │  • Failed │  │  • DataQuality │
              │    OrdersIn │   │    Jobs   │  │    StatusTile  │
              │    Transit  │   │    Tile   │  │  • DqtYesterday│
              │  • JobStorage│   │           │  │    StatusTile  │
              │    singleton │   │           │  │                │
              └─────────────┘   └───────────┘  └────────────────┘
                                                       
   No edges between Dashboard ↔ BackgroundJobs ↔ DataQuality.
   All three depend only on the Xcc contract surface.
```

### Key Design Decisions

#### Decision 1: Tiles register in their owning module, period

**Options considered:**
- (A) Keep `DashboardModule` as the central registration point ("Dashboard owns the dashboard").
- (B) Move each `RegisterTile<T>()` call into the module that owns the tile class (the established pattern).
- (C) Introduce assembly scanning so tiles auto-register based on a marker interface.

**Chosen approach:** (B). Spec's FR-1, FR-2, FR-3 already require this.

**Rationale:** (A) is the current bug — it creates inbound coupling Dashboard → BackgroundJobs / DataQuality, which `docs/architecture/development_guidelines.md` forbids ("Don't ignore module boundaries"). (C) is explicitly out of scope and would be a much larger change with a registration-order risk. (B) is a pure relocation that aligns Dashboard with six other modules that already work this way; the resulting file is uniform across the codebase.

#### Decision 2: The Xcc dependency is fine

**Options considered:**
- (A) Have recipient modules depend on Xcc directly via `using Anela.Heblo.Xcc.Services.Dashboard;`.
- (B) Re-export `RegisterTile<T>` from a Dashboard-feature namespace so recipients route through Dashboard.

**Chosen approach:** (A).

**Rationale:** Xcc is the shared cross-cutting layer; every feature module already references it. `CatalogModule.cs:28` does exactly this today. Option (B) would invert the dependency arrow back into the same problem we are fixing — recipient modules would point at `Dashboard` again. The spec's parenthetical concern about "tile contracts are a Dashboard-provided extension point" is incorrect: the contract lives in Xcc, not in `Features/Dashboard`. Remove that line during implementation (see Specification Amendments).

#### Decision 3: `DashboardModule` keeps `PurchaseOrdersInTransitTile` and `JobStorage` singleton

**Chosen approach:** Leave `services.RegisterTile<PurchaseOrdersInTransitTile>()` (line 20) and `services.AddSingleton(_ => JobStorage.Current)` (line 17) in `DashboardModule`.

**Rationale:** `PurchaseOrdersInTransitTile` lives under `Features/Dashboard/Tiles` and is genuinely Dashboard-owned. `JobStorage` is the Hangfire storage handle that `FailedJobsTile` consumes — but it is registered as a *singleton* that any dashboard-style consumer could need, and Hangfire is bootstrapped at the host layer regardless. Moving it to `BackgroundJobsModule` is plausible but is *not in scope* per the spec ("pure relocation of registration calls"). Leave it in `DashboardModule`. Flag it in Specification Amendments for the reviewer's awareness, not for action.

## Implementation Guidance

### Directory / Module Structure

No new directories or files. Three existing files are edited:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` | Remove lines 21, 22, 23 (the three foreign `RegisterTile` calls). Remove lines 1 and 3 (the `using` directives for `BackgroundJobs.DashboardTiles` and `DataQuality.DashboardTiles`). Keep lines 2, 4–6 and the `PurchaseOrdersInTransitTile` registration on line 20. Keep the `JobStorage` singleton on line 17. |
| `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` | Add `using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;` and `using Anela.Heblo.Xcc.Services.Dashboard;`. Add `services.RegisterTile<FailedJobsTile>();` inside `AddBackgroundJobsModule` (place it alongside the existing `AddScoped<IRecurringJobStatusChecker, …>()`). |
| `backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityModule.cs` | Add `using Anela.Heblo.Application.Features.DataQuality.DashboardTiles;` and `using Anela.Heblo.Xcc.Services.Dashboard;`. Add `services.RegisterTile<DataQualityStatusTile>();` and `services.RegisterTile<DqtYesterdayStatusTile>();` inside `AddDataQualityModule`. |

### Interfaces and Contracts

No new contracts. The existing extension method
```csharp
public static IServiceCollection RegisterTile<TTile>(this IServiceCollection services)
    where TTile : class, ITile
```
in `Anela.Heblo.Xcc.Services.Dashboard.TileRegistryExtensions` is the only API touched, and only as a caller.

### Data Flow

Startup-time only — no request-path change. Per `TileRegistryExtensions.cs:9-27`:

1. Each module's `Add{Module}Module(IServiceCollection)` runs during `Startup`/`Program.cs` composition.
2. `RegisterTile<TTile>()` does two things: registers the tile as `Scoped` in DI and appends its `Type` to a static `ConcurrentBag<Type> RegisteredTileTypes`.
3. After the host is built, `app.InitializeTileRegistry()` reads that bag and hands each type to `ITileRegistry.RegisterTile<T>()` via reflection.

**Ordering caveat (verify, then ignore):** Because step 2 uses a `ConcurrentBag` and step 3 calls `.Distinct()` with no `OrderBy`, the order in which tiles appear at runtime is **not** guaranteed by source order. Moving lines between module files cannot change this — the bag is process-wide. FR-4's claim of "same order as before" is therefore already true in the weak sense (the set is unchanged) and undefined in the strict sense (order was never deterministic). The implementer should not waste effort trying to preserve a source-order that the registry never observed. If the frontend imposes a display order, it does so itself; the BE refactor cannot affect it.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `BackgroundJobsModule.AddBackgroundJobsModule()` or `DataQualityModule.AddDataQualityModule()` is not called from `Program.cs` / `ApplicationModule`, so the moved registrations silently drop. | High | Before editing, grep `AddBackgroundJobsModule\|AddDataQualityModule` to confirm both are invoked at startup. If either is missing, the tiles disappear at runtime even though build succeeds. |
| Recipient module references the `Xcc` project but not via a transitive path that exposes `Services.Dashboard`. | Low | Both modules already share the same csproj references as `CatalogModule`, which uses this exact namespace. No project file change needed; verify by a clean `dotnet build`. |
| `PurchaseOrdersInTransitTile` accidentally removed during cleanup of `DashboardModule.cs`. | Medium | Spec FR-3 is ambiguous about Dashboard-owned tiles. Reviewer must confirm line 20 stays. Add a "kept" comment is unnecessary — the surrounding context is self-evident. |
| An integration or unit test pins the *count* of registered tile types and breaks if registrations move. | Low | Grep `RegisteredTileTypes\|InitializeTileRegistry\|ITileRegistry` under `backend/test/`. If any such assertion exists, it is testing the set, not the source location, so moves are invisible to it. |
| Future reviewer assumes `JobStorage` singleton (`DashboardModule.cs:17`) should also have moved. | Low | This singleton is out of scope per the spec. Leave it; do not pre-emptively touch. |

## Specification Amendments

1. **Dependencies section is mildly incorrect.** The spec states "If `RegisterTile` lives in the Dashboard namespace, `BackgroundJobsModule` and `DataQualityModule` will add a `using` for that namespace." It does not. `RegisterTile<T>` lives in `Anela.Heblo.Xcc.Services.Dashboard` (the cross-cutting Xcc project). Both recipient modules will add `using Anela.Heblo.Xcc.Services.Dashboard;` — a dependency they share with every other feature module already. The spec's hedge about "downstream modules depend on Dashboard's contracts" is unnecessary; no Dashboard-feature dependency is introduced.

2. **FR-3 should explicitly preserve `PurchaseOrdersInTransitTile`.** Add to the acceptance criteria: "`services.RegisterTile<PurchaseOrdersInTransitTile>()` remains in `DashboardModule.cs` (it is genuinely Dashboard-owned, located under `Features/Dashboard/Tiles`)." The current wording — "still registers any tiles … if any exist after removal" — invites accidental deletion.

3. **FR-4 wording on "same order" is technically vacuous.** Tile registration order is not preserved by the registry (`ConcurrentBag` + `.Distinct()` with no ordering). Soften to: "The same set of tiles is registered after the refactor; any pre-existing display order is determined by the frontend or the registry's own behavior and is unaffected by this refactor." This avoids implementers chasing a guarantee that does not exist.

4. **Out-of-scope note for `JobStorage` singleton.** Add: "The `services.AddSingleton(_ => JobStorage.Current)` registration on `DashboardModule.cs:17` is out of scope. It is the Hangfire storage handle consumed by `FailedJobsTile` but its proper owner is debatable. Leave it untouched in this refactor."

## Prerequisites

None. This is a pure source-relocation refactor that requires:
- No migration.
- No config change.
- No new package or project reference (`Xcc.Services.Dashboard` is already on the transitive path for every feature module).
- No infrastructure change.

Validation per `CLAUDE.md`: run `dotnet build` + `dotnet format` after the edits; manually load the dashboard once locally and confirm `FailedJobsTile`, `DataQualityStatusTile`, and `DqtYesterdayStatusTile` still render.