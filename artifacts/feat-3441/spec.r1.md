# Specification: Dashboard Tile Registry (replace `TileContent.tsx` switch statement)

## Summary
`TileContent.tsx` currently dispatches to 20+ tile-specific React components via a `switch (tile.tileId)` statement. This refactor replaces the switch with a static, record-based tile registry so that registering a new dashboard tile no longer requires modifying `TileContent.tsx`. This is a pure internal refactor with no visible behavior change, no API change, and no new tile functionality.

## Background
The Dashboard module renders a configurable grid of tiles; each tile's `tileId` (a string returned by the backend, see `DashboardTile.tileId` in `frontend/src/api/hooks/useDashboard.ts`) determines which React component renders its `data`. Today that mapping lives in a 60-line `switch` inside `frontend/src/components/dashboard/tiles/TileContent.tsx` (lines 34–94), with one `case` per tile plus a `default` fallback to `DefaultTile`.

As the tile catalogue has grown past 20 entries, this file has become a recurring edit point: every new backend-registered tile requires a matching frontend `case`, and forgetting it silently falls through to `DefaultTile` (no visible error, no compile-time check binding backend tile IDs to frontend cases). The switch statement violates the Open/Closed principle — the component must be modified, not extended, to support new tiles — and is a predictable merge-conflict hotspot for a solo-developer-plus-AI-review workflow where multiple tile features may be in flight concurrently.

This spec formalizes the fix suggested by the arch-review finding: extract the mapping into a `TILE_RENDERERS` registry object co-located with `TileContent.tsx`, and have `TileContent` do a lookup instead of a switch.

## Functional Requirements

### FR-1: Tile registry module
Create a new module (e.g. `frontend/src/components/dashboard/tiles/tileRegistry.tsx`) that exports a `TILE_RENDERERS` record mapping every existing `tileId` string (all 23 cases currently in the switch, listed below) to a renderer function/component that receives `{ data, tile }` and returns the exact same JSX currently produced by the corresponding `case`.

Tile IDs to migrate (verbatim from current switch, `frontend/src/components/dashboard/tiles/TileContent.tsx:34-94`):
`backgroundtaskstatus`, `todayproduction`, `nextdayproduction`, `manufactureconditions`, `manualactionrequired`, `purchaseordersintransit`, `intransitboxes`, `receivedboxes`, `errorboxes`, `invoiceimportstatistics`, `bankstatementimportstatistics`, `productinventorycount`, `materialinventorycount`, `productinventorysummary`, `materialwithexpirationinventorysummary`, `materialwithoutexpirationinventorysummary`, `lowstockefficiency`, `criticalgiftpackages`, `lowstockalert`, `dataqualitystatus`, `dqtyesterdaystatus`, `weatherforecast`, `failedjobs`, `packingstats`.

Each renderer must preserve, byte-for-byte in behavior, the props currently passed in the switch — including:
- Default title fallbacks (`todayproduction` → `'Dnes'`, `nextdayproduction` → `'Zítra'`) when `tile.title` is falsy.
- Static icon/iconColor/targetUrl props baked into each `CountTile`-based case (e.g. `intransitboxes` → `Truck` icon, `text-blue-600`, `targetUrl="/logistics/transport-boxes"`).
- `tileCategory`/`tileTitle` props passed from `tile.category` / `tile.title` where the current switch passes them (`manualactionrequired`, `purchaseordersintransit`, and all `CountTile` cases).
- Shared component reuse where multiple tile IDs currently map to the same component with different static props (e.g. three `intransitboxes`/`receivedboxes`/`errorboxes` cases all use `CountTile` with different icon/color; three inventory-summary variants all use `InventorySummaryTile` with different `targetUrl`).

**Acceptance criteria:**
- `TILE_RENDERERS` contains exactly the 23 keys listed above, each mapping to a renderer that is behaviorally identical (same component, same static props) to today's corresponding `case`.
- No `tileId` is dropped, renamed, or altered in matching behavior (matching remains an exact, case-sensitive string match on `tile.tileId`, as today).
- The registry module has no dependency on `TileContent.tsx` (no circular import) — `TileContent.tsx` imports from the registry, not vice versa.

### FR-2: `TileContent.tsx` lookup-based dispatch
Replace the `switch` statement in `TileContent.tsx` with a lookup against `TILE_RENDERERS`, falling back to `DefaultTile` when `tile.tileId` has no matching entry (preserving today's `default` behavior, including passing `data={tile.data}` to `DefaultTile`).

**Acceptance criteria:**
- `TileContent.tsx` no longer contains a `switch` statement or per-tile `case`/import list for tile components; it imports only `TILE_RENDERERS`, `DefaultTile`, `LoadingTile`, and `UnauthorizedTile`.
- The `isUnauthorized` → `UnauthorizedTile` and `!tile.data` → `LoadingTile` guard behavior at the top of `TileContent` (lines 26–32) is unchanged.
- Unknown/empty `tileId` (including `''`) renders `DefaultTile` with `data={tile.data}`, matching current behavior.
- `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx` passes unmodified (all 20 existing test cases, including icon-color assertions, default-title assertions, and the unknown/empty tileId fallback assertions) — this test file is the executable acceptance contract for behavioral parity and should not need edits to keep passing under this refactor.

### FR-3: Extensibility for new tiles
A new dashboard tile must be addable by adding one entry to `TILE_RENDERERS` (and, if it needs a new visual component, creating that component file) — with zero edits to `TileContent.tsx` itself.

**Acceptance criteria:**
- Adding a hypothetical new `tileId` entry to `TILE_RENDERERS` and re-running the app renders that tile correctly without touching `TileContent.tsx`.
- Code review / grep confirms `TileContent.tsx`'s line count and cyclomatic complexity drop substantially (no per-tile branching logic remains in the file).

## Non-Functional Requirements

### NFR-1: Performance
No measurable regression. A single object property lookup (`TILE_RENDERERS[tile.tileId]`) is O(1) and strictly cheaper than the current linear `switch` (V8 typically compiles small string switches to a jump table or linear compare-chain of similar cost, so this is a wash-to-improvement, not a risk). No re-render behavior changes: renderer functions must not be recreated on every `TileContent` render in a way that breaks React reconciliation or memoization of child tiles (define `TILE_RENDERERS` and its renderer functions at module scope, not inside the `TileContent` function body).

### NFR-2: Security
No change. No new data enters the trust boundary; `tile.tileId` was already used as a dispatch key from backend-supplied data and continues to be used the same way (as an object-key lookup rather than a switch discriminant — both are safe against injection since neither uses `eval`/dynamic code execution; a malicious/unexpected `tileId` simply fails the lookup and falls back to `DefaultTile`, same as an unmatched `switch` case today falls to `default`).

### NFR-3: Maintainability
This is the primary driver for the change. Registering a new tile should require touching only the registry file (and optionally adding a new leaf component), never `TileContent.tsx`, eliminating the recurring merge-conflict hotspot identified in the arch-review finding.

## Data Model
No data model changes. This is a pure presentation-layer refactor.

- `DashboardTile` (`frontend/src/api/hooks/useDashboard.ts:4-15`) — unchanged. Fields relevant to dispatch: `tileId: string`, `title: string`, `category: string`, `data?: any`, `isUnauthorized?: boolean`.
- New type: `TileRenderer` — a function/component type of shape `(props: { data: any; tile: DashboardTileType }) => JSX.Element`, used as the value type in the `TILE_RENDERERS: Record<string, TileRenderer>` map.

## API / Interface Design
No backend/API changes. This is entirely internal to the React frontend.

- New file: `frontend/src/components/dashboard/tiles/tileRegistry.tsx` (or `.ts` if JSX can be avoided via `React.createElement` — `.tsx` is simpler and consistent with existing `.tsx` component files in this directory) exporting `TILE_RENDERERS: Record<string, TileRenderer>`.
- Modified file: `frontend/src/components/dashboard/tiles/TileContent.tsx` — replace switch body with:
  ```tsx
  const Renderer = TILE_RENDERERS[tile.tileId];
  return Renderer ? <Renderer data={tile.data} tile={tile} /> : <DefaultTile data={tile.data} />;
  ```
- `frontend/src/components/dashboard/tiles/index.ts` — no change required (it does not currently export per-tile components other than `BackgroundTasksTile`, `ProductionTile`, `DefaultTile`; leave as-is unless the team wants to also export `TILE_RENDERERS` for reuse, which is out of scope here since nothing currently consumes it externally).

## Dependencies
- No new external libraries.
- Depends on existing tile leaf components remaining unchanged in their own prop contracts (`BackgroundTasksTile`, `ProductionTile`, `ConditionsTile`, `ManualActionRequiredTile`, `PurchaseOrdersInTransitTile`, `CountTile`, `InventorySummaryTile`, `LowStockAlertTile`, `DataQualityTile`, `DqtYesterdayStatusTile`, `WeatherForecastTile`, `FailedJobsTile`, `PackingStatsTile`, `DefaultTile`, `LoadingTile`, `UnauthorizedTile` — all under `frontend/src/components/dashboard/tiles/`).
- Depends on `lucide-react` icons (`Truck`, `PackageCheck`, `Package`, `FileText`, `Landmark`, `ClipboardList`, `Beaker`, `AlertTriangle`, `Gift`) currently imported in `TileContent.tsx`; these move to the new registry file.
- The existing test suite `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx` (which mocks individual tile components by module path, e.g. `jest.mock('../BackgroundTasksTile', ...)`) must continue to pass without modification, since it mocks leaf components directly rather than the registry — confirm the registry's imports of these components use the same module paths (`./BackgroundTasksTile`, `./ProductionTile`, etc.) so Jest's `jest.mock` calls (which target `../BackgroundTasksTile` relative to the test file, i.e. `./BackgroundTasksTile` relative to `TileContent.tsx`) still intercept them correctly regardless of whether the import statement lives in `TileContent.tsx` or `tileRegistry.tsx`.

## Out of Scope
- Any change to which tiles exist, their visual design, their data contracts, or their backend registration (`/api/dashboard/tiles`, `/api/dashboard/data`).
- Making the registry dynamically extensible at runtime (e.g. plugin registration, backend-driven component resolution). The registry remains a static, compile-time TypeScript object — new tiles still require a code change (adding a registry entry + component file), just not a change to `TileContent.tsx`.
- Adding automated enforcement that every backend-registered `tileId` has a corresponding frontend renderer (e.g. a build-time or test-time check comparing backend tile catalogue to `TILE_RENDERERS` keys). Worth considering as a follow-up but not required to close this arch-review finding.
- Refactoring the individual leaf tile components themselves (e.g. consolidating the three near-identical `InventorySummaryTile` cases or the five `CountTile` cases into more data-driven configuration). The finding is specifically about the switch statement in `TileContent.tsx`, not the leaf components' own design.
- Updating `frontend/src/components/dashboard/tiles/index.ts` exports beyond what's needed for `TileContent.tsx` to compile.

## Open Questions
None.

## Status: COMPLETE
