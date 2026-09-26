### task: wire-target-url-in-tile-registry

**Files:**
- Modify: `frontend/src/components/dashboard/tiles/tileRegistry.tsx:150` (the `packingstats` entry)

- [ ] **Step 1: Update the `packingstats` entry**

In `frontend/src/components/dashboard/tiles/tileRegistry.tsx`, find the line:

```tsx
  packingstats: ({ data }) => <PackingStatsTile data={data} />,
```

Replace it with:

```tsx
  packingstats: ({ data }) => <PackingStatsTile data={data} targetUrl="/baleni" />,
```

This is the only change in this file.

- [ ] **Step 2: Re-run the `PackingStatsTile` test suite**

Run: `cd frontend && CI=true npx react-scripts test src/components/dashboard/tiles/__tests__/PackingStatsTile.test.tsx --watchAll=false`

Expected: PASS (unaffected by this file — the test suite passes `targetUrl` directly as a prop and does not import `tileRegistry.tsx`; this step guards against an unrelated regression having crept in).

- [ ] **Step 3: Type-check and lint the frontend**

Run: `cd frontend && npm run build`
Expected: build succeeds with no new TypeScript errors (confirms `PackingStatsTileProps`'s new shape is compatible with how `tileRegistry.tsx` now calls it, and that `TileDataWithDrillDown`'s `data` prop is structurally satisfied by whatever `DashboardTileType`'s `data: any` passes through).

Run: `cd frontend && npm run lint`
Expected: no new lint errors introduced by either file.

- [ ] **Step 4: Run the full frontend test suite**

Run: `cd frontend && CI=true npm test -- --watchAll=false`
Expected: PASS — no existing test (in particular no dashboard/tile snapshot or integration test) regresses from the `targetUrl` addition.

- [ ] **Step 5: Commit**

```bash
cd frontend
git add src/components/dashboard/tiles/tileRegistry.tsx
git commit -m "feat(dashboard): pass targetUrl=/baleni into PackingStatsTile via tileRegistry"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (shared `TileDataWithDrillDown` typing + helpers, no hardcoded `navigate('/baleni')`, no custom `drillDown` field) → covered by `refactor-packing-stats-tile-drilldown` Step 3.
- FR-2 (`targetUrl?: string` prop, graceful no-op when absent) → covered by `refactor-packing-stats-tile-drilldown` Step 3 (prop declaration) and Step 1/4 tests ("does not navigate when targetUrl is not supplied").
- FR-3 (`tileRegistry.tsx` passes `targetUrl="/baleni"`) → covered by `wire-target-url-in-tile-registry` Step 1.
- FR-4 (no visible behavior change; empty `filters` yields no query string; non-enabled stays non-clickable) → covered by the "navigates to targetUrl with no query string...", "is not clickable when drillDown is absent", and "is not clickable when drillDown.enabled is false" tests in `refactor-packing-stats-tile-drilldown` Step 1.
- NFR-1 (structural consistency with `InventorySummaryTile`) → the Step 3 implementation uses the identical `isTileClickable`/`getTileTooltip`/`handleClick` shape as `InventorySummaryTile.tsx`.
- NFR-2 (no backend changes) → no backend file appears in either task's Files list.

**2. Placeholder scan:** No TBD/TODO/"add appropriate handling" phrases; every step shows complete, runnable code or an exact command with its expected output.

**3. Type consistency:** `PackingStatsTileProps`, `PackingStatsData`, and `PackerStat` are defined once (Step 3 of the first task) and used as-is by the test file (Step 1 of the same task) and by `tileRegistry.tsx` (second task, which only passes props — it does not reference the interface names directly, so there is no drift risk).

No gaps found; no additional tasks needed.
