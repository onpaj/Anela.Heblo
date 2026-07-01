# Design: Dashboard Tile Registry (replace `TileContent.tsx` switch statement)

## Component Design

### `tileRegistry.tsx` (new)
**Path:** `frontend/src/components/dashboard/tiles/tileRegistry.tsx`

**Responsibility:** Own the complete `tileId → renderer` mapping and all per-tile static configuration (icons, colors, target URLs, title fallbacks, `tileCategory`/`tileTitle` passthrough). This is the single place a developer edits to add, change, or remove a tile's dispatch behavior.

**Exports:**
- `TileRenderer` — type alias for a renderer component: `React.FC<{ data: any; tile: DashboardTileType }>`.
- `TILE_RENDERERS: Record<string, TileRenderer>` — a module-scope constant object with exactly 23 keys, one per existing `tileId` (listed in spec FR-1). Each value is a component created once at module load time (either an inline arrow function or a call to a local factory helper for the repeated `CountTile` pattern), never recreated per render.

**Interface contract:**
- Every renderer receives `{ data, tile }` and returns the exact JSX the corresponding switch `case` produced today (same leaf component, same static props).
- Leaf tile components it imports (`BackgroundTasksTile`, `ProductionTile`, `ConditionsTile`, `ManualActionRequiredTile`, `PurchaseOrdersInTransitTile`, `CountTile`, `InventorySummaryTile`, `LowStockAlertTile`, `DataQualityTile`, `DqtYesterdayStatusTile`, `WeatherForecastTile`, `FailedJobsTile`, `PackingStatsTile`) must be imported via the **same relative module paths** currently used in `TileContent.tsx` (e.g. `./BackgroundTasksTile`), since `TileContent.test.tsx` mocks these by module path and does not care which file holds the `import` statement.
- No import of `TileContent.tsx` (one-directional dependency: `TileContent.tsx` → `tileRegistry.tsx`, never the reverse).
- An optional local factory (e.g. `countTile(icon, color, url)` returning a `TileRenderer`) may be used to de-duplicate the repeated `CountTile`-backed entries, provided each call site remains individually traceable to one `tileId` and the factory itself is invoked at module scope (not inside any render function).

### `TileContent.tsx` (modified)
**Path:** `frontend/src/components/dashboard/tiles/TileContent.tsx`

**Responsibility:** Reduced to three concerns only — the `isUnauthorized` guard, the `!tile.data` loading guard, and a lookup-based dispatch to the resolved renderer (or `DefaultTile` fallback). No per-tile branching, no per-tile component imports, no static prop configuration.

**Interface contract:**
- Imports only `TILE_RENDERERS` (from `./tileRegistry`), `DefaultTile`, `LoadingTile`, `UnauthorizedTile`.
- Behavior, in order:
  1. `tile.isUnauthorized` → render `<UnauthorizedTile />`.
  2. `!tile.data` → render `<LoadingTile />`.
  3. `TILE_RENDERERS[tile.tileId]` lookup: if found, render `<Renderer data={tile.data} tile={tile} />`; if not found (including `''` or any unmatched string), render `<DefaultTile data={tile.data} />`.
- Contract rule for future changes: a PR that adds a new tile must touch only `tileRegistry.tsx` (plus, optionally, a new leaf component file). Any PR that adds a new leaf-component import or branching logic to `TileContent.tsx` itself indicates the registry pattern is being bypassed and should be rejected in review.

### Leaf tile components (unchanged)
All existing per-tile components (`CountTile`, `InventorySummaryTile`, `ConditionsTile`, etc.) are untouched — same props, same file location, same export. This refactor only relocates *how* they are selected, not their own implementation or contract.

### `index.ts` (unchanged)
No new exports required. `TILE_RENDERERS` is not consumed outside `tileRegistry.tsx`/`TileContent.tsx`, so it is not added to the barrel export.

## Data Schemas

No data model or API changes. This is a presentation-dispatch-only refactor.

- **`DashboardTile`** (`frontend/src/api/hooks/useDashboard.ts:4-15`) — unchanged shape, fields relevant to dispatch:
  ```ts
  interface DashboardTile {
    tileId: string;
    title: string;
    category: string;
    data?: any;
    isUnauthorized?: boolean;
    // ...other existing fields, unchanged
  }
  ```
- **New type `TileRenderer`** (introduced in `tileRegistry.tsx`, internal to the frontend — not an API contract):
  ```ts
  type TileRenderer = React.FC<{ data: any; tile: DashboardTile }>;
  ```
- **`TILE_RENDERERS` shape:**
  ```ts
  type TileRendererMap = Record<string, TileRenderer>;
  ```
  Keys: the 23 `tileId` strings enumerated in the spec (verbatim, case-sensitive, matching backend-supplied values exactly — no normalization).

- No backend endpoint, request/response payload, or event schema is added, removed, or altered. `/api/dashboard/tiles` and `/api/dashboard/data` contracts are out of scope and unaffected.
