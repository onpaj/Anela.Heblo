# Architecture Review: PackingStatsTile drill-down consistency fix

## Skip Design: true

## Architectural Fit Assessment
This is a small, surgical conformance fix, not new architecture. The target pattern already exists and is used by three other tiles (`CountTile`, `InventorySummaryTile`, `MaterialExpirationSummaryTile`), all consuming the same `frontend/src/utils/urlUtils.ts` helpers (`createFilteredUrl`, `isTileClickable`, `getTileTooltip`, `TileDataWithDrillDown`). `PackingStatsTile` is the only tile with backend-emitted `filters`-shape `drillDown` that does not go through these helpers. The fix brings it into that existing group; it introduces no new interfaces, no new backend contract, and no new routing concept.

I verified in the repo:
- `frontend/src/utils/urlUtils.ts` already exports exactly the helpers and types the issue names — no gaps to fill.
- `frontend/src/components/dashboard/tiles/InventorySummaryTile.tsx` is a near-exact structural template for what `PackingStatsTile` should become: a multi-field, non-`CountTile`-literal tile that takes `data: TileDataWithDrillDown & {...}` and an optional `targetUrl?: string` prop, and gates its `onClick` with `isClickable && targetUrl && data.drillDown?.filters`.
- `frontend/src/components/dashboard/tiles/tileRegistry.tsx` already has a `packingstats: ({ data }) => <PackingStatsTile data={data} />` entry (the tile is registered, just missing `targetUrl`).
- Backend `Anela.Heblo.Application/Features/Packaging/DashboardTiles/PackingStatsTile.cs` (lines 87–92) already emits `drillDown: { filters: {}, enabled: true, tooltip: "..." }` — the exact shape `TileDataWithDrillDown`/`DrillDownInfo` expects. No backend change needed or in scope.
- `docs/architecture/development_guidelines.md` line 60 states the guideline the issue cites: "Frontend owns routing: URL construction happens in frontend based on its routing structure" — confirms the project-level rationale for keeping `/baleni` in `tileRegistry.tsx`, not inside the tile component.

No conflicting pattern, no migration risk, no data model change. This is as low-risk as a refactor gets.

## Proposed Architecture

### Component Overview
```
tileRegistry.tsx
  packingstats: ({ data }) => <PackingStatsTile data={data} targetUrl="/baleni" />
                                        │
                                        ▼
                          PackingStatsTile.tsx
                 (data: TileDataWithDrillDown & {...}, targetUrl?: string)
                                        │
                    isTileClickable(data) / getTileTooltip(data)
                    createFilteredUrl(targetUrl, data.drillDown.filters)
                                        │
                                        ▼
                              react-router navigate()
```
No new components, no new files. Two files change: `PackingStatsTile.tsx` (logic + prop types) and `tileRegistry.tsx` (one-line prop addition).

### Key Design Decisions

#### Decision 1: Follow `InventorySummaryTile`, not `CountTile`, as the literal template
**Options considered:**
- (a) Have `tileRegistry.tsx` render `<CountTile ... />` directly for `packingstats`, discarding the custom multi-stat/packer-list markup.
- (b) Keep `PackingStatsTile` as its own component (custom markup for the 3-stat grid + packer list) but adopt the same `urlUtils` helpers + `targetUrl` prop convention that `CountTile` uses — the same approach `InventorySummaryTile` already took for its own multi-field, non-count tile.

**Chosen approach:** (b). The issue's suggested fix says "adopt the CountTile pattern" meaning the *drill-down protocol* (helpers + `targetUrl` + `filters`), not the literal `CountTile` component — `CountTile` renders a single number plus an icon and cannot represent `PackingStatsTile`'s 3-stat grid and per-packer breakdown. `InventorySummaryTile` is the existing precedent for exactly this situation (custom render, shared drill-down plumbing).

**Rationale:** Preserves the tile's actual UI (out of scope per the issue and spec FR-4 "no visible behavior change") while fully satisfying the architectural complaint (no hardcoded route, no custom `drillDown` shape). Reusing `InventorySummaryTile`'s established shape also means a reviewer already familiar with that file needs zero new context to review this one.

#### Decision 2: `targetUrl` stays an optional prop supplied by `tileRegistry.tsx`, not hardcoded or derived from a route-key registry
**Options considered:**
- (a) Migrate to the `resolveDrillDown()` / `DashboardTileDrillDown` / `DASHBOARD_DRILLDOWN_ROUTES` pattern (pattern 1, used by `DataQualityTile`/`FailedJobsTile`).
- (b) Adopt the `targetUrl` prop + `createFilteredUrl` pattern (pattern 2, used by `CountTile`/`InventorySummaryTile`).

**Chosen approach:** (b), per the issue's explicit direction. The backend already emits the pattern-2 shape (`filters`, not `routeKey`); switching to pattern 1 would require a backend change (emit `routeKey` instead of `filters`) that is explicitly out of scope, plus a new `DASHBOARD_DRILLDOWN_ROUTES` entry. Pattern 2 requires zero backend change.

**Rationale:** Minimizes blast radius — this is a pure frontend conformance fix. Adding a second, unnecessary pattern migration would inflate scope beyond what the issue asks for and beyond what the backend payload supports today.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Changes confined to:
- `frontend/src/components/dashboard/tiles/PackingStatsTile.tsx`
- `frontend/src/components/dashboard/tiles/tileRegistry.tsx`

If a test file is added (see Prerequisites/testing note below), it goes at the existing convention path: `frontend/src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx` (sibling to `DataQualityTile.test.tsx`, `FailedJobsTile.test.tsx`).

### Interfaces and Contracts

`PackingStatsTile.tsx` — before/after prop type:

```ts
// Before
interface PackingStatsTileProps {
  data: {
    status: string;
    error?: string;
    data?: PackingStatsData;
    drillDown?: { enabled: boolean; tooltip?: string };
  };
}

// After
interface PackingStatsTileProps {
  data: TileDataWithDrillDown & {
    status?: string;
    error?: string;
    data?: PackingStatsData;
  };
  targetUrl?: string;
}
```

Import `createFilteredUrl`, `isTileClickable`, `getTileTooltip`, `TileDataWithDrillDown` from `'../../../utils/urlUtils'` (same relative path `CountTile.tsx`/`InventorySummaryTile.tsx` already use from that directory).

Click handling — replace:
```ts
const isClickable = data.drillDown?.enabled ?? false;
// ...
onClick={isClickable ? () => navigate('/baleni') : undefined}
title={data.drillDown?.tooltip}
```
with:
```ts
const isClickable = isTileClickable(data);
const tooltip = getTileTooltip(data);
const handleClick = () => {
  if (isClickable && targetUrl && data.drillDown?.filters) {
    navigate(createFilteredUrl(targetUrl, data.drillDown.filters));
  }
};
// ...
onClick={isClickable ? handleClick : undefined}
title={tooltip}
```

`tileRegistry.tsx` — the `packingstats` entry changes from:
```ts
packingstats: ({ data }) => <PackingStatsTile data={data} />,
```
to:
```ts
packingstats: ({ data }) => <PackingStatsTile data={data} targetUrl="/baleni" />,
```

`PackingStatsData` (the inner `data.data` shape: `ordersBeingPackedCount`, `packedByPacker`, etc.) is untouched.

### Data Flow
Unchanged end-to-end shape: backend `PackingStatsTile.cs` → dashboard API → `useDashboard` hook → `tileRegistry.tsx` dispatch → `PackingStatsTile` component. The only change is *where* the `/baleni` string lives (moves from inside the component to the registry entry) and *how* clickability/tooltip/URL are computed (via shared helpers instead of ad-hoc inline logic). No new network calls, no new state, no new props beyond the single optional `targetUrl` string.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `isTileClickable` requires `Boolean(data.drillDown.filters)` to be truthy; backend sends `filters: {}` (empty object). `Boolean({})` is `true` in JS, so this is safe — but worth a regression test since it's the one non-obvious behavioral edge in this refactor. | Low | Add/verify a unit test asserting an empty `filters: {}` still yields `isClickable === true` and navigates to exactly `/baleni` (no query string), matching current behavior. |
| Forgetting to pass `targetUrl` in `tileRegistry.tsx` would silently make the tile permanently non-navigating (since the click guard requires `targetUrl` truthy) rather than crash. | Low | Explicit FR-3/FR-4 acceptance criteria cover this; a quick manual check or test asserting `navigate` is called with `/baleni` after the change catches a missed prop immediately. |
| None of the other tile diffs in this repo change; no shared utility (`urlUtils.ts`) needs modification, so zero risk of regressing `CountTile`/`InventorySummaryTile`/other consumers. | None | N/A — no shared file touched. |

## Specification Amendments
None. The spec (`spec.r1.md`) is implementable as written; no gaps found during architectural review.

## Prerequisites
None — no migrations, no config, no infrastructure changes. This can be implemented immediately.

Testing note (non-blocking, for the planner to size): there is currently no `PackingStatsTile.test.tsx` in the repo. Given `DataQualityTile.test.tsx` and `FailedJobsTile.test.tsx` exist as the sibling precedent for tiles with drill-down logic, the planning stage should decide whether to add a minimal `PackingStatsTile.test.tsx` covering: (1) non-clickable when `drillDown.enabled` is false/absent, (2) clickable + navigates to `/baleni` when `drillDown` is `{enabled: true, filters: {}}` and `targetUrl="/baleni"`, (3) tooltip renders from `getTileTooltip`. This is scoped as an implementation task, not a spec requirement, since the issue itself does not mandate new tests.
