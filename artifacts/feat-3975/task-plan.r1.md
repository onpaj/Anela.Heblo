# Extract OrgChart Position Filtering Logic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the inline department/level position-filtering IIFE out of `OrgChartPage.tsx` into a pure, unit-tested `filterPositions` function in `orgChartUtils.ts`, wired via a `useMemo` placed above the component's early returns, with no behavior change.

**Architecture:** `orgChartUtils.ts` already hosts the page's other pure helpers (`calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren`); `filterPositions` joins them as a fifth sibling, reusing `getAllParentPositionIds` internally for ancestor-inclusive department filtering. `OrgChartPage.tsx` then calls it through a `useMemo` colocated with the existing `orgData` memo — moved above the `isLoading`/`queryError`/`!orgData` early returns to satisfy React's Rules of Hooks — replacing the plain per-render IIFE expression and eliminating recomputation on zoom-only re-renders.

**Tech Stack:** React, TypeScript, Jest (via `react-scripts test`), existing `makePosition`/Arrange-Act-Assert test conventions in `orgChartUtils.test.ts`.

---

### task: add-filter-positions-with-tests

**Files:**
- Modify: `frontend/src/pages/orgChartUtils.ts` (insert new function after line 71, i.e. after `getAllParentPositionIds` and before `buildTree` at line 73)
- Test: `frontend/src/pages/__tests__/orgChartUtils.test.ts` (append a new `describe('filterPositions', ...)` block after the existing `describe('getChildren', ...)` block, i.e. after line 216)

#### Goal
Add the pure `filterPositions` function to the shared utils module and lock down its behavior — including the non-obvious ancestor-inclusive department filter and the sequential-narrowing composition with the level filter — with direct unit tests, before touching the component that currently inlines this logic.

#### Context (from spec.r1.md / arch-review.r1.md / design.r1.md)
- Exact signature (spec FR-1, design):
  ```ts
  export function filterPositions(
    positions: Position[],
    departmentFilter: string,
    levelFilter: string,
  ): Position[]
  ```
- Must be **exactly equivalent** to the current inline IIFE at `OrgChartPage.tsx:133-164`:
  1. `matchingPositions = positions` (start with the full input array).
  2. If `departmentFilter !== 'all'`: find all positions where `department === departmentFilter`; for each, collect ancestors via `getAllParentPositionIds(pos.id!, positions)` — **always against the original, unfiltered `positions` array**, never the narrowing intermediate; then set `matchingPositions` to every position in `positions` whose `department === departmentFilter` OR whose `id` is in the collected ancestor-id set.
  3. If `levelFilter !== 'all'`: filter `matchingPositions` (the already-narrowed result from step 2, not the original `positions`) to `!pos.level || pos.level <= parseInt(levelFilter, 10)`.
  4. Return the result.
- Filters compose via **sequential narrowing** (department first, then level on that result) — NOT independent union. An ancestor pulled in by the department stage can still be dropped by the level stage if its own level exceeds the cutoff. This is the one subtlety most likely to be implemented wrong (arch-review Risk table, Medium severity) — the combined-filter test must pin this down.
- The `!pos.level` short-circuit (undefined **or `0`** always passes the level filter) is existing, intentional behavior — explicitly out of scope to "fix" (spec Out of Scope). Do not add a `pos.level !== undefined` or `pos.level > 0` guard.
- Use `parseInt(levelFilter, 10)` — explicit radix (the current inline code omits it; adding it is a documented no-op for this domain since values are always `'1'`-`'4'` or `'all'`).
- Function must be pure: no mutation of `positions` or any element in it.
- `orgChartUtils.ts` currently ends at line 88 (`getChildren`). `getAllParentPositionIds` is defined at lines 51-71, `buildTree` starts at line 73 — insert `filterPositions` between them (non-binding ordering suggestion from arch-review/design, but follow it for local readability next to its dependency).
- Test file conventions already established in `orgChartUtils.test.ts`: `makePosition(overrides: Partial<PositionDto>)` helper (lines 12-16), `buildOrganizationData` helper (not needed here — `filterPositions` takes a `Position[]` directly, not `OrganizationData`), Arrange/Act/Assert comments, `.sort()` on id arrays before equality checks where order isn't semantically meaningful (see `getAllParentPositionIds` and `buildTree` describe blocks).
- Acceptance criteria to cover (spec FR-2):
  - No filters (`'all'`, `'all'`) returns every input position, unchanged, in original order.
  - Department filter alone (leaf department): returns that department's positions plus all ancestors up to the root, excluding unrelated branches.
  - Department filter alone with a multi-level chain inside the same department: still includes external ancestors above the department's own root-most member.
  - Department filter matching no position: returns an empty array.
  - Level filter alone: returns only positions at or below the given level, always including positions with `level` undefined.
  - Level filter `'1'`: returns only level-1 positions (plus any level-undefined ones).
  - Combined department + level: covers both an ancestor retained by department but *excluded* by level (its own level exceeds the cutoff), and one that is retained.
  - Empty `positions` input: returns `[]` for any filter combination.
  - Does not mutate its input array (assert input reference/contents unchanged after the call — mirrors the existing `calculateLevels` "does not mutate" test at lines 59-71).
  - A position with `level: 0` is treated as falsy and always passes the level filter (explicitly confirms `!pos.level` is preserved, not "fixed").

#### Implementation steps

- [ ] **Step 1: Write the failing tests**

First, update the import block at the top of `frontend/src/pages/__tests__/orgChartUtils.test.ts` (lines 1-8) to add `filterPositions`:

Replace:
```ts
import {
  calculateLevels,
  getAllParentPositionIds,
  buildTree,
  getChildren,
  OrganizationData,
  Position,
} from '../orgChartUtils';
```
with:
```ts
import {
  calculateLevels,
  getAllParentPositionIds,
  buildTree,
  getChildren,
  filterPositions,
  OrganizationData,
  Position,
} from '../orgChartUtils';
```

Then append the following new `describe` block to the end of the file (after the closing `});` of the `describe('getChildren', ...)` block at line 216):

```ts
describe('filterPositions', () => {
  it('returns every position unchanged and in order when both filters are "all"', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a', department: 'Sales' }),
      makePosition({ id: 'b', department: 'Engineering', parentPositionId: 'a' }),
    ];

    // Act
    const result = filterPositions(positions, 'all', 'all');

    // Assert
    expect(result).toEqual(positions);
  });

  it('includes a leaf department plus all its ancestors, excluding unrelated branches', () => {
    // Arrange — ceo -> vpEng -> engineer (Engineering), ceo -> vpSales -> rep (Sales)
    const positions = [
      makePosition({ id: 'ceo', department: 'Executive' }),
      makePosition({ id: 'vpEng', department: 'Executive', parentPositionId: 'ceo' }),
      makePosition({ id: 'engineer', department: 'Engineering', parentPositionId: 'vpEng' }),
      makePosition({ id: 'vpSales', department: 'Executive', parentPositionId: 'ceo' }),
      makePosition({ id: 'rep', department: 'Sales', parentPositionId: 'vpSales' }),
    ];

    // Act
    const result = filterPositions(positions, 'Engineering', 'all');

    // Assert — engineer (match) + vpEng, ceo (ancestors); vpSales/rep excluded
    expect(result.map((p) => p.id).sort()).toEqual(['ceo', 'engineer', 'vpEng']);
  });

  it('includes external ancestors above a department that itself spans multiple levels', () => {
    // Arrange — ceo (Executive) -> deptHead (Engineering) -> deptMember (Engineering)
    const positions = [
      makePosition({ id: 'ceo', department: 'Executive' }),
      makePosition({ id: 'deptHead', department: 'Engineering', parentPositionId: 'ceo' }),
      makePosition({ id: 'deptMember', department: 'Engineering', parentPositionId: 'deptHead' }),
    ];

    // Act
    const result = filterPositions(positions, 'Engineering', 'all');

    // Assert — both Engineering positions plus the external Executive ancestor
    expect(result.map((p) => p.id).sort()).toEqual(['ceo', 'deptHead', 'deptMember']);
  });

  it('returns an empty array when the department filter matches no position', () => {
    // Arrange
    const positions = [makePosition({ id: 'a', department: 'Sales' })];

    // Act
    const result = filterPositions(positions, 'Nonexistent', 'all');

    // Assert
    expect(result).toEqual([]);
  });

  it('returns only positions at or below the given level, always including level-undefined positions', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a', department: 'X', level: 1 }),
      makePosition({ id: 'b', department: 'X', level: 2 }),
      makePosition({ id: 'c', department: 'X', level: 3 }),
      makePosition({ id: 'd', department: 'X' }), // no level set
    ];

    // Act
    const result = filterPositions(positions, 'all', '2');

    // Assert
    expect(result.map((p) => p.id).sort()).toEqual(['a', 'b', 'd']);
  });

  it('level "1" returns only root-level positions plus level-undefined ones', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a', department: 'X', level: 1 }),
      makePosition({ id: 'b', department: 'X', level: 2 }),
      makePosition({ id: 'c', department: 'X' }), // no level set
    ];

    // Act
    const result = filterPositions(positions, 'all', '1');

    // Assert
    expect(result.map((p) => p.id).sort()).toEqual(['a', 'c']);
  });

  it('a position with level 0 is treated as falsy and always passes the level filter', () => {
    // Arrange — mirrors the existing !pos.level short-circuit; level 0 is intentionally not "fixed"
    const positions = [
      makePosition({ id: 'a', department: 'X', level: 0 }),
      makePosition({ id: 'b', department: 'X', level: 5 }),
    ];

    // Act
    const result = filterPositions(positions, 'all', '1');

    // Assert
    expect(result.map((p) => p.id).sort()).toEqual(['a']);
  });

  it('composes department and level filters via sequential narrowing, not independent union', () => {
    // Arrange — ceo (level 1, Executive) -> midManager (level 2, Executive) -> engineer (level 3, Engineering)
    // Department filter on 'Engineering' pulls in ceo and midManager as ancestors.
    // Level filter '1' then drops midManager (level 2) but keeps ceo (level 1); engineer (level 3) is also dropped.
    const positions = [
      makePosition({ id: 'ceo', department: 'Executive', level: 1 }),
      makePosition({ id: 'midManager', department: 'Executive', level: 2, parentPositionId: 'ceo' }),
      makePosition({ id: 'engineer', department: 'Engineering', level: 3, parentPositionId: 'midManager' }),
    ];

    // Act
    const result = filterPositions(positions, 'Engineering', '1');

    // Assert — only ceo survives: retained by department-stage ancestor inclusion,
    // then retained by level-stage since level 1 <= 1. midManager and engineer are excluded by level.
    expect(result.map((p) => p.id).sort()).toEqual(['ceo']);
  });

  it('retains a department-included ancestor whose own level satisfies the level filter', () => {
    // Arrange — same hierarchy, but level filter '2' keeps ceo (1) and midManager (2), drops engineer (3)
    const positions = [
      makePosition({ id: 'ceo', department: 'Executive', level: 1 }),
      makePosition({ id: 'midManager', department: 'Executive', level: 2, parentPositionId: 'ceo' }),
      makePosition({ id: 'engineer', department: 'Engineering', level: 3, parentPositionId: 'midManager' }),
    ];

    // Act
    const result = filterPositions(positions, 'Engineering', '2');

    // Assert
    expect(result.map((p) => p.id).sort()).toEqual(['ceo', 'midManager']);
  });

  it('returns an empty array for an empty positions input, for any filter combination', () => {
    // Act & Assert
    expect(filterPositions([], 'all', 'all')).toEqual([]);
    expect(filterPositions([], 'Sales', 'all')).toEqual([]);
    expect(filterPositions([], 'all', '2')).toEqual([]);
    expect(filterPositions([], 'Sales', '2')).toEqual([]);
  });

  it('does not mutate its input array', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a', department: 'X', level: 1 }),
      makePosition({ id: 'b', department: 'Y', level: 2, parentPositionId: 'a' }),
    ];
    const snapshot = positions.map((p) => ({ ...p }));

    // Act
    filterPositions(positions, 'Y', '1');

    // Assert
    expect(positions).toEqual(snapshot);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd frontend && CI=true npm test -- orgChartUtils.test.ts --watchAll=false`
Expected: FAIL — `TypeError: (0 , _orgChartUtils.filterPositions) is not a function` (or a TypeScript compile error `Module '"../orgChartUtils"' has no exported member 'filterPositions'`), since `filterPositions` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

Insert into `frontend/src/pages/orgChartUtils.ts` after line 71 (end of `getAllParentPositionIds`) and before line 73 (`export function buildTree`):

```ts
export function filterPositions(
  positions: Position[],
  departmentFilter: string,
  levelFilter: string,
): Position[] {
  let matchingPositions = positions;

  if (departmentFilter !== 'all') {
    // Find all positions in the selected department
    const departmentPositions = positions.filter((pos) => pos.department === departmentFilter);

    // Collect all parent position IDs for these department positions
    // (always looked up against the original, unfiltered `positions` array)
    const parentPositionIds = new Set<string>();
    departmentPositions.forEach((pos) => {
      const parents = getAllParentPositionIds(pos.id!, positions);
      parents.forEach((id) => parentPositionIds.add(id));
    });

    // Include department positions + all their parents
    matchingPositions = positions.filter(
      (pos) => pos.department === departmentFilter || parentPositionIds.has(pos.id!),
    );
  }

  // Apply level filter (show selected level and all parent levels)
  if (levelFilter !== 'all') {
    matchingPositions = matchingPositions.filter(
      (pos) => !pos.level || pos.level <= parseInt(levelFilter, 10),
    );
  }

  return matchingPositions;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd frontend && CI=true npm test -- orgChartUtils.test.ts --watchAll=false`
Expected: PASS — all tests in `orgChartUtils.test.ts` pass, including the new `filterPositions` describe block (11 new test cases) alongside the pre-existing `calculateLevels`/`getAllParentPositionIds`/`buildTree`/`getChildren` blocks.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/orgChartUtils.ts frontend/src/pages/__tests__/orgChartUtils.test.ts
git commit -m "Extract filterPositions pure function into orgChartUtils with unit tests"
```

### task: wire-org-chart-page-to-filter-positions

**Files:**
- Modify: `frontend/src/pages/OrgChartPage.tsx:5-12` (imports), `frontend/src/pages/OrgChartPage.tsx:40-51` (existing `orgData` memo — new memo inserted immediately after it), `frontend/src/pages/OrgChartPage.tsx:133-164` (remove the inline IIFE)

#### Goal
Replace the inline filtering IIFE in `OrgChartPage.tsx` with a call to the new `filterPositions`, wrapped in a `useMemo` that is moved above the component's early returns so it complies with React's Rules of Hooks, and stops recomputing on every zoom/positionRect-only re-render.

#### Context (from spec.r1.md / arch-review.r1.md / design.r1.md)
- Current file layout (pre-edit, verified against the actual source):
  - Lines 5-12: import block from `./orgChartUtils` — currently imports `calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren as orgChartGetChildren`, `Position`, `OrganizationData`.
  - Lines 40-51: existing `orgData` `useMemo` (deps `[orgChartResponse]`).
  - Lines 106-128: three early returns — `isLoading` (106-112), `queryError` (114-120), `!orgData` (122-128).
  - Lines 133-164: the inline IIFE computing `filteredPositions` (this entire block is deleted).
  - Line 146: the only use of `getAllParentPositionIds` in this file (inside the IIFE being deleted) — confirmed via search, so the import must be removed.
- **Rules of Hooks constraint (spec FR-3, arch-review Decision 2):** a `useMemo` cannot be placed after a conditional `return` (lines 106-128 return early). The new `useMemo` must be inserted **immediately after** the existing `orgData` memo (i.e., right after line 51, before `getElementPosition` at line 53) — i.e. before all three early returns — not left in place at the old IIFE location.
- Exact call-site contract (spec, arch-review, design all agree verbatim):
  ```ts
  const filteredPositions = useMemo(
    () => (orgData ? filterPositions(orgData.organization.positions, filters.department, filters.level) : []),
    [orgData, filters],
  );
  ```
- Dependency array is `[orgData, filters]` — the `filters` object reference, not destructured `filters.department`/`filters.level` — because `filters` is always replaced wholesale via `setFilters({...})` (never mutated in place), so reference equality already tracks field changes correctly (spec FR-3 acceptance criteria; arch-review Decision 3).
- Must NOT change: `totalEmployees` (reads `filteredPositions`, unchanged), `getChildren` (unchanged), `renderConnections` (unchanged), the department `<select>` options (unchanged), and the `positionRects` `useEffect` at lines 70-104 — its dependency array stays `[orgData, filters, zoom]` exactly as-is; it does NOT get changed to depend on `filteredPositions` (spec explicitly puts this out of scope).
- After removing the IIFE, the filtering block in the render body becomes a single reference to the already-computed `filteredPositions` memo value — no remaining inline filtering logic in the component.
- Remove `getAllParentPositionIds` from the `./orgChartUtils` import (line 7) since its only use in this file was inside the deleted IIFE; add `filterPositions` to that same import. Confirm via lint (no unused-import warning) and build (no missing-symbol error) after editing — do not remove speculatively without this check.

#### Implementation steps

- [ ] **Step 1: Update the import block (lines 5-12)**

Replace:
```ts
import {
  calculateLevels,
  getAllParentPositionIds,
  buildTree,
  getChildren as orgChartGetChildren,
  Position,
  OrganizationData,
} from './orgChartUtils';
```
with:
```ts
import {
  calculateLevels,
  filterPositions,
  buildTree,
  getChildren as orgChartGetChildren,
  Position,
  OrganizationData,
} from './orgChartUtils';
```

- [ ] **Step 2: Insert the new `useMemo` immediately after the existing `orgData` memo (after line 51, before `getElementPosition` at line 53)**

Insert:
```ts
  // Filter positions based on department and level; must sit above the early
  // returns below (isLoading/queryError/!orgData) per React's Rules of Hooks.
  const filteredPositions = useMemo(
    () =>
      orgData
        ? filterPositions(orgData.organization.positions, filters.department, filters.level)
        : [],
    [orgData, filters],
  );

```

- [ ] **Step 3: Remove the inline IIFE at the old location (lines 133-164 in the pre-edit file)**

Delete this entire block (it is now redundant — `filteredPositions` is already computed above):
```ts
  // Filter positions based on department and level
  const filteredPositions = (() => {
    const allPositions = orgData.organization.positions;

    // First, find positions that match the department filter
    let matchingPositions = allPositions;

    if (filters.department !== 'all') {
      // Find all positions in the selected department
      const departmentPositions = allPositions.filter(pos => pos.department === filters.department);

      // Collect all parent position IDs for these department positions
      const parentPositionIds = new Set<string>();
      departmentPositions.forEach(pos => {
        const parents = getAllParentPositionIds(pos.id!, allPositions);
        parents.forEach(id => parentPositionIds.add(id));
      });

      // Include department positions + all their parents
      matchingPositions = allPositions.filter(pos =>
        pos.department === filters.department || parentPositionIds.has(pos.id!)
      );
    }

    // Apply level filter (show selected level and all parent levels)
    if (filters.level !== 'all') {
      matchingPositions = matchingPositions.filter(pos =>
        !pos.level || pos.level <= parseInt(filters.level)
      );
    }

    return matchingPositions;
  })();

```

Leave the line immediately before it (`const departments = Array.from(new Set(orgData.organization.positions.map((p) => p.department)));`) and the line immediately after it (`const totalEmployees = filteredPositions.reduce(...)`) untouched — only the IIFE block itself is deleted.

- [ ] **Step 4: Run build and lint to confirm the import cleanup and hook placement are correct**

Run: `cd frontend && npm run build`
Expected: PASS — no TypeScript errors (in particular, no "possibly null" error on `orgData.organization.positions` inside the new memo, confirming the `orgData ? ... : []` guard is correctly in place; no "cannot find name `getAllParentPositionIds`" error confirming the old IIFE was fully removed).

Run: `cd frontend && npm run lint`
Expected: PASS — no `no-unused-vars` warning for `getAllParentPositionIds` (confirms it was correctly dropped from the import) and no `react-hooks/rules-of-hooks` violation (confirms the new `useMemo` is positioned before all early returns).

- [ ] **Step 5: Run the full test suite for this page's utils to confirm no regression**

Run: `cd frontend && CI=true npm test -- orgChartUtils.test.ts --watchAll=false`
Expected: PASS — all tests (pre-existing plus the new `filterPositions` block from the previous task) still pass unchanged.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/OrgChartPage.tsx
git commit -m "Wire OrgChartPage to filterPositions via useMemo placed above early returns"
```
