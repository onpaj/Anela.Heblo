# Architecture Review: Relocate `PurchaseOrdersInTransitTile` to Purchase Module

## Skip Design: true

## Architectural Fit Assessment

This refactor is a textbook alignment with the codebase's established conventions. The verified state:

- **Pattern is established and consistent.** Eight modules register their own dashboard tiles via `services.RegisterTile<T>()`: `Purchase` (`LowStockEfficiencyTile`), `Logistics` (4 tiles), `Manufacture` (4 tiles), `Catalog` (6 tiles), `Analytics`, `WeatherForecast`, plus `Xcc` for its own `BackgroundTaskStatusTile`. Each tile lives at `Features/{Module}/DashboardTiles/` (or equivalent) and is registered in `{Module}Module.cs`. `LowStockEfficiencyTile.cs:5` is the direct sibling reference for the move.
- **Module-independence rule applies.** `docs/architecture/development_guidelines.md` line 34 explicitly forbids "Direct access to another module's entities." `PurchaseOrdersInTransitTile.cs:1` violates it by importing `Anela.Heblo.Domain.Features.Purchase` from within the Dashboard module's namespace.
- **Boundary enforcement exists but does not yet cover this edge.** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces eight namespace-prefix rules (Leaflet→KnowledgeBase, Logistics→Manufacture, Purchase→Catalog, etc.). There is no `Dashboard → Purchase` rule today, which is why the violation went undetected by CI until the daily arch-review found it.
- **No test fallout.** Grep for `PurchaseOrdersInTransitTile` across `backend/test/**` returns zero matches; the type is consumed only by the registration line in `DashboardModule.cs:20` and instantiated reflectively via the tile registry.
- **Frontend independence confirmed.** `frontend/src/components/dashboard/tiles/PurchaseOrdersInTransitTile.tsx` and `TileContent.tsx` reference the tile by its registered string key, not by backend namespace. No frontend change is required.

The refactor is structurally sound and low-risk. No architectural objections.

## Proposed Architecture

### Component Overview

```
Before                                          After
──────────────────────────────────────          ──────────────────────────────────────
Features/                                       Features/
├── Dashboard/                                  ├── Dashboard/
│   ├── DashboardModule.cs                      │   ├── DashboardModule.cs
│   │   └─ RegisterTile<PurchaseOrders…>  ✗     │   │   (no Purchase reference)         ✓
│   └── Tiles/                                  │   └── (Tiles/ folder may be empty,
│       └── PurchaseOrdersInTransitTile  ✗     │        see spec amendment below)
│           └─ uses IPurchaseOrderRepo          │
│              (cross-module dep)               │
│                                               │
└── Purchase/                                   └── Purchase/
    ├── PurchaseModule.cs                           ├── PurchaseModule.cs
    │   └─ RegisterTile<LowStockEff…>               │   ├─ RegisterTile<LowStockEff…>
    │                                               │   └─ RegisterTile<PurchaseOrders…> ✓
    └── DashboardTiles/                             └── DashboardTiles/
        └── LowStockEfficiencyTile                       ├── LowStockEfficiencyTile
                                                         └── PurchaseOrdersInTransitTile ✓
                                                             └─ uses IPurchaseOrderRepo
                                                                (intra-module — OK)
```

### Key Design Decisions

#### Decision 1: Mirror the `LowStockEfficiencyTile` placement exactly
**Options considered:**
- A) Place under `Features/Purchase/DashboardTiles/` (matches `LowStockEfficiencyTile`).
- B) Place under `Features/Purchase/UseCases/Dashboard/` or similar to group with use-case handlers.
- C) Create a new `Features/Purchase/Tiles/` folder.

**Chosen approach:** A — `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`.

**Rationale:** `LowStockEfficiencyTile` is the canonical Purchase-domain tile and already establishes this folder name. The spec explicitly references mirroring it. Diverging here would create yet another inconsistency.

#### Decision 2: Keep the dependency on `IPurchaseOrderRepository`, don't introduce an interface inversion
**Options considered:**
- A) Direct dependency on `IPurchaseOrderRepository` (status quo, now legal because tile and repo are in the same module).
- B) Define a Dashboard-owned `IPurchaseOrderInTransitSource` contract implemented by Purchase (pattern from `ILeafletKnowledgeSource`).

**Chosen approach:** A — direct dependency, no inversion.

**Rationale:** The `ILeafletKnowledgeSource` adapter pattern exists for *cross-module read access* (consumer in module X, provider in module Y). After this refactor, both the tile and the repository live in the same Purchase module — there is no boundary to invert. The tile is a Purchase capability that happens to render on the dashboard surface; the cross-cutting contract is `ITile` (in `Xcc.Services.Dashboard`), not the repository access.

#### Decision 3: Do not extend `ModuleBoundariesTests` in this PR
**Options considered:**
- A) Add a `Dashboard → Purchase` rule to `ModuleBoundariesTests.Rules()` to prevent regression.
- B) Leave the test suite untouched.

**Chosen approach:** B — leave untouched (with caveat below).

**Rationale:** Spec's "Out of Scope" explicitly excludes "Adding or modifying tests beyond what is required to keep the existing suite green." Adding the rule is a *legitimate* follow-up — see Risk R3 — but belongs in its own brief alongside a broader audit of which module pairs should have boundary rules. Doing it inline expands scope and invites incidental findings.

## Implementation Guidance

### Directory / Module Structure

**Files to modify:**

| Action | Path | Change |
|---|---|---|
| Move | `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs` → `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs` | File location + `namespace` line only |
| Edit | `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs` | Remove `using Anela.Heblo.Application.Features.Dashboard.Tiles;` (line 2) and `services.RegisterTile<PurchaseOrdersInTransitTile>();` (line 20) |
| Edit | `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` | Add `services.RegisterTile<PurchaseOrdersInTransitTile>();` adjacent to the existing `LowStockEfficiencyTile` registration on line 26. `using Anela.Heblo.Application.Features.Purchase.DashboardTiles;` is already imported on line 5 — no new using needed. |

**Files NOT to modify:**
- `PurchaseOrdersInTransitTile.cs` body — class name, members, ctor, `LoadDataAsync` logic, `FormatAmountInThousands` helper all stay byte-identical.
- `frontend/src/components/dashboard/tiles/PurchaseOrdersInTransitTile.tsx` — frontend resolves tiles by registered key, not backend namespace.
- `ITile` / `TileRegistry` / `TileRegistryExtensions` — registration contract is stable.

### Interfaces and Contracts

- **`ITile` interface** (`Anela.Heblo.Xcc.Services.Dashboard.ITile`) — unchanged. The tile implements `Title`, `Description`, `Size`, `Category`, `DefaultEnabled`, `AutoShow`, `ComponentType`, `RequiredPermissions`, and `LoadDataAsync(...)`. Category remains `TileCategory.Purchase`.
- **`IPurchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus.InTransit, ct)`** — unchanged contract, unchanged call site, just now invoked from inside the owning module.
- **`RegisterTile<T>()` extension** — unchanged; PurchaseModule already uses it for `LowStockEfficiencyTile`.

### Data Flow

Identical before and after. The tile registry instantiates the tile via DI; the dashboard `GetTileData` handler calls `LoadDataAsync`; the tile queries `IPurchaseOrderRepository`, computes a sum, formats it, and returns the response object. None of this changes.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| **R1.** A stale string literal (e.g. type name as a registration key, log filter, or feature-flag key) references the old fully-qualified namespace. | Low | Grep before merging: `rg "Anela\.Heblo\.Application\.Features\.Dashboard\.Tiles\.PurchaseOrdersInTransitTile"` and `rg "Features\.Dashboard\.Tiles\.PurchaseOrders"` across the repo. Verified zero matches today other than the file itself; re-run after the move to confirm cleanup. |
| **R2.** Tile-enabled state stored per user (e.g. in `UserDashboardSettings`) keys off the type name and breaks on migration. | Low | Inspect `Features/Dashboard/Contracts/UserDashboardTileDto.cs` and the `GetAvailableTiles` / `SaveUserSettings` handlers to confirm the persisted key is the unqualified type name (`"PurchaseOrdersInTransitTile"`) or a registry-assigned ID, not the full namespace. If the FQN is persisted, add a data-migration note to the spec. |
| **R3.** Regression: a future change re-introduces a Dashboard module dependency on `Anela.Heblo.Domain.Features.Purchase`, since no `ModuleBoundariesTests` rule guards this edge. | Medium | Out of scope per spec, but file a follow-up brief: add `Dashboard → Purchase` to `Rules()` in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` with an empty allowlist. Same pattern as the existing `Purchase → Catalog` rule (line 142–151). |
| **R4.** The orphaned `Features/Dashboard/Tiles/` folder remains after the move with no tiles inside it, becoming a "dead" folder that invites confusion. | Low | After move, `Features/Dashboard/Tiles/` will be empty (no other tile lives there). Decide explicitly: either delete the folder or leave it (and remove the stray folder later if no tile is added back). See spec amendment below. |
| **R5.** Architecture-test suite (`ModuleBoundariesTests`) is reflection-based and only runs at test-time. A misregistration (e.g. forgot to add to PurchaseModule, forgot to remove from DashboardModule, or accidentally double-registered) is not caught by `dotnet build`. | Low | Run `dotnet test --filter FullyQualifiedName~Anela.Heblo.Tests` after the move; the existing dashboard tile-discovery integration test (if any) plus a manual sanity check that the dashboard still lists "Suma nákupních objednávek" at staging confirms registration. |

## Specification Amendments

1. **FR-3 first bullet is moot as written.** The spec says: *"`DashboardModule.cs` no longer carries the `using Anela.Heblo.Domain.Features.Purchase;` statement…"* That using is **not present** in the current `DashboardModule.cs` (verified at `backend/src/Anela.Heblo.Application/Features/Dashboard/DashboardModule.cs:1-6`). The actual stale using that must be removed is `using Anela.Heblo.Application.Features.Dashboard.Tiles;` (line 2), since after the move no other registered tile in that file lives in the `Dashboard.Tiles` namespace. **Amend FR-3** to read: *"Remove `using Anela.Heblo.Application.Features.Dashboard.Tiles;` from `DashboardModule.cs` once the registration line is deleted."*

2. **Add explicit instruction about the empty source folder.** After the move, `backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/` will be empty. Add an FR-5 (or amend FR-1): *"Delete the now-empty `Features/Dashboard/Tiles/` folder if your VCS / build tooling does not preserve empty directories. Do not leave a `.gitkeep` placeholder."*

3. **Confirm tile-identity key.** Before declaring the spec complete, add a check item under FR-4: *"Verify that the per-user tile-enabled persistence (in `UserDashboardSettings`) is unaffected — i.e., the persisted key is the unqualified class name `\"PurchaseOrdersInTransitTile\"` (or a registry-assigned ID) and not the assembly-qualified type name. If it is the FQN, ship a data migration."* This is the only place where the namespace change has a non-zero chance of producing user-visible breakage.

4. **Optional follow-up (not blocking).** Mention in the spec's "Out of Scope" section that `DashboardModule.cs` continues to register `DqtYesterdayStatusTile` and `FailedJobsTile` (both lives under `DataQuality.DashboardTiles` and `BackgroundJobs.DashboardTiles` respectively). This split-registration pattern is inconsistent with the convention but is explicitly *not* this brief's concern; document it as a known follow-up so the reviewer doesn't flag it as an oversight.

## Prerequisites

None. This is a self-contained source-tree refactor:
- No database migrations.
- No configuration changes (no Azure Key Vault entries, no App Settings).
- No infrastructure or CI/CD changes.
- No frontend changes.
- No new NuGet packages.
- No coordinated deployment — a single build artifact contains both the removal and the re-registration; runtime DI resolution at startup picks up the tile from PurchaseModule, no in-flight requests are impacted.

Implementation can begin immediately. Validation gate is the standard `dotnet build` + `dotnet format` + existing test suite (per `CLAUDE.md`'s "Validation before completion"), plus a smoke check of the dashboard at staging that the "Suma nákupních objednávek" tile still renders.