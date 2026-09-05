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
