I have enough context. The bug fires on `/` (Dashboard route), and the most likely candidates are in `Dashboard.tsx` and `DashboardSettings.tsx`. Now I'll produce the architecture review.

# Architecture Review: Fix frontend `r.filter is not a function` at `Yq`

## Skip Design: true

No UI/visual work. This is a defensive bug fix in client-side data consumption; rendered output stays identical when data is present and loading/empty states are already covered by the affected components.

## Architectural Fit Assessment

The fix is a **single-call-site defensive guard** that aligns with patterns already used elsewhere in this codebase. Examples observed:

- `Dashboard.tsx:28` — `const { data: allTileData = [], ... } = useTileData()` (destructuring default `[]`).
- `DashboardSettings.tsx:18` — `const { data: availableTiles = [], ... } = useAvailableTiles()`.
- `JournalList.tsx:202-203` — `currentQuery.data?.entries || []`.

The codebase already converges on "default-to-empty-array at the React Query consumer boundary." The defect is consistent with one or two sites where this convention was missed during the three same-day PRs (#2962, #2943, #2948). Integration points: React Query hooks under `frontend/src/api/hooks/`, components under `frontend/src/components/dashboard/` and `frontend/src/components/pages/Journal/`.

The error fires on `/` (Dashboard route — `App.tsx:7`/`Dashboard.tsx`), so the highest-probability culprits are (ranked):

1. **`Dashboard.tsx:38`** — `userSettings.tiles.reduce(...)`. `useUserDashboardSettings` short-circuits to `{tiles: [], ...}` on 403 but otherwise trusts `response.json()`; if backend returns a malformed/null `tiles` field, `.reduce` (or `.filter` on the same shape, depending on Terser inlining of `Yq`) throws.
2. **`Dashboard.tsx:43`** — `allTileData.filter(...)`. Already guarded by `!allTileData.length`, so failure requires `allTileData` to be a non-array object that has `.length` truthy. Lower probability.
3. **`DashboardSettings.tsx:38`** — `userSettings?.tiles.filter(...)`. `?.` only stops at `userSettings`; `userSettings.tiles` could still be non-array. **Not on the initial `/` render path** (only after Settings click), but it's reachable from the dashboard and worth fixing under FR-3.

Minified symbol `Yq` must still be resolved from source maps (FR-1) before committing; the above is a prioritized inspection list, not a conclusion.

## Proposed Architecture

### Component Overview

```
                          ┌─────────────────────────────┐
                          │  React Query hooks          │
                          │  (frontend/src/api/hooks/)  │
                          │  - useTileData              │
                          │  - useUserDashboardSettings │
                          │  - useJournalEntries        │
                          │  - useSearchJournalEntries  │
                          └──────────────┬──────────────┘
                                         │ data (may be undefined/null
                                         │ during load, on 4xx/5xx, or
                                         │ when response shape drifts)
                                         ▼
        ┌─────────────────────────────────────────────────────────┐
        │  Consumer components (Dashboard, JournalList, settings) │
        │                                                         │
        │  ── Defensive boundary ──                               │
        │  Apply `data ?? <safe default>` at the destructure /    │
        │  selector site. No global wrapper.                      │
        └─────────────────────────────────────────────────────────┘
```

Defensive guards live at the **consumer boundary**, never as a middleware/global wrapper. This matches NFR-3 ("do not silently hide a real upstream bug") and the spec's Out-of-Scope rule banning global runtime array defaults.

### Key Design Decisions

#### Decision 1: Symbol resolution method

**Options considered:**
- A. Enable `GENERATE_SOURCEMAP=true` for a one-off prod-equivalent build, fetch the deployed bundle, run `source-map-cli` / `npx source-map-explorer` to walk the stack frame.
- B. Run `npm run build` locally with sourcemaps, search the emitted `*.js` for the `Yq` identifier and trace back through the map.
- C. Skip sourcemaps; inspect every `.filter()` introduced/modified in the three suspect PRs by hand.

**Chosen approach:** **B + C in parallel.** Build locally with `GENERATE_SOURCEMAP=true npm run build`, grep the bundle for `Yq`, walk the map back to source. Simultaneously inspect the three PRs' diffs. The two converge in minutes; whichever lands first is the answer, the other is corroboration.

**Rationale:** The Dockerfile does not set `GENERATE_SOURCEMAP=false` and CRA defaults to emitting maps, so a local build reproduces the production minification. Option A requires the original prod bundle artifact, which may not be retained for a single-occurrence telemetry signal. Option C alone is sufficient because the candidate set is small (~3 PRs touching list/grid code) and the inspection is the same audit FR-3 mandates anyway.

#### Decision 2: Shape of the defensive guard

**Options considered:**
- A. Destructuring default: `const { data: tiles = [] } = useFoo()` — only catches `undefined`.
- B. Nullish coalescing: `const tiles = data ?? []` — catches `undefined` and `null`.
- C. `Array.isArray` guard: `const tiles = Array.isArray(data) ? data : []` — catches anything non-array.
- D. Schema validation (Zod) at the hook layer.

**Chosen approach:** **C at the failing call site, B for sibling sites where the type system already guarantees array-or-undefined.**

**Rationale:** The bug reached production because the destructuring default (A) missed the `null` or non-array case. `Array.isArray` (C) is the narrowest correct guard for the exact failure mode (`.filter is not a function`) and survives future contract drift. Use B as the lighter form for sibling sites where TS types already exclude non-array values and the failure mode is a missing default. Option D is over-engineering for this single occurrence and conflicts with the spec's "Out of Scope: global runtime guards."

#### Decision 3: Where the guard lives — hook vs. consumer

**Options considered:**
- A. Patch each consumer call site (component).
- B. Patch the hook so `data` is always an array.
- C. Both.

**Chosen approach:** **A (consumer-side).**

**Rationale:** The spec is explicit: "no silent data-swallowing" and "the loading/empty path must be handled explicitly in the component" (FR-2). Hook-side normalization would mask the distinction between "still loading," "error," and "empty result." Consumers already have `isLoading`/`error` branches; the guard only protects the success branch from contract drift. Hook-side patches also risk causing other consumers to silently render an empty UI on real backend errors.

#### Decision 4: Regression test placement

**Options considered:**
- A. Co-located `__tests__/` next to the component (existing convention — see `frontend/src/components/pages/__tests__/Dashboard.test.tsx`).
- B. Shared `frontend/src/__tests__/` directory.

**Chosen approach:** **A.**

**Rationale:** Matches the established convention everywhere in this repo. The existing `Dashboard.test.tsx` already mocks `useUserDashboardSettings` and `useTileData` — the regression test extends that file with `undefined`/`null`/`[]` cases for the affected hook.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits only:

- **Likely fix site (after FR-1 confirms):** one of
  - `frontend/src/components/pages/Dashboard.tsx` (lines 27–53 area — `useUserDashboardSettings`/`useTileData` consumption)
  - `frontend/src/components/dashboard/DashboardSettings.tsx:38` — `userSettings?.tiles.filter(...)`
  - `frontend/src/components/pages/Journal/JournalList.tsx` (entries derivation around line 202)
- **Regression test:** extend `frontend/src/components/pages/__tests__/Dashboard.test.tsx` (or the analogous test file next to whichever component is the culprit).
- **PR audit notes:** record findings in the PR description (per FR-3 acceptance); do not add a new doc file.

### Interfaces and Contracts

No public interface changes. Two internal contracts are tightened:

1. **Consumer pattern for React Query array data:**
   ```ts
   // Preferred at the failing site
   const tiles = Array.isArray(data) ? data : [];

   // Acceptable at sibling sites where TS types already exclude non-array
   const tiles = data ?? [];
   ```
   No new utility. Inlined at each site; this matches existing style and avoids premature abstraction.

2. **No new types.** Existing `DashboardTile[]`, `UserDashboardSettings`, `JournalEntryDto[]` shapes are unchanged.

If FR-1 reveals that the **backend response shape** drifted from the TypeScript client (NFR-3), file a separate issue and link from the PR; do not patch the OpenAPI generator or backend in this PR. The MVP architecture doc treats the auto-generated client as authoritative — silent client-side smoothing of contract drift violates that.

### Data Flow

For the Dashboard initial render at `/`:

```
1. Mount Dashboard
2. useUserDashboardSettings() — React Query starts
3. useTileData() — React Query starts (refetchInterval: 30000)
4. settingsLoading || dataLoading → spinner branch (lines 113–116)
5. Both queries resolve →
   - userSettings: UserDashboardSettings | undefined
   - allTileData: DashboardTile[] (default [] via destructuring)
6. visibleTileData = useMemo([...])
   ├─ Line 36 guard: !userSettings || !allTileData.length → []
   ├─ Line 38: userSettings.tiles.reduce(...)   ← guard required if .tiles can be non-array
   ├─ Line 43: allTileData.filter(...)          ← guard required if allTileData can be non-array despite the line-36 length check
   └─ Line 48: .sort(...)
7. <DashboardGrid tiles={visibleTileData} />
```

The fix inserts an `Array.isArray` check around the iterable just before each array method is called (steps 6.b, 6.c). The line-36 guard becomes `if (!userSettings || !Array.isArray(allTileData) || allTileData.length === 0) return [];` — a minimal expansion that protects both downstream calls.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Sourcemap-based symbol resolution fails (maps not preserved for the deployed bundle) | Medium | Run FR-3 audit in parallel (Decision 1) — it covers the same surface area independent of map availability. |
| Guard silently hides a real backend contract violation (NFR-3) | Medium | When a guard short-circuits on a non-array path, also log a one-time `console.warn` via the existing telemetry pipeline (`frontend/src/telemetry/`) **only** if FR-1 confirms the upstream is misbehaving; otherwise file a follow-up issue per NFR-3 and add no log. |
| Audit (FR-3) misses sibling sites because three PRs touched many files | Medium | Use `git diff` against each PR's base commit, filter for `.tsx`/`.ts`, then grep for `\.filter\(`, `\.map\(`, `\.reduce\(` in the changed hunks. Document the sites reviewed in the PR description (acceptance criterion). |
| Regression test mocks a hook return that doesn't reflect reality (false confidence) | Low | Test must mirror the actual React Query result shape: `{ data: undefined, isLoading: false, error: null }` for the "resolved-to-undefined" case and `{ data: null, ... }` for the null case — match the existing `mockUseTileData.mockReturnValue({...})` style in `Dashboard.test.tsx`. |
| TypeScript types lie (server returns `null` for a non-nullable array per OpenAPI schema) | Low | Documented as a known generator limitation in `docs/development/api-client-generation.md` (DTO/record rule). The runtime guard is the correct defense. |
| Fix is applied to the wrong call site because `Yq` was misidentified | Low | Regression test must FAIL against pre-fix code (FR-4 acceptance) — if it doesn't fail before the patch, the call site is wrong. |

## Specification Amendments

The spec is sound. Two small clarifications:

1. **FR-2 should explicitly allow `Array.isArray`-style guards**, not just `data ?? []` / destructuring defaults. The destructuring default in `Dashboard.tsx:28` already exists yet did not prevent the error, which implies the failure mode is `null` or non-array — `Array.isArray` is the correct shape per Decision 2.
2. **FR-1 should specify the source-map approach as a local prod-equivalent build** (`GENERATE_SOURCEMAP=true npm run build`) since prod bundle maps are not necessarily retained. The PR audit in FR-3 should be treated as the primary path, not a fallback.

## Prerequisites

- **None on infrastructure or backend.** No migrations, no Key Vault secrets, no deployment changes.
- **Developer-local:** Node + npm already required by the project. Ensure `npm install` has run in `frontend/` before `npm run build`.
- **Access:** GitHub read access to PRs #2962, #2943, #2948 for the FR-3 audit.
- **Validation gates** (from `CLAUDE.md`): before declaring done — `npm run build`, `npm run lint`, and Jest run covering the affected component. E2E suite runs nightly, not in PR CI — no E2E needed (consistent with the spec's "Out of Scope" entry).