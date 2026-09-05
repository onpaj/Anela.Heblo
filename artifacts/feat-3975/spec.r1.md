# Specification: Extract OrgChart Position Filtering Logic

## Summary
`OrgChartPage.tsx` computes its filtered position list via an inline IIFE embedded in the component's render body, mixing non-trivial business logic (ancestor-inclusive department filtering, level filtering) with presentation code. This spec covers extracting that logic into a pure, independently-tested `filterPositions` function in `frontend/src/pages/orgChartUtils.ts`, and wiring the component to call it via `useMemo`. This is a behavior-preserving refactor: the rendered org chart, its filter dropdowns, and all existing tests must behave identically before and after.

## Background
`orgChartUtils.ts` already hosts the other pure helpers this page depends on (`calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren`), each covered by unit tests in `frontend/src/pages/__tests__/orgChartUtils.test.ts`. The filtering logic at `OrgChartPage.tsx` lines 133–164 is the one piece of the same logical group left inline inside the component, where it cannot be unit-tested without mounting the full component (which requires network/query mocking via `useOrgChart`). The filtering rule — "when a department is selected, also keep every ancestor of matching positions so the tree stays connected up to the root" — is non-obvious and worth locking down with direct tests. The component file is also already 333 lines; moving this logic out reduces it without changing its behavior.

This finding was filed by the daily arch-review routine (`docs` review process is out of scope here — this spec addresses only the flagged code).

## Functional Requirements

### FR-1: Add `filterPositions` to `orgChartUtils.ts`
Add an exported pure function with this signature:

```ts
export function filterPositions(
  positions: Position[],
  departmentFilter: string,
  levelFilter: string,
): Position[]
```

Behavior must be **exactly equivalent** to the current inline IIFE in `OrgChartPage.tsx` (lines 133–164):

1. Start with `matchingPositions = positions` (the full input array).
2. If `departmentFilter !== 'all'`:
   - Find all positions whose `department === departmentFilter`.
   - For each such position, collect the full ancestor-id set via `getAllParentPositionIds(pos.id!, positions)` (using the *original, unfiltered* `positions` array as the lookup universe, matching current behavior).
   - Set `matchingPositions` to every position in `positions` whose `department === departmentFilter` OR whose `id` is in the collected ancestor-id set.
3. If `levelFilter !== 'all'`:
   - Filter `matchingPositions` (the possibly department-filtered result from step 2, not the original `positions`) to those where `!pos.level || pos.level <= parseInt(levelFilter, 10)`.
4. Return the resulting array.

Preserve the existing `!pos.level` short-circuit exactly: a position with no `level` set always passes the level filter, regardless of `levelFilter`'s value. Do not "fix" this — it is existing behavior and out of scope to change (see Out of Scope).

Use `parseInt(levelFilter, 10)` (explicit radix). The current inline code omits the radix; adding it is a safe no-op for this call site (values are always `'1'`–`'4'` or `'all'`, decimal, no leading zeros) and matches the brief's suggested fix. Do not change any other parsing behavior.

**Acceptance criteria:**
- `filterPositions(positions, 'all', 'all')` returns all input positions, unchanged, in original order.
- `filterPositions(positions, someDept, 'all')` returns exactly the positions in `someDept` plus every ancestor (transitively, up to root) of each such position, with no duplicates, in original array order.
- `filterPositions(positions, 'all', '2')` returns positions where `level <= 2` or `level` is undefined/null.
- `filterPositions(positions, someDept, '2')` applies the department+ancestor filter first, then narrows by level — matching the current two-stage behavior (a department match whose own level exceeds the level filter, but which is also an ancestor of a lower-level match, is retained only if it independently satisfies the level filter — i.e. filters compose via sequential narrowing, not via independent union).
- `filterPositions(positions, unknownDept, 'all')` returns an empty array (no position matches `department === unknownDept` and no ancestor set is populated).
- Positions with a `level` of `0` are treated as falsy by `!pos.level` and therefore always pass the level filter (matches current `!pos.level` behavior) — confirm this is intentionally preserved, not fixed.
- Function is pure: does not mutate the input `positions` array or any element in it.
- Output for every existing manual/exploratory scenario in the org chart UI (all-departments/all-levels, single department, single level, department+level combined) is byte-identical to the pre-refactor IIFE's output for the same inputs.

### FR-2: Unit tests for `filterPositions`
Add a `describe('filterPositions', ...)` block to `frontend/src/pages/__tests__/orgChartUtils.test.ts`, following the file's existing style (Arrange/Act/Assert comments, `makePosition` helper, `.sort()` on id arrays before equality checks where order isn't semantically meaningful).

**Acceptance criteria — at minimum, cover:**
- No filters applied (`'all'`, `'all'`) returns every input position.
- Department filter alone: selecting a leaf department returns that department's positions plus all ancestors up to the root, and excludes unrelated branches.
- Department filter alone: selecting a department that itself contains a multi-level chain (parent and child both in the same department) still includes external ancestors above the department's own root-most member.
- Department filter with a department value that matches no position returns an empty array.
- Level filter alone: returns only positions at or below the given numeric level, and always includes positions with `level` undefined (mirrors `calculateLevels` output shape, where every position ends up with a level, but the helper must not assume that).
- Level filter alone: level `'1'` returns only root-level (level 1) positions (plus any leveled-undefined, if constructed that way in the fixture).
- Combined department + level filters: verify the two-stage narrowing (department set computed first, then level-filtered), including a case where an ancestor pulled in by the department filter is *excluded* by the level filter because its own level exceeds the level cutoff, and a case where it is retained.
- Empty `positions` input returns an empty array for any filter combination.
- Does not mutate its input array (assert the input reference/contents are unchanged after the call, consistent with the existing `calculateLevels` "does not mutate" test).

### FR-3: Wire `OrgChartPage.tsx` to use `filterPositions` via `useMemo`
Replace the inline IIFE (current lines 133–164) with a `useMemo`-wrapped call to the new `filterPositions` function, importing it from `./orgChartUtils` alongside the other existing imports.

**Constraint — Rules of Hooks:** The current IIFE sits *after* three early `return` statements (`isLoading`, `queryError`, `!orgData`, lines 106–128). A `useMemo` call cannot be placed after a conditional `return` without violating React's Rules of Hooks (inconsistent hook-call order between renders). The new `useMemo` call **must be moved above** those early returns — colocated with the existing `orgData` `useMemo` (lines 40–51) is the natural placement — and must handle `orgData` being `null` at that point in the render (mirror the guard style already used by the `orgData` memo, e.g. return `[]` when `orgData` is `null`, matching current effective behavior since `filteredPositions` is never read before the `!orgData` early return today).

**Acceptance criteria:**
- `filteredPositions` is computed via a single `useMemo` call positioned before all conditional early returns in the component.
- The `useMemo` dependency array is `[orgData, filters]` (matching what the current IIFE effectively recomputes on: it is a plain expression re-evaluated on every render, and re-renders are driven by `orgData` and `filters` changes plus the unrelated `zoom`/`positionRects` state — recomputing only on `orgData`/`filters` avoids the currently-wasted recomputation on every zoom or position-rect update, which is the same optimization the arch-review brief calls out).
- No other logic in the component changes: `totalEmployees`, `getChildren`, `renderConnections`, the department `<select>` options, and the existing `useEffect` (lines 70–104, dependency array `[orgData, filters, zoom]`) are left exactly as they are — the `useEffect` continues to depend on `filters` and `orgData` directly (not on the new memoized `filteredPositions`), since changing that dependency array is not required to fix the arch-review finding and is out of scope.
- The `getAllParentPositionIds` import in `OrgChartPage.tsx` is removed if (and only if) it becomes unused after the extraction (it is currently imported solely for use inside the IIFE being removed — confirm via lint/build, do not remove speculatively before verifying no other call site exists in the file).
- `frontend/src/pages/OrgChartPage.tsx` has no remaining IIFE-based filtering logic; the filtering block is a single expression: `filterPositions(orgData.organization.positions, filters.department, filters.level)` (or the null-guarded equivalent required by the Rules-of-Hooks placement above).

## Non-Functional Requirements

### NFR-1: Performance
No performance regression; the refactor should be a strict improvement. Currently `filteredPositions` is recomputed on every render of `OrgChartPage` (including zoom-only and position-rect-only re-renders) since it's a plain expression, not memoized. After this change, `filterPositions` must only re-run when `orgData` or `filters` actually change (via `useMemo`), eliminating redundant recomputation on zoom changes. No new async work, no new renders, no measurable change in initial render latency for typical org sizes (tens to low hundreds of positions).

### NFR-2: Security
Not applicable — this is a client-side, in-memory, pure-function refactor over already-fetched data. No new data exposure, no new inputs from untrusted sources, no auth/authorization surface change.

## Data Model
No data model changes. Reuses existing types from `orgChartUtils.ts`:
- `Position` (alias for `PositionDto`, generated from the backend OpenAPI contract) — relevant fields: `id`, `department`, `level`, `parentPositionId`.
- `OrganizationData` — `{ organization: { name: string; positions: Position[] } }`.

No new entities, no backend/API changes, no persistence changes.

## API / Interface Design
Pure function addition to an existing internal module — no HTTP API, no events, no new UI. Function signature (also given in FR-1):

```ts
// frontend/src/pages/orgChartUtils.ts
export function filterPositions(
  positions: Position[],
  departmentFilter: string,
  levelFilter: string,
): Position[]
```

Call site (also given in FR-3):

```ts
// frontend/src/pages/OrgChartPage.tsx
const filteredPositions = useMemo(
  () => (orgData ? filterPositions(orgData.organization.positions, filters.department, filters.level) : []),
  [orgData, filters],
);
```

(Exact null-guard styling left to the implementer to match surrounding code conventions; behavior must match the acceptance criteria in FR-3.)

No changes to the two `<select>` controls, the "Resetovat filtry" button, the zoom controls, or any rendered markup — this is a logic-location change only, not a UI change.

## Dependencies
- Depends on existing `getAllParentPositionIds` in `orgChartUtils.ts` (unchanged).
- Depends on the existing `Position` / `PositionDto` type (unchanged, generated from backend OpenAPI contract — no backend changes required or expected).
- No new npm packages, no new backend endpoints, no feature flag.

## Out of Scope
- Changing the actual filtering *semantics* (e.g., "fixing" the `!pos.level` short-circuit so level-less positions are excluded instead of always-included, or changing department+level to independent-union instead of sequential-narrowing). Any such behavior change is a separate decision requiring product input, not a refactor.
- Changing the `useEffect` (lines 70–104) that computes `positionRects` to depend on `filteredPositions` instead of `orgData`/`filters` — out of scope for this finding; left as-is per FR-3.
- Any other extraction/cleanup opportunity in `OrgChartPage.tsx` (e.g., `getElementPosition`, `renderConnections`, the loading/error JSX blocks) — this spec addresses only the filtering-logic finding raised by arch-review.
- Any change to `PositionCard`, `useOrgChart`, or backend org-chart endpoints.
- Adding new filter types (e.g., by employee, by name) — not requested, not part of this finding.

## Open Questions
None.

## Status: COMPLETE
