# Architecture Review: Dashboard Tile Registry (replace `TileContent.tsx` switch)

## Skip Design: true

Pure internal refactor of an existing rendering dispatch mechanism. No new UI, no visual change, no new component appears on screen that isn't already there today — every tile renders exactly as before, byte-for-byte. No design document, screen, or layout decision is implicated.

## Architectural Fit Assessment

This aligns cleanly with existing conventions and touches a well-isolated seam.

- `frontend/src/components/dashboard/tiles/` already follows a flat, one-component-per-file layout (`CountTile.tsx`, `InventorySummaryTile.tsx`, `ProductionTile.tsx`, etc.), with `TileContent.tsx` acting as the sole dispatcher and `index.ts` as a partial public-export barrel (it does **not** currently export every tile component — only `BackgroundTasksTile`, `ProductionTile`, `DefaultTile`, plus `TileHeader`/`TileContent`/`LoadingTile`). A new `tileRegistry.tsx` file fits this directory's existing "flat sibling module" convention; it does not require a new subdirectory or layering concept.
- Tests already live in `frontend/src/components/dashboard/tiles/__tests__/TileContent.test.tsx` and mock leaf tile components **by their own module path** (e.g. `jest.mock('../BackgroundTasksTile', ...)`), not by mocking `TileContent.tsx` itself or any switch internals. This is the key architectural fact that de-risks the refactor: Jest's module registry mocks are resolved per-module-path, independent of which file imports them. Moving the `import { BackgroundTasksTile } from './BackgroundTasksTile'` line from `TileContent.tsx` into `tileRegistry.tsx` does not change the resolved module path (`.../dashboard/tiles/BackgroundTasksTile`), so the existing mocks keep intercepting correctly. Verified by reading the test file directly — confirms the spec's NFR/dependency claim is correct, not just asserted.
- No backend, API, or data-model involvement — `DashboardTile.tileId` (`frontend/src/api/hooks/useDashboard.ts`) is unchanged, and this is purely a frontend presentation-dispatch concern.
- The `docs/architecture/development_guidelines.md` ADR log shows a precedent for "systemic recurring fix" reasoning (e.g. ADR-006 dark mode), but nothing there governs component dispatch patterns specifically — no conflicting convention exists that this refactor would violate.

## Proposed Architecture

### Component Overview

```
tile.tileId (string, backend-supplied)
        │
        ▼
TileContent.tsx  ──uses──▶  tileRegistry.tsx
   (guards: isUnauthorized,        │
    !data → Loading)               │  exports TILE_RENDERERS: Record<string, TileRenderer>
        │                          │  (module-scope object, one entry per tileId,
        ▼                          │   each value a small FC closing over static
  lookup TILE_RENDERERS[tileId]    │   props: icon, iconColor, targetUrl, etc.)
        │                          ▼
        └────────────────▶  BackgroundTasksTile, ProductionTile, CountTile,
                             InventorySummaryTile, ConditionsTile, ...
                             (existing leaf components — UNCHANGED)
```

`TileContent.tsx` becomes a thin guard + lookup + fallback; `tileRegistry.tsx` owns the mapping and all per-tile static configuration (icons, colors, target URLs, title fallbacks). Leaf tile components are untouched.

### Key Design Decisions

#### Decision 1: Registry file format — object map vs. discriminated-union dispatcher vs. component-per-tileId convention

**Options considered:**
1. Static `Record<string, TileRenderer>` object map (as specified).
2. A discriminated union + exhaustive switch inside a generic `resolveTileComponent()` helper (moves the switch, doesn't remove it).
3. Convention-over-configuration: derive the component from `tileId` via dynamic `require`/lazy import (e.g. `./tiles/${tileId}`).

**Chosen approach:** Option 1 (static object map), exactly as the spec proposes.

**Rationale:** Option 2 doesn't solve the actual problem — it relocates the switch rather than eliminating the "must touch this file" property. Option 3 introduces dynamic module resolution, which breaks static analysis/tree-shaking, defeats TypeScript's ability to catch a typo in `tileId` at the call site, and is a much bigger behavioral change (async loading, bundler config) than this ticket's scope ("pure refactor, no behavior change"). The object map is the smallest change that satisfies Open/Closed: new tiles are additive (`TILE_RENDERERS['newid'] = ...`), existing code paths are untouched, and the type system still enforces the `TileRenderer` shape on every entry.

#### Decision 2: Where renderer functions are defined (module scope vs. inline in the map vs. per-tile factory)

**Options considered:**
1. Inline arrow functions directly as object values in `TILE_RENDERERS`, defined at module scope (spec's example).
2. A factory helper `countTile(icon, color, url)` that returns a renderer, to cut down repetition across the 8 `CountTile`-backed entries.
3. Named local functions in `tileRegistry.tsx` referenced in the map, rather than inline arrows.

**Chosen approach:** Option 1 for tiles with unique props (e.g. `ConditionsTile`, `WeatherForecastTile`), Option 2 (a small local factory) for the repeated `CountTile` pattern to avoid restating `data-icon-color`/`tileCategory`/`tileTitle`/`targetUrl` boilerplate 8 times — but only if it doesn't obscure the 1:1 traceability to the original switch. This is an implementation-level call, not a hard requirement; keep it if it measurably shortens the file without hiding any tile's specific icon/color/URL from a reader scanning the map.

**Rationale:** The NFR explicitly requires renderers be defined at module scope, not recreated per-render, to avoid React reconciliation churn from unstable component identities. A factory function called once at module load time to produce a stable component reference satisfies this equally well as an inline arrow — both are created exactly once, at import time, not per-render. Developers should default to inline arrows (matches the spec's example, simplest to review) and only introduce a factory if repetition genuinely hurts readability; do not over-engineer this into a generic tile-config DSL — that's explicitly out of scope per the spec.

#### Decision 3: `.tsx` vs `.ts` for the registry file

**Chosen approach:** `.tsx`, as the spec states. The directory's existing convention is one component per `.tsx` file; the registry contains JSX (`<CountTile icon={<Truck .../>} .../>`), so `.tsx` is required for the JSX icon elements regardless of style preference.

## Implementation Guidance

### Directory / Module Structure

No new directories. Add one sibling file:

```
frontend/src/components/dashboard/tiles/
├── tileRegistry.tsx        # NEW — TILE_RENDERERS map + TileRenderer type
├── TileContent.tsx         # MODIFIED — guards + lookup only, no switch
├── CountTile.tsx           # unchanged
├── InventorySummaryTile.tsx# unchanged
├── ... (all other leaf tiles unchanged)
├── index.ts                # unchanged (spec says leave as-is; confirmed: it
│                            #   does not currently export CountTile,
│                            #   InventorySummaryTile, etc. individually, so
│                            #   there is nothing to update for consistency)
└── __tests__/
    └── TileContent.test.tsx  # unchanged — must pass with zero edits
```

Do not create a `tiles/registry/` subdirectory or split the map across multiple files — 23 entries in one file is consistent with the current single-file switch's size and keeps the "one place to look" property that motivated this refactor in the first place.

### Interfaces and Contracts

```tsx
// tileRegistry.tsx
import type { DashboardTile as DashboardTileType } from '../../../api/hooks/useDashboard';

export type TileRenderer = React.FC<{ data: any; tile: DashboardTileType }>;

export const TILE_RENDERERS: Record<string, TileRenderer> = {
  backgroundtaskstatus: ({ data }) => <BackgroundTasksTile data={data} />,
  todayproduction: ({ data, tile }) => <ProductionTile data={data} title={tile.title || 'Dnes'} />,
  nextdayproduction: ({ data, tile }) => <ProductionTile data={data} title={tile.title || 'Zítra'} />,
  manufactureconditions: ({ data }) => <ConditionsTile data={data} />,
  manualactionrequired: ({ data, tile }) => (
    <ManualActionRequiredTile data={data} tileCategory={tile.category} tileTitle={tile.title} />
  ),
  purchaseordersintransit: ({ data, tile }) => (
    <PurchaseOrdersInTransitTile data={data} tileCategory={tile.category} tileTitle={tile.title} />
  ),
  intransitboxes: ({ data, tile }) => (
    <CountTile data={data} icon={<Truck className="h-10 w-10" />} iconColor="text-blue-600"
      tileCategory={tile.category} tileTitle={tile.title} targetUrl="/logistics/transport-boxes" />
  ),
  // ... remaining 17 entries, one per tileId, verbatim props from current switch
  lowstockalert: ({ data }) => <LowStockAlertTile data={data} />,
  dataqualitystatus: ({ data }) => <DataQualityTile data={data} />,
  dqtyesterdaystatus: ({ data }) => <DqtYesterdayStatusTile data={data} />,
  weatherforecast: ({ data }) => <WeatherForecastTile data={data} />,
  failedjobs: ({ data }) => <FailedJobsTile data={data} />,
  packingstats: ({ data }) => <PackingStatsTile data={data} />,
};
```

```tsx
// TileContent.tsx (post-refactor, full replacement of lines 34-94 in current file)
import { TILE_RENDERERS } from './tileRegistry';
import { DefaultTile } from './DefaultTile';
import { LoadingTile } from './LoadingTile';
import { UnauthorizedTile } from './UnauthorizedTile';

export const TileContent: React.FC<TileContentProps> = ({ tile }) => {
  if (tile.isUnauthorized) return <UnauthorizedTile />;
  if (!tile.data) return <LoadingTile />;

  const Renderer = TILE_RENDERERS[tile.tileId];
  return Renderer ? <Renderer data={tile.data} tile={tile} /> : <DefaultTile data={tile.data} />;
};
```

**Contract rule for future tile additions:** a new tile requires exactly one new key in `TILE_RENDERERS`, matching the backend `tileId` string exactly (case-sensitive, as today — no normalization is introduced). `TileContent.tsx` must never gain a new import of a leaf tile component; if a PR touches `TileContent.tsx` beyond the guard clauses, that is a signal the registry pattern is being bypassed.

### Data Flow

Unchanged end-to-end. `useDashboard` hook fetches tiles + data from the backend → `DashboardTile[]` passed down to per-tile `TileContent` instances → `TileContent` now performs an object-key lookup instead of a switch to pick the renderer → renderer passes `data`/`tile` fields through to the same leaf components with the same static props as before. No new render, no new fetch, no new state.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Transcription error moving 23 cases (wrong icon color, wrong `targetUrl`, dropped `tileCategory`/`tileTitle` prop) | Medium | `TileContent.test.tsx` already asserts icon colors, default titles, and props per tile and must pass unmodified — treat any test failure as a transcription bug, not a test to "fix." Do a side-by-side diff of old switch vs. new map before removing the switch. |
| Renderer functions accidentally defined inside `TileContent`'s function body (recreated every render) instead of at module scope in `tileRegistry.tsx` | Low | Enforced by file boundary: `TILE_RENDERERS` only exists in `tileRegistry.tsx`, which has no access to `TileContent`'s render scope — structurally hard to get wrong once the registry lives in its own file. |
| Jest `jest.mock('../BackgroundTasksTile', ...)` calls in the test file stop intercepting because import moved to a different file | Low | Verified: Jest mocks resolve by module path, not by importer. Confirmed by reading `TileContent.test.tsx` — mocks target `../ComponentName` relative to the test file, which resolves to the same absolute module path regardless of whether `TileContent.tsx` or `tileRegistry.tsx` holds the `import` statement. No action needed beyond keeping the same relative paths (`./BackgroundTasksTile` etc.) in the new file. |
| Silent tileId typos still fall through to `DefaultTile` with no build-time signal (pre-existing gap, not introduced by this change) | Low (accepted, explicitly out of scope) | Spec correctly scopes a backend/frontend tileId-catalogue consistency check as a follow-up, not part of this ticket. Do not attempt it here. |

## Specification Amendments

None required — the spec is implementation-ready and its architectural claims (module-path-based Jest mock resolution, module-scope renderer requirement, `.tsx` file choice) were independently verified against the actual test file and directory contents during this review and hold up. One small clarification worth adding to the spec, non-blocking:

- The spec should explicitly state that developers may factor the 8 repeated `CountTile` entries through a small local helper function (see Decision 2) if it improves readability, provided the helper is called at module scope (not per-render) and each call site remains individually traceable to one `tileId` — this is a style latitude, not a required change, so it doesn't need a spec revision, just a note for the implementer.

## Prerequisites

None. No migrations, no config, no infrastructure changes. The change is self-contained to `frontend/src/components/dashboard/tiles/` and can start immediately; the only "setup" step is reading the current `TileContent.tsx` switch (lines 34-94) side by side while authoring `tileRegistry.tsx` to guarantee a 1:1, zero-drift transcription of all 23 cases.
