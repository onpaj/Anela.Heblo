# Design: Extract OrgChart Position Filtering Logic

## Component Design

No UI changes. This design covers two existing files only — one gains a new exported pure function, the other relocates a computation and swaps it from a plain per-render expression to a memoized one. No new files, no new components, no change to `PositionCard`, `useOrgChart`, or the rendered markup.

### `frontend/src/pages/orgChartUtils.ts` — `filterPositions` (new export)

Joins the module's existing pure-helper family (`calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren`) as a fifth sibling. Placed near `getAllParentPositionIds`, its sole dependency, for local readability (non-binding ordering).

```ts
export function filterPositions(
  positions: Position[],
  departmentFilter: string,
  levelFilter: string,
): Position[]
```

Responsibility: given the full, unfiltered position list plus the two active filter values from `OrgChartPage`'s `filters` state, return the subset of positions that should be rendered — no I/O, no React, no mutation of `positions` or its elements.

Internal algorithm (sequential narrowing, mirroring the current inline IIFE exactly):

1. `matching := positions` (reference to input; narrowed by reassignment, not mutation).
2. If `departmentFilter !== 'all'`:
   - `deptMatches := positions.filter(p => p.department === departmentFilter)`.
   - `ancestorIds := union of getAllParentPositionIds(p.id!, positions) for each p in deptMatches` — ancestor lookup always uses the original `positions` array, never the narrowing `matching`.
   - `matching := positions.filter(p => p.department === departmentFilter || ancestorIds.has(p.id!))`.
3. If `levelFilter !== 'all'`:
   - `matching := matching.filter(p => !p.level || p.level <= parseInt(levelFilter, 10))` — operates on the already department-narrowed `matching`, not the original `positions`; the `!p.level` short-circuit (undefined or `0` always passes) is preserved unchanged, not "fixed".
4. Return `matching`.

Both filter stages compose via sequential narrowing (department first, then level on that result), not independent union — an ancestor pulled in by the department stage can still be dropped by the level stage if its own level exceeds the cutoff.

Callers: only `OrgChartPage.tsx`, via the `useMemo` described below.

### `frontend/src/pages/OrgChartPage.tsx` — call-site relocation

Responsibility unchanged (container/presentation component); only the *location* and *memoization* of the filtering step changes.

- Import `filterPositions` from `./orgChartUtils` alongside the existing imports.
- Remove the inline IIFE currently at lines 133–164.
- Remove the `getAllParentPositionIds` import only if, after the edit, it has no remaining use in the file (its only current use is inside the IIFE being deleted — confirm via `npm run lint` / `npm run build` rather than assuming).
- Add a new `useMemo` call immediately after the existing `orgData` `useMemo` (lines 40–51) — i.e. before `getElementPosition` and before the `isLoading` / `queryError` / `!orgData` early returns, since a hook cannot legally sit after a conditional `return` (Rules of Hooks):

```ts
const filteredPositions = useMemo(
  () => (orgData ? filterPositions(orgData.organization.positions, filters.department, filters.level) : []),
  [orgData, filters],
);
```

- Dependency array is `[orgData, filters]` (object reference, not destructured primitives) — `filters` is always replaced wholesale via `setFilters({...})`, never mutated in place, so reference equality tracks field changes correctly while avoiding a recompute on the unrelated `zoom` / `positionRects` state changes that currently cause redundant recomputation.
- No other logic changes: `totalEmployees`, `getChildren`, `renderConnections`, the department `<select>` options, and the `positionRects` `useEffect` (lines 70–104, dependency array unchanged at `[orgData, filters, zoom]`) all continue to read `filteredPositions` exactly as before — only its source computation moved and became memoized.

### Data flow (updated)

```
useOrgChart() → orgChartResponse
       │
       ▼
useMemo #1 (unchanged): orgData = calculateLevels(transform(orgChartResponse)) | null
       │
       ▼
useMemo #2 (NEW, moved above early returns):
   filteredPositions = orgData
     ? filterPositions(orgData.organization.positions, filters.department, filters.level)
     : []
   deps: [orgData, filters]
       │
       ▼
early returns (isLoading / queryError / !orgData) — unaffected, still gate before render
       │
       ▼
render: totalEmployees, getChildren, buildTree(filteredPositions), renderConnections()
```

`positionRects` `useEffect` keeps reading `orgData` and `filters` directly (not `filteredPositions`) — that dependency is explicitly out of scope for this change.

## Data Schemas

No data model, API, or persistence changes. This is a client-side, in-memory, pure-function refactor over already-fetched data.

Reused types (unchanged, from `orgChartUtils.ts` / generated OpenAPI client):

```ts
type Position = PositionDto; // generated from backend OpenAPI contract
// relevant fields used by filterPositions: id, department, level, parentPositionId

interface OrganizationData {
  organization: {
    name: string;
    positions: Position[];
  };
}
```

`filterPositions` input/output shape:

| Parameter | Type | Notes |
|---|---|---|
| `positions` | `Position[]` | full, unfiltered list — original array, never the already-narrowed intermediate, is used for ancestor lookups |
| `departmentFilter` | `string` | `'all'` or an exact `department` value |
| `levelFilter` | `string` | `'all'` or `'1'`–`'4'` (parsed via `parseInt(levelFilter, 10)`) |
| returns | `Position[]` | filtered subset, original relative order preserved, no duplicates |

Test data shape (for the new `describe('filterPositions', ...)` block in `frontend/src/pages/__tests__/orgChartUtils.test.ts`): built via the file's existing `makePosition(overrides: Partial<PositionDto>)` and `buildOrganizationData(positions)` helpers, following the established Arrange/Act/Assert comment style and `.sort()`-before-comparison convention for order-insensitive id-array assertions.

No new events, no new HTTP endpoints, no new persisted entities, no feature flag.
