I have enough context to write the review.

```markdown
# Architecture Review: Decouple Dashboard Tile Drill-Downs from Frontend Routing

## Skip Design: true

(Backend contract change + a thin frontend resolver. No new visual components; tiles render identically.)

## Architectural Fit Assessment

The feature directly restores a violated rule from `docs/architecture/development_guidelines.md` ("Backend constructing frontend URLs"). The proposal fits the codebase, but the spec was written without enough knowledge of two existing facts that change the design:

1. **Tile payloads are not OpenAPI-typed.** `DashboardController.GetTileData` returns `DashboardTileDto.Data: object?` (`backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDto.cs:13`). Each tile returns an anonymous object from `LoadDataAsync(): Task<object>`. The generated TypeScript client therefore sees `data: any` (`frontend/src/api/hooks/useDashboard.ts:13`). **The new `DashboardTileDrillDown` C# class will not flow into the generated client.** FR-1's acceptance criterion "The OpenAPI-generated TypeScript type reflects the new shape" is unrealistic without extra plumbing.

2. **A drill-down pattern already exists and is different.** Filter-based tiles (`PurchaseOrdersInTransitTile`, `LowStockAlertTile`, `BackgroundTasksTile`, `CountTile`, `InventorySummaryTile`, etc.) use `drillDown: { filters, enabled, tooltip }` — no URL, no route key (`backend/src/Anela.Heblo.Application/Features/Dashboard/Tiles/PurchaseOrdersInTransitTile.cs:51`, `frontend/src/utils/urlUtils.ts:23`). The base path is hardcoded *inside the frontend tile component* (`'/purchase/orders'`, `'/logistics/inventory'`, etc.) and the existing `createFilteredUrl()` glues filters onto it. These tiles already comply with the "frontend owns routing" rule — the three URL-emitting tiles are outliers. After this change the codebase will have **two drill-down shapes** living side by side until a future pass unifies them.

There's also a behavioral truth the spec doesn't acknowledge: **for all three target tiles, the frontend already ignores the backend's URL string.** `DataQualityTile.tsx:22`, `DqtYesterdayStatusTile.tsx:35`, and `FailedJobsTile.tsx:15` hardcode their navigation targets. So the present "drift" exists only in the wire payload, not in user-visible navigation. This means the change must update both sides (kill the backend URL, swap the frontend hardcoded path for a resolver call) — which the spec correctly captures.

There is one cross-origin trap the spec misses (see Risks).

## Proposed Architecture

### Component Overview

```
+--------------------- BACKEND ---------------------+    +-------------------- FRONTEND --------------------+
|                                                   |    |                                                  |
|  Feature modules (DataQuality, BackgroundJobs)    |    |  api/hooks/useDashboard.ts                       |
|        |                                          |    |     | DashboardTile.data: any                    |
|        | reference                                |    |     v                                            |
|        v                                          |    |  components/dashboard/tiles/<TileName>.tsx       |
|  Application/Features/Dashboard/Contracts/        |    |     |                                            |
|    DashboardTileDrillDown                         |    |     | reads drillDown { routeKey, enabled,       |
|        ^                                          |    |     | parameters? }                              |
|        |                                          |    |     v                                            |
|        | embedded in LoadDataAsync() return       |    |  components/dashboard/drillDownRoutes.ts         |
|                                                   |    |    DASHBOARD_DRILLDOWN_ROUTES (closed map)       |
|  DashboardController/GetTileData                  |    |    resolveDrillDown(drillDown): Resolution|null  |
|        | object? Data                             |--->|                                                  |
|                                                   |    |     |                                            |
+---------------------------------------------------+    |     v                                            |
                                                         |  react-router useNavigate    OR                  |
                                                         |  window.location.assign(`${apiUrl}${path}`)      |
                                                         |  for backend-mounted admin UIs (Hangfire)        |
                                                         +--------------------------------------------------+
```

### Key Design Decisions

#### Decision 1: Where the `DashboardTileDrillDown` class lives

**Options considered:**
- (a) `Anela.Heblo.Application/Features/Dashboard/Contracts/`
- (b) `Anela.Heblo.Xcc/Services/Dashboard/` (next to `ITile`)
- (c) A new shared "tile result contracts" folder

**Chosen approach:** (a) `Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs`.

**Rationale:** The Dashboard feature module owns the tile-data API surface (it owns `DashboardTileDto`, `GetTileDataHandler`, and the tile registry orchestration). Tile-author modules already take an implicit dependency on the Dashboard module's contracts when they assemble tile payloads — making that dependency explicit by referencing a typed DTO is consistent with `development_guidelines.md`'s "Communication between modules exclusively through `contracts/`". Xcc is rejected because the same doc lists "DTOs defined in API or Xcc" as a forbidden practice. Option (c) is YAGNI.

#### Decision 2: How the route-key contract reaches TypeScript

**Options considered:**
- (a) Hand-maintain the TS type in `frontend/src/components/dashboard/drillDownRoutes.ts` (same approach as the existing `DrillDownInfo` in `urlUtils.ts`).
- (b) Add a synthetic OpenAPI registration so NSwag emits `DashboardTileDrillDown` into the client.
- (c) Promote `DashboardTileDrillDown` to a strongly-typed field on `DashboardTileDto` (e.g., `DrillDown` alongside `Data`).

**Chosen approach:** (a). The frontend defines `DashboardDrillDownRouteKey` and the consumed payload shape by hand.

**Rationale:** The tile data envelope is `object?` by design — every tile returns a different shape. Forcing the drill-down into the typed envelope (option c) either creates a useless empty field on tiles that have no drill-down or splits the response into two parallel structures. Option (b) (synthetic schema registration) adds NSwag complexity for one type and still leaves `data: any`. Option (a) matches the existing pattern (`DrillDownInfo` is also hand-defined) and keeps the closed-union of `DashboardDrillDownRouteKey` as a compile-time safety net — adding a backend route key without a frontend entry will fail TypeScript and be caught at build time when a developer updates the consuming tile component. **The spec's FR-1 acceptance criterion about OpenAPI must be amended accordingly.**

#### Decision 3: How "external" (Hangfire) drill-downs are resolved

**Options considered:**
- (a) Treat `external` paths as same-origin and use `window.location.assign(path)`.
- (b) Treat `external` paths as backend-origin and use `window.location.assign(`${apiUrl}${path}`)` plus `window.open(..., '_blank')` to preserve current UX.

**Chosen approach:** (b). The `external` discriminator means "mounted on the backend host"; the resolver returns the full URL after prepending `getConfig().apiUrl`. The Hangfire route key opens in a new tab to match current `FailedJobsTile.tsx` behavior.

**Rationale:** Frontend and backend are not necessarily same-origin (different ports in dev — frontend 3001, backend 5001 per `docs/architecture/environments.md`; in prod they share a host but the existing component still uses `apiUrl`). The current `FailedJobsTile.tsx:18-21` opens `${apiUrl}${HANGFIRE_PATH}` in a new tab; the resolver must preserve this. **The spec's FR-5/FR-7 are silent on this — see Specification Amendments.**

#### Decision 4: Keep the two drill-down shapes (route-key vs filters) separate for now

**Options considered:**
- (a) Convert all filter-based tiles to the new `{ routeKey, parameters }` shape in this PR.
- (b) Keep both shapes; document the divergence; plan a future unification.

**Chosen approach:** (b).

**Rationale:** The brief and spec scope this change to three tiles. Converting all filter-based tiles would touch ~6 backend tiles, 6 frontend components, and their tests — a much larger blast radius. The two shapes can coexist as long as a tile component picks the right one. Note this as tech debt in `memory/decisions/` so the next pass cleans it up.

## Implementation Guidance

### Directory / Module Structure

**Backend (new files):**
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Contracts/DashboardTileDrillDown.cs` — the new DTO class.

**Backend (edited):**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` — replace both anonymous `drillDown` payloads.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` — drop the `DrillDownHref` constant; replace four payloads.
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` — drop `FailedJobsUrl`; replace two payloads (keep `tooltip`).
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` — assertions like `doc.RootElement.GetProperty("drillDown").GetProperty("href").GetString().Should().Be("/automation/data-quality")` change to assert `routeKey == "dataQuality"`. Same for `FailedJobsTileTests.cs` and any `DataQualityStatusTile` tests if they exist.

**Frontend (new files):**
- `frontend/src/components/dashboard/drillDownRoutes.ts` — exports `DashboardDrillDownRouteKey`, `DrillDownTarget`, `DASHBOARD_DRILLDOWN_ROUTES`, and `resolveDrillDown(...)`.
- `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.ts` — unit tests for the resolver.

**Frontend (edited):**
- `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` — replace hardcoded `navigate('/automation/data-quality')` with `resolveDrillDown(data.drillDown)`-driven navigation; accept `drillDown` on `data`.
- `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` — same.
- `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx` — remove `HANGFIRE_PATH`; drive the click via the resolver (which still returns the backend-origin URL).
- Component tests under `frontend/src/components/dashboard/tiles/__tests__/`.

Note: `drillDownRoutes.ts` belongs under `components/dashboard/` (where the existing dashboard code lives), **not** `src/dashboard/` as the spec proposes. There is no `src/dashboard/` folder.

### Interfaces and Contracts

**Backend DTO** (`DashboardTileDrillDown.cs`):
```csharp
namespace Anela.Heblo.Application.Features.Dashboard.Contracts;

// Plain class, not a record — DTO serialization rule from project CLAUDE.md.
public class DashboardTileDrillDown
{
    public string RouteKey { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public IReadOnlyDictionary<string, string>? Parameters { get; set; }
}
```

The class is referenced from each tile's anonymous-object payload:
```csharp
drillDown = new DashboardTileDrillDown { RouteKey = "dataQuality", Enabled = true }
```

(The class is serialized inside an anonymous parent because `LoadDataAsync` returns `Task<object>`; this is unchanged.)

**Frontend resolver** (`drillDownRoutes.ts`):
```typescript
export type DashboardDrillDownRouteKey = 'dataQuality' | 'hangfireFailedJobs';

export type DrillDownTarget =
  | { type: 'react-router'; path: string }
  | { type: 'external'; path: string };  // 'external' = mounted on backend origin

export interface DashboardTileDrillDown {
  routeKey: string;
  enabled: boolean;
  parameters?: Record<string, string>;
}

export interface DrillDownResolution {
  url: string;                                  // for 'external', already prefixed with apiUrl
  strategy: DrillDownTarget['type'];
}

export const DASHBOARD_DRILLDOWN_ROUTES: Record<DashboardDrillDownRouteKey, DrillDownTarget> = {
  dataQuality:       { type: 'react-router', path: '/automation/data-quality' },
  hangfireFailedJobs:{ type: 'external',     path: '/hangfire/jobs/failed' },
};

export function resolveDrillDown(
  drillDown: DashboardTileDrillDown | undefined
): DrillDownResolution | null { /* lookup, prepend apiUrl for external, warn + null on unknown */ }
```

Tile components call `resolveDrillDown(data.drillDown)` and dispatch based on `strategy`:
- `react-router` → `useNavigate()(resolution.url)`
- `external` → `window.open(resolution.url, '_blank')` (preserves FailedJobsTile UX)

### Data Flow

`GET /api/dashboard/data` →
`DashboardController.GetTileData` →
`GetTileDataHandler` (iterates user's enabled tiles) →
each `ITile.LoadDataAsync` returns an anonymous object containing `drillDown: new DashboardTileDrillDown { RouteKey = "...", Enabled = true }` →
serialized as `DashboardTileDto.Data: object?` →
frontend `useTileData` hook (typed as `data: any`) →
`TileContent.tsx` dispatches by `tileId` to the right tile component →
tile component calls `resolveDrillDown(data.drillDown)` →
on click: navigate (react-router) or open new tab (external, backend-origin).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Backend ships first; frontend has no entry for a new route key → tile becomes dead. | Medium | `resolveDrillDown` returns `null` and renders the tile as `Enabled = false` with a `console.warn`. Documented in FR-7. |
| `FailedJobsTile` regression: Hangfire on different origin in dev. | High | `external` resolution prepends `getConfig().apiUrl` and `window.open(...)`. Without this, the current cross-origin/new-tab behavior breaks. Must be explicit in the resolver contract and covered by a frontend test. |
| Backend tests today assert on `drillDown.href` / `drillDown.url`. Those assertions will silently pass against a misspelled new field if the JSON parsing path is tolerant. | Low | Update existing tests to assert `routeKey` and explicitly assert the absence of `href`/`url` keys, OR drop the JSON-DOM tests in favor of deserializing into a small private record that fails if the property name changes. |
| Two drill-down contracts (filters-based and routeKey-based) coexist; future tile authors won't know which to pick. | Medium | Add a short note in `memory/patterns/dashboard-tiles.md` (or wherever tile authoring is documented). State that new tiles SHOULD use `DashboardTileDrillDown { RouteKey, Enabled, Parameters }`; filter-based shape is legacy pending migration. |
| `Dictionary<string, string>` parameter values can't carry numbers/booleans cleanly; downstream filter URLs may need coercion. | Low | Acceptable for current 3 tiles (none use parameters). Document that values are strings and that the frontend resolver does NOT inject parameters into the URL automatically — the consuming tile decides how to apply them (matches the existing `createFilteredUrl` pattern). |
| Generated TypeScript client does not emit `DashboardTileDrillDown`. | Low | Hand-define the TS type in `drillDownRoutes.ts` (same approach as `DrillDownInfo` in `urlUtils.ts`). Document that the contract is mirrored manually. |

## Specification Amendments

1. **FR-1 acceptance criterion "The OpenAPI-generated TypeScript type reflects the new shape" must be removed.** The tile data envelope is `object?` (`DashboardTileDto.Data`); NSwag will not pick up a type used only inside an anonymous projection. The frontend mirrors the type by hand under `drillDownRoutes.ts`. Replace with: *"A TypeScript interface `DashboardTileDrillDown` exists in `drillDownRoutes.ts` and matches the C# shape; deviation between the two is caught by tile-component tests, not the OpenAPI client."*

2. **FR-4 / FR-5 must call out the cross-origin Hangfire concern.** The current `FailedJobsTile.tsx` does `window.open(`${getConfig().apiUrl}${HANGFIRE_PATH}`, '_blank')`. The resolver's `external` type must prepend `getConfig().apiUrl` to the path so this still works when frontend and backend are not same-origin (dev: 3001 vs 5001). The Hangfire drill-down must continue to open in a **new tab** to preserve current UX — record this in the resolver contract.

3. **FR-5 file path is wrong.** Frontend dashboard code lives at `frontend/src/components/dashboard/`, not `frontend/src/dashboard/`. Place new files under `frontend/src/components/dashboard/`.

4. **FR-2 acceptance criterion about "navigates to the same destination as `DqtYesterdayStatusTile`" is already true today.** `DataQualityTile.tsx:22` already hardcodes `/automation/data-quality`. The visible drift was only on the wire payload. The acceptance criterion is fine but worth flagging so the reviewer doesn't expect to verify a visible navigation change.

5. **Note the coexistence of two drill-down shapes.** Add a NFR or a "Compatibility" note: this PR does **not** change filter-based tiles (`PurchaseOrdersInTransitTile`, `LowStockAlertTile`, `BackgroundTasksTile`, `CountTile`, `InventorySummaryTile`, etc.). Their `drillDown: { filters, enabled, tooltip }` shape and `urlUtils.ts` helpers remain. Tile authors of *new* tiles SHOULD prefer `DashboardTileDrillDown`.

6. **DqtYesterdayStatusTile error path** (`backend/src/.../DqtYesterdayStatusTile.cs:90-95`): the frontend's error branch (`DqtYesterdayStatusTile.tsx:37-46`) renders a non-clickable error tile. The backend still emits a drill-down on error. That is fine — keep emitting it for consistency; the frontend just doesn't render a click handler in the error state. No acceptance-criterion change, but the implementer shouldn't try to remove the error-state drill-down.

7. **Test scope is broader than FR-2 implies.** `DataQualityStatusTile` does not currently have a backend unit test file (only `DqtYesterdayStatusTileTests.cs` and `FailedJobsTileTests.cs` exist). Adding one is recommended as part of this work to keep coverage symmetric.

## Prerequisites

- No migrations, infrastructure, or configuration changes are required.
- No new packages.
- No environment variables.
- Coordinated deploy is NOT required for compatibility — if backend deploys first with route keys the frontend doesn't yet know, FR-7 keeps the tile rendered (drill-down suppressed, warn logged). If frontend deploys first, it still expects `drillDown.routeKey`; the old backend emits `drillDown.href`/`url` — `resolveDrillDown(undefined)` returns `null` and the tile renders non-interactive until backend catches up. Acceptable.
```