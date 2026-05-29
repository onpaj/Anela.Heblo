I have enough context. The current frontend tiles already hardcode their own navigation (the backend's `drillDown` is dead data for these three), and the Hangfire URL is fundamentally different from the DataQuality SPA route — it's a backend-hosted dashboard opened externally via `window.open(\`${apiUrl}${path}\`)`. The spec's proposed registry shape papers over this distinction; the architecture review needs to fix it.

# Architecture Review: Remove Hardcoded Frontend Routes from Dashboard Tiles

## Skip Design: true

Pure refactor of an internal payload contract. No new tiles, no visual or layout changes, no UX flows altered.

## Architectural Fit Assessment

The feature is a textbook application of the **frontend owns routing** principle already documented in `docs/architecture/development_guidelines.md` (the "Frontend-Backend Separation" section and the "Backend constructing frontend URLs" forbidden-practice row). It removes an existing violation rather than introducing a new pattern.

**Integration points are minimal and local:**
- Three backend tile classes in `Anela.Heblo.Application/Features/{DataQuality,BackgroundJobs}/DashboardTiles/` — anonymous-object literals change shape; no contracts, no MediatR handlers, no DI changes.
- Three frontend tile components in `frontend/src/components/dashboard/tiles/` — `DataQualityTile.tsx`, `DqtYesterdayStatusTile.tsx`, `FailedJobsTile.tsx`. Each currently hardcodes its own destination path in its click handler; the registry replaces those literals.
- The backend `drillDown` field is currently **dead data on the FE** for all three tiles (the components ignore it). The fix wires the field through for real for the first time.

**Two material gaps in the spec must be corrected before implementation:**

1. **The spec puts the registry under `frontend/src/features/dashboard/`. That path does not exist for the dashboard module.** Dashboard code lives under `frontend/src/components/dashboard/`. The `features/` directory holds unrelated modules (articles, changelog, feature-flags, grid-layout, leaflet-generator). Place the registry where the consumers live.

2. **The spec treats `/hangfire/jobs/failed` as a SPA route. It is not.** `App.tsx` defines no `/hangfire` route. The current `FailedJobsTile.tsx` opens it via `window.open(\`${apiUrl}${HANGFIRE_PATH}\`, '_blank')` because the Hangfire dashboard is served by the **backend** at the API origin (different port in dev: 5001 vs the SPA's 3001). A `Record<string, string>` registry resolved through `react-router`'s `navigate()` would break this — clicking the tile would 404 inside the SPA. The registry must encode the **kind** of destination, not just a string.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────┐         ┌────────────────────────────────────┐
│ Backend tile (LoadDataAsync)        │  HTTP   │ Frontend tile component            │
│ DataQualityStatusTile / Dqt… /      │ ──────▶ │ DataQualityTile.tsx /              │
│ FailedJobsTile                      │  JSON   │ DqtYesterdayStatusTile.tsx /       │
│                                     │         │ FailedJobsTile.tsx                 │
│ returns: { drillDown:               │         │                                    │
│   { routeKey: "...", enabled: bool }│         │ click handler calls                │
│ }                                   │         │   resolveDrillDownTarget(key)      │
└─────────────────────────────────────┘         └──────────────┬─────────────────────┘
                                                                │
                                                                ▼
                                                ┌─────────────────────────────────────┐
                                                │ drillDownRoutes.ts (registry)       │
                                                │                                     │
                                                │ { dataQuality:                      │
                                                │     { kind: 'spa',  path: ... },    │
                                                │   hangfireFailedJobs:               │
                                                │     { kind: 'external', path: ... } │
                                                │ }                                   │
                                                │                                     │
                                                │ resolveDrillDownTarget(key)         │
                                                │   → { kind, path } | null           │
                                                └─────────────────────────────────────┘
                                                                │
                                          ┌─────────────────────┴───────────────────┐
                                          ▼                                         ▼
                              react-router navigate(path)            window.open(`${apiUrl}${path}`, '_blank')
                              (when kind === 'spa')                  (when kind === 'external')
```

### Key Design Decisions

#### Decision 1: Registry entries encode destination kind, not just a path string

**Options considered:**
- **A.** `Record<string, string>` (spec's proposal): one path per key.
- **B.** `Record<string, { kind: 'spa' | 'external'; path: string }>` with a resolver that returns the typed descriptor.
- **C.** Two separate maps (`spaRoutes`, `externalPaths`) keyed independently.

**Chosen approach:** **B**.

**Rationale:** `/hangfire/jobs/failed` is served by the backend at the API origin and must be opened with `window.open(\`${apiUrl}${path}\`, '_blank')`, while `/automation/data-quality` is a SPA route that must be entered via `useNavigate()` — they cannot share a single open-the-link mechanism. Option A pretends they are the same, breaking the FailedJobs tile (it would try to navigate the SPA to `/hangfire/jobs/failed`, which has no `<Route>` and would render the 404 page). Option C bifurcates the registry and forces callers to know which map to consult, defeating the point of a semantic key. Option B is the smallest deviation from the spec that preserves correctness and keeps a single registry as the single source of truth.

#### Decision 2: Tile components keep their own click handlers; no generic renderer change

**Options considered:**
- **A.** Add a generic drill-down click handler to `DashboardTile.tsx` driven by `tile.drillDown.routeKey`.
- **B.** Keep per-tile click handlers in each tile component; they call `resolveDrillDownTarget(tile.drillDown.routeKey)` to obtain the path.

**Chosen approach:** **B**.

**Rationale:** A generic handler in `DashboardTile.tsx` is a larger refactor than this task scope: each tile currently makes its own decisions about which DOM element is clickable, whether to show error states without a click target, whether to wrap the whole tile or just a content region, and (for FailedJobs) whether to open externally. Forcing a single click owner would either drop those per-tile behaviors or grow a bunch of new props to preserve them. Keeping click handlers in the leaf components is consistent with the existing pattern across the rest of `frontend/src/components/dashboard/tiles/` and is the smallest surface area that satisfies FR-1 through FR-4.

#### Decision 3: No backend constants for `routeKey` values

**Options considered:**
- **A.** Define `RouteKeys.DataQuality = "dataQuality"` etc. in a shared backend location.
- **B.** Inline the string literals in each tile, with one matching constant in the FE registry.

**Chosen approach:** **B**.

**Rationale:** A backend constant introduces a new shared symbol whose only purpose is to spell a string the frontend will look up — that's coupling-by-naming with no compiler enforcement (the FE never imports the C# constant). The only correctness invariant is that the literal in the C# tile matches a key in the TS registry; this is verified by a single test on each side (FR-4 already covers the FE; FR-5 covers the BE). Don't invent ceremony for a two-line contract.

#### Decision 4: Unknown `routeKey` disables the drill-down silently to the user, loudly to the developer

**Options considered:**
- **A.** Throw / render nothing.
- **B.** Disable the click target and log a `console.warn` (spec FR-4).
- **C.** Fall back to a default path.

**Chosen approach:** **B**, as specified.

**Rationale:** This is the right failure mode for a contract drift between BE and FE deployed in the same Docker image — a user shouldn't see a broken page, a developer shouldn't see silence. Option C masks the bug.

## Implementation Guidance

### Directory / Module Structure

**Backend** — no new files; edit only:
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DataQualityStatusTile.cs` (lines 36, 58, 67 — three sites of `drillDown = new { href = "/data-quality", ... }`).
- `backend/src/Anela.Heblo.Application/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTile.cs` (line 9 constant removal; lines 53, 78, 93 — three sites).
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/DashboardTiles/FailedJobsTile.cs` (line 9 constant removal; lines 44, 61 — two sites).

**Frontend** — new file plus three edits:
- **NEW** `frontend/src/components/dashboard/drillDownRoutes.ts` — the registry and `resolveDrillDownTarget`. Co-located with consumers, not under `features/`.
- **NEW** `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.ts` — unit test asserting both keys resolve to the expected `{ kind, path }`.
- Edit `frontend/src/components/dashboard/tiles/DataQualityTile.tsx` — replace `navigate('/automation/data-quality')` with the registry call.
- Edit `frontend/src/components/dashboard/tiles/DqtYesterdayStatusTile.tsx` — same; also update the inline `drillDown` interface from `{ href: string; enabled: boolean }` to `{ routeKey: string; enabled: boolean }`.
- Edit `frontend/src/components/dashboard/tiles/FailedJobsTile.tsx` — remove `HANGFIRE_PATH` literal; read path from registry; keep `window.open(\`${apiUrl}${path}\`, '_blank')` behavior gated on `kind === 'external'`.

**Backend tests** — edit:
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/DashboardTiles/DqtYesterdayStatusTileTests.cs` (lines 44–45, 134–135 — assertions on `drillDown.href`).
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/DashboardTiles/FailedJobsTileTests.cs` (lines 35–37, 63–65 — assertions on `drillDown.url`).
- If a `DataQualityStatusTileTests.cs` does not yet exist, the spec does not require adding one — only existing tests need to be updated.

**Frontend tests** — edit:
- `frontend/src/components/dashboard/tiles/__tests__/FailedJobsTile.test.tsx` (line 48 — keeps the same expected URL, but the source of `'/hangfire/jobs/failed'` becomes the registry; if the registry mock is needed, mock the module).
- `frontend/src/components/dashboard/tiles/__tests__/DqtYesterdayStatusTile.test.tsx` (line 122 — same `mockNavigate.toHaveBeenCalledWith('/automation/data-quality')` assertion holds; just confirm the resolved path matches).

### Interfaces and Contracts

**Backend payload (per tile, anonymous object — unchanged shape elsewhere):**

```csharp
drillDown = new { routeKey = "dataQuality",        enabled = true } // DataQualityStatusTile, DqtYesterdayStatusTile
drillDown = new { routeKey = "hangfireFailedJobs", enabled = true } // FailedJobsTile
```

Note: `FailedJobsTile` currently also emits `tooltip = "Open Hangfire failed jobs"` in the drill-down object. The spec doesn't mention it; preserve it as-is (it's a UI hint, not a routing concern) — only `url` → `routeKey` changes.

**Frontend registry (`drillDownRoutes.ts`):**

```ts
export type DrillDownKind = 'spa' | 'external';
export interface DrillDownTarget { kind: DrillDownKind; path: string; }

export const drillDownRoutes: Record<string, DrillDownTarget> = {
  dataQuality:        { kind: 'spa',      path: '/automation/data-quality' },
  hangfireFailedJobs: { kind: 'external', path: '/hangfire/jobs/failed' },
};

export function resolveDrillDownTarget(routeKey: string | undefined): DrillDownTarget | null {
  if (!routeKey) return null;
  const target = drillDownRoutes[routeKey];
  if (!target) {
    // eslint-disable-next-line no-console
    console.warn(`[dashboard] Unknown drill-down routeKey: ${routeKey}`);
    return null;
  }
  return target;
}
```

**Tile-side usage (sketch, applied to all three tiles):**

```ts
const target = resolveDrillDownTarget(data.drillDown?.routeKey);
const handleClick = () => {
  if (!target || data.drillDown?.enabled === false) return;
  if (target.kind === 'spa')       navigate(target.path);
  else /* external */              window.open(`${getConfig().apiUrl}${target.path}`, '_blank');
};
// render the wrapper as non-clickable when target is null
```

The `DashboardTile` type in `useDashboard.ts` currently has `data?: any`, so no OpenAPI regeneration is required (NFR-4 confirms backend and frontend ship in lockstep). The per-tile inline `drillDown` interface in `DqtYesterdayStatusTile.tsx` should be updated to the new shape; the others access `data.drillDown` loosely already.

### Data Flow

1. Browser polls `/api/dashboard/data` (every 30s, see `useTileData` in `useDashboard.ts`).
2. Backend executes each registered tile's `LoadDataAsync`, returning anonymous objects with `drillDown: { routeKey, enabled }`.
3. React Query stores the response; `TileContent` dispatches by `tile.tileId` to the leaf component.
4. Leaf component renders normally; on click, it calls `resolveDrillDownTarget(tile.data.drillDown.routeKey)`.
5. Resolver hits the in-memory map; `kind === 'spa'` → `useNavigate()` push, `kind === 'external'` → `window.open(apiUrl + path, '_blank')`.
6. Unknown key or `enabled === false` → click is a no-op and (for unknown key) a `console.warn` fires.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Treating Hangfire URL as a SPA route would render a 404 inside the SPA when clicked. | High | Decision 1: encode `kind: 'spa' \| 'external'`. The FailedJobs tile keeps its `window.open(apiUrl + path, '_blank')` behavior gated on `kind === 'external'`. |
| Backend deploy lands before frontend deploy (key not yet in registry), or vice-versa, breaks clicks. | Low | NFR-4: BE and FE ship in one Docker image. Registry warns + disables click on unknown key, so the failure is visible in console and never breaks the page. |
| The `drillDown` type in `DqtYesterdayStatusTile.tsx` is currently `{ href: string; enabled: boolean }` and asserted in `DqtYesterdayStatusTileTests` (BE) and FE tests. Forgetting to update both sides leaves a drift. | Medium | Implementation checklist (below) lists every assertion site; arch-review regex from FR-1 catches any stray `"/automation/data-quality"` / `"/hangfire/..."` / `"/data-quality"` string literal in `backend/src/Anela.Heblo.Application/Features/**/DashboardTiles/**`. |
| A future tile author re-introduces a path string in the backend. | Low (recurring) | FR-1's regex acceptance criterion stays useful as a single ad-hoc grep at PR time; add a one-line test in `ModuleBoundariesTests.cs` (already a reflection-based architectural test) that scans every `ITile` implementation's compiled assembly for the regex if the team wants automated enforcement. Optional — out of scope unless requested. |
| `DataQualityStatusTile` has no existing unit test file (only `DqtYesterdayStatusTileTests.cs` was found). Removing `/data-quality` is therefore untested at the BE layer. | Low | Spec FR-5 only requires updating existing tests. The frontend test for `DataQualityTile.tsx` covers the user-visible behavior; the BE change is a one-token edit. No new test required. |

## Specification Amendments

1. **FR-4, registry location:** change `frontend/src/features/dashboard/drillDownRoutes.ts` → `frontend/src/components/dashboard/drillDownRoutes.ts`. The `features/` path under `frontend/src/` exists for unrelated modules; the dashboard module lives under `components/dashboard/`. The unit-test path becomes `frontend/src/components/dashboard/__tests__/drillDownRoutes.test.ts` (co-located `__tests__` is the convention used everywhere else in this tree).

2. **FR-4, registry shape:** change `Record<string, string>` → `Record<string, { kind: 'spa' | 'external'; path: string }>`. The exported helper becomes `resolveDrillDownTarget(routeKey): { kind, path } | null`. The Hangfire entry is `external`; the DataQuality entry is `spa`. Without this, the FailedJobs tile cannot preserve its current `window.open(apiUrl + path, '_blank')` behavior — the SPA has no `/hangfire` route.

3. **FR-1, FailedJobs payload:** preserve the existing `tooltip` field in the `FailedJobsTile` drill-down object. The spec only addresses `url` → `routeKey`; the `tooltip` is a presentational hint and should remain. Acceptance criterion: `FailedJobsTile.LoadDataAsync` returns `drillDown = new { routeKey = "hangfireFailedJobs", enabled = true, tooltip = "Open Hangfire failed jobs" }`.

4. **FR-1, line-number drift:** the spec cites `DataQualityStatusTile.cs` lines 37 and 50; the actual sites are lines 36, 58, and 67 (three occurrences across the three return statements). The number-of-sites count, not the line numbers, is what matters — make sure all three are replaced.

5. **FR-3, "more recent" rationale:** strike the phrase "the path used by the more recent `DqtYesterdayStatusTile`, which matches the current `/automation/` section of the SPA." Replace with: "the path defined by the SPA `Route path="/automation/data-quality"` in `App.tsx`, which is the only existing route for the Data Quality page; `/data-quality` is currently a dead link." That's the real reason `/automation/data-quality` is canonical.

6. **FR-5, scope correction:** the spec says "All unit/integration tests under `backend/test/**` that reference `DrillDownHref`, `FailedJobsUrl`, `"/data-quality"`, `"/automation/data-quality"`, or `"/hangfire/jobs/failed"`…". The audit found exactly two such files: `DqtYesterdayStatusTileTests.cs` and `FailedJobsTileTests.cs`. There is no `DataQualityStatusTileTests.cs`. Either acknowledge no test exists for that tile, or add a minimal one — recommendation: do not add one (out of scope; spec said update existing).

7. **Add FR-7, frontend test updates:** the spec covers the BE test updates (FR-5) and the new FE registry test (FR-4) but is silent on the existing FE tile tests. `FailedJobsTile.test.tsx` (line 48 asserts the resolved URL) and `DqtYesterdayStatusTile.test.tsx` (line 122 asserts the navigate target) must continue to pass — they should still assert the same final URL/path; the source of truth simply moves to the registry. If the tests need to mock the registry to keep them unit-scoped, do so; otherwise let them go through the real registry (it's a pure module, no I/O).

## Prerequisites

None. No migrations, no infrastructure, no config, no Key Vault entries, no DI registration, no new packages. Backend and frontend deploy together in one Docker image (per NFR-4) so there is no rollout-ordering concern. Implementation can begin immediately.