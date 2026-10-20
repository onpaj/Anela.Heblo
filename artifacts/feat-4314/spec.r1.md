# Specification: PackingStatsTile drill-down consistency fix

## Summary
`PackingStatsTile` (frontend/src/components/dashboard/tiles/PackingStatsTile.tsx) currently defines its own ad-hoc `drillDown: {enabled, tooltip}` shape and hardcodes `navigate('/baleni')`, ignoring the `filters` field the backend tile (`Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs`) already emits. This spec brings the tile in line with the `CountTile` / `InventorySummaryTile` drill-down convention already used elsewhere on the dashboard, using the shared `urlUtils.ts` helpers and a `targetUrl` prop sourced from `tileRegistry.tsx`, with no visible behavior change for the user.

## Background
The dashboard has two established, mutually exclusive drill-down conventions for interactive tiles:

1. **`resolveDrillDown()` / `DashboardTileDrillDown`** (`frontend/src/components/dashboard/drillDownRoutes.ts`) — used by `DataQualityTile`, `FailedJobsTile`. Backend sends a `routeKey`; frontend resolves it to a URL via the `DASHBOARD_DRILLDOWN_ROUTES` registry.
2. **`createFilteredUrl(targetUrl, filters)` / `TileDataWithDrillDown`** (`frontend/src/utils/urlUtils.ts`) — used by `CountTile`, `InventorySummaryTile`, `MaterialExpirationSummaryTile`. Backend sends `drillDown: {enabled, filters, tooltip}`; the tile component receives a `targetUrl` prop (the frontend-owned route) from `tileRegistry.tsx` and builds the final URL by combining `targetUrl` with `filters` via `createFilteredUrl`.

`PackingStatsTile` matches neither: it defines a private `drillDown?: {enabled, tooltip}` prop type (no `filters`), and hardcodes the destination path (`/baleni`) inline in the `onClick` handler instead of receiving it via a `targetUrl` prop. The backend tile (`PackingStatsTile.cs`, lines 87–92) already emits the `{filters: {}, enabled: true, tooltip: "..."}` shape — i.e. it already speaks the pattern-2 (`CountTile`) protocol — so only the frontend side needs to change.

This violates the project convention that frontend owns route construction (`docs/architecture/development_guidelines.md`): the target route is baked into the tile component instead of being declared once in `tileRegistry.tsx`, the single source of truth other tiles use for their `targetUrl`.

No backend change is required or in scope.

## Functional Requirements

### FR-1: `PackingStatsTile` uses the `TileDataWithDrillDown`-based drill-down pattern
Replace the tile's private `drillDown?: {enabled, tooltip}` field with the shared `TileDataWithDrillDown` type from `frontend/src/utils/urlUtils.ts`, and replace the inline `isClickable`/`onClick` logic with the same helpers `CountTile` and `InventorySummaryTile` use: `isTileClickable(data)`, `getTileTooltip(data)`, and `createFilteredUrl(targetUrl, data.drillDown.filters)`.

**Acceptance criteria:**
- `PackingStatsTileProps` no longer declares a custom `drillDown` field; the `data` prop type is (or extends) `TileDataWithDrillDown`.
- Clickability is derived via `isTileClickable(data)`, not `data.drillDown?.enabled ?? false`.
- The tooltip is derived via `getTileTooltip(data)`, not `data.drillDown?.tooltip` directly.
- Clicking the tile (when clickable) navigates via `navigate(createFilteredUrl(targetUrl, data.drillDown.filters))`, not via a hardcoded `navigate('/baleni')` string.
- No import of `'/baleni'` or any other literal route path remains inside `PackingStatsTile.tsx`.

### FR-2: `PackingStatsTile` accepts a `targetUrl` prop
Add an optional `targetUrl?: string` prop to `PackingStatsTileProps`, following the exact convention `CountTile`/`InventorySummaryTile` use (prop passed in by the caller in `tileRegistry.tsx`, not derived internally).

**Acceptance criteria:**
- `PackingStatsTileProps` includes `targetUrl?: string`.
- The click handler only navigates when `isTileClickable(data) && targetUrl && data.drillDown?.filters` all hold (mirrors `InventorySummaryTile`'s guard) — i.e. it degrades gracefully (no navigation, no crash) if `targetUrl` is ever omitted.

### FR-3: `tileRegistry.tsx` supplies `targetUrl="/baleni"` for the `packingstats` tile
Update the existing `packingstats` entry in `frontend/src/components/dashboard/tiles/tileRegistry.tsx` to pass `targetUrl="/baleni"` into `PackingStatsTile`, matching how every `CountTile`/`InventorySummaryTile`-based entry supplies its `targetUrl`.

**Acceptance criteria:**
- The `packingstats` entry becomes `({ data }) => <PackingStatsTile data={data} targetUrl="/baleni" />` (or equivalent).
- `/baleni` appears in `tileRegistry.tsx` as the single source of the route, and nowhere else in the tile's own component file.

### FR-4: No visible behavior change
End-user behavior must be unchanged: the tile still shows the cursor-pointer affordance and tooltip when the backend marks it drill-down-enabled, and clicking it still navigates to `/baleni` (with any future non-empty `filters` from the backend now correctly appended as query parameters, which the current hardcoded `navigate('/baleni')` silently drops).

**Acceptance criteria:**
- With today's backend payload (`drillDown: {enabled: true, filters: {}, tooltip: "Přejít do modulu Balení"}`), clicking the tile navigates to exactly `/baleni` (an empty `filters` object produces no query string, per `createFilteredUrl`'s existing behavior).
- If the backend ever starts sending non-empty `filters` (e.g. `{packerId: "123"}`), the frontend now appends them as query params instead of ignoring them — this is a latent capability unlock, not something this spec requires new backend work to exercise.
- If `drillDown.enabled` is `false` or `drillDown` is absent, the tile renders non-clickable exactly as before (no cursor pointer, no click handler fires).

## Non-Functional Requirements

### NFR-1: Consistency
The resulting code must be structurally indistinguishable in style/pattern from `InventorySummaryTile.tsx` (the closest existing analog: a multi-stat, non-`CountTile`-literal tile that already uses `TileDataWithDrillDown` + `targetUrl` + the three `urlUtils` helpers). Reviewers familiar with that component should recognize the same pattern immediately in `PackingStatsTile.tsx`.

### NFR-2: No backend changes
This is a frontend-only fix. The backend already emits the correct shape (verified in `PackingStatsTile.cs` lines 87–92); it must not be modified as part of this change.

## Data Model
No new data model. Reuses the existing `TileDataWithDrillDown` / `DrillDownInfo` types already defined in `frontend/src/utils/urlUtils.ts`:

```ts
export interface DrillDownInfo {
  filters?: Record<string, any>;
  enabled: boolean;
  tooltip?: string;
}

export interface TileDataWithDrillDown {
  status?: string;
  data?: { count?: number; [key: string]: any };
  error?: string;
  drillDown?: DrillDownInfo;
  [key: string]: any;
}
```

`PackingStatsTile`'s existing `PackingStatsData` interface (`ordersBeingPackedCount`, `ordersBeingProcessedCount`, `totalOrdersPackedToday`, `packedByPacker`, etc.) is unaffected and stays as-is; only the outer `data.drillDown` typing and the `targetUrl` prop change.

## API / Interface Design
No backend/API contract change. Frontend-only interface change:

- `PackingStatsTileProps` — remove custom `drillDown` field from the `data` shape; `data` becomes/extends `TileDataWithDrillDown`; add `targetUrl?: string` top-level prop.
- `tileRegistry.tsx`'s `packingstats` renderer — pass `targetUrl="/baleni"`.

## Dependencies
- `frontend/src/utils/urlUtils.ts` — `createFilteredUrl`, `isTileClickable`, `getTileTooltip`, `TileDataWithDrillDown` (all already exist, no changes needed).
- `frontend/src/components/dashboard/tiles/tileRegistry.tsx` — single edit site for the `targetUrl`.
- No changes to `Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs` (backend) — already compatible.

## Out of Scope
- Any change to the backend `PackingStatsTile.cs` payload shape.
- Migrating `PackingStatsTile` to the `resolveDrillDown()` / `routeKey` pattern (pattern 1) — the issue explicitly directs adoption of the `CountTile`/pattern-2 convention instead, since the backend already emits that shape.
- Adding real (non-empty) `filters` to the backend payload — out of scope; this spec only ensures the frontend would honor them if/when the backend adds them.
- Any visual/UX redesign of the tile's stat grid or packer list.
- Writing net-new automated tests beyond what's needed to cover the changed drill-down logic (see design/plan for exact test scope — no dedicated `PackingStatsTile.test.tsx` currently exists in the repo).

## Open Questions
None.

## Status: COMPLETE
