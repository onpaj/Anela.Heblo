# Architecture Review: Extract OrgChart Position Filtering Logic

## Skip Design: true

No new or changed UI components, screens, layouts, or visual behavior. Verified against both the spec (explicitly: "Out of Scope... this is a logic-location change only, not a UI change") and the current `OrgChartPage.tsx`: the two `<select>` controls, the reset button, zoom controls, and all rendered markup are untouched. This is a pure internal refactor — code motion of an existing inline IIFE into a named, exported function in an already-existing sibling module, plus swapping a plain expression for a `useMemo` call. No design review is warranted.

## Architectural Fit Assessment

This fits the codebase's established convention precisely — it does not introduce one. `frontend/src/pages/orgChartUtils.ts` already exists as the pure-logic module for this page, already hosts four sibling helpers (`calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren`) that follow exactly this shape (pure functions over `Position[]`, no React, no I/O), and is already exercised by a dedicated, colocated test file (`frontend/src/pages/__tests__/orgChartUtils.test.ts`) using an established `makePosition`/Arrange-Act-Assert style. The filtering IIFE at `OrgChartPage.tsx:133-164` is simply the one piece of this same logical family that was never moved out — it directly calls `getAllParentPositionIds` from the same module, so its natural home is obvious and uncontested.

This also aligns with the project's general separation-of-concerns rule (`docs/architecture/development_guidelines.md`: business logic does not belong inlined in the presentation layer — the doc states this for MediatR controllers on the backend, and the same principle is being applied here on the frontend, consistent with how the other four helpers were already separated).

Integration points are narrow and fully enumerated in the spec:
- `orgChartUtils.ts` gains one exported function; no existing export's behavior changes.
- `OrgChartPage.tsx` changes only the `filteredPositions` computation and its position within the component body (must move above the early returns — see Decision 2).
- The test file gains one new `describe` block; no existing test changes.

No other component, hook, or backend contract is touched. `PositionCard`, `useOrgChart`, and the OpenAPI-generated `PositionDto` are all unchanged per the spec's explicit "Out of Scope" and "Dependencies" sections, which I have no reason to dispute after reading the source.

## Proposed Architecture

### Component Overview

```
OrgChartPage.tsx (container/presentation)
  ├─ useOrgChart()                    [unchanged — data fetching]
  ├─ useMemo → orgData                [unchanged — calculateLevels(...)]
  ├─ useMemo → filteredPositions      [MOVED UP, now calls filterPositions()]
  │     depends on [orgData, filters]
  ├─ early returns (isLoading / queryError / !orgData)   [unchanged, now AFTER both memos]
  ├─ getChildren, renderConnections, JSX               [unchanged, read filteredPositions]
  └─ imports filterPositions ─────────────┐
                                           ▼
orgChartUtils.ts (pure logic module)
  ├─ calculateLevels()            [unchanged]
  ├─ getAllParentPositionIds()    [unchanged — reused internally by filterPositions]
  ├─ buildTree()                  [unchanged]
  ├─ getChildren()                [unchanged]
  └─ filterPositions()            [NEW — department + level filtering]

__tests__/orgChartUtils.test.ts (unit tests, no React rendering needed)
  └─ describe('filterPositions', ...)  [NEW]
```

No new modules, no new files beyond what's already named in the brief/spec. No change to the module's public surface for `calculateLevels`, `getAllParentPositionIds`, `buildTree`, or `getChildren`.

### Key Design Decisions

#### Decision 1: Function signature and placement — match the brief's `filterPositions`, not a generic "filter service"

**Options considered:**
- (a) A single pure function `filterPositions(positions, departmentFilter, levelFilter): Position[]` in `orgChartUtils.ts`, mirroring the sibling helpers.
- (b) A more "generic" filter abstraction (e.g. a list of predicate objects, a `FilterCriteria` type, a small filter-combinator utility) to anticipate future filter types.

**Chosen approach:** (a), exactly as specified in the brief and spec.

**Rationale:** The spec explicitly scopes out "adding new filter types" as future work requiring its own decision. Building a generic predicate/combinator abstraction now for two hardcoded filters is speculative generality this task should not introduce — it would also change the semantics under test (sequential narrowing vs. independent union) in ways the spec is careful to freeze. Two-argument, two-branch pure function matching existing sibling helpers is the right amount of structure. If a third filter dimension is added later, that is the moment to reconsider the shape — not now.

#### Decision 2: Where `useMemo` goes — must move above the early returns

**Options considered:**
- (a) Leave the filtering computation where the IIFE currently sits (after the `isLoading`/`queryError`/`!orgData` early returns) and just wrap it with `useMemo`.
- (b) Move the `useMemo` call up to sit alongside the existing `orgData` `useMemo` (lines 40-51), before all three early returns, with an explicit `orgData` null-guard inside the memo callback.

**Chosen approach:** (b).

**Rationale:** Option (a) is not legal — a `useMemo` call cannot be placed after a conditional `return` (`isLoading`/`queryError`/`!orgData` all return early). React's Rules of Hooks require every hook call to execute in the same order on every render; placing `useMemo` after those returns would make the hook conditionally-called (skipped when the component returns early), which is exactly what the rule forbids and what `eslint-plugin-react-hooks` (already presumably enforced in this project's lint config, given `npm run lint` is a required gate) would flag. This is not a style preference; it is a correctness requirement the brief's own suggested fix overlooks and the spec correctly calls out. The colocated placement next to the `orgData` memo is also the most readable spot — both memos derive from the same `orgChartResponse`/`orgData` lineage.

Verified in the current source (`OrgChartPage.tsx:106-128` for the early returns, `:40-51` for the existing `orgData` memo) that this reordering is required and that no other logic sits between the two memo positions that would be affected by moving the block up.

#### Decision 3: `useMemo` dependency array — `[orgData, filters]`, not `[orgData, filters.department, filters.level]`

**Options considered:**
- (a) `[orgData, filters]` (object reference).
- (b) `[orgData, filters.department, filters.level]` (primitive fields).

**Chosen approach:** (a), per the spec.

**Rationale:** `filters` is only ever replaced wholesale via `setFilters({...})` calls (`onChange` handlers and the reset button all construct a brand-new object), never mutated in place — so `filters` as a single dependency is referentially stable exactly when its fields are unchanged, and changes exactly when either field changes. This matches current behavior 1:1 (the IIFE re-evaluates whenever the component re-renders due to a `filters` state update) while eliminating the recompute on zoom-only or positionRect-only re-renders, which is the actual performance win this refactor claims. Destructuring to primitive deps would be marginally more defensive against a future mutating `setFilters` call, but that would be inventing a safeguard against a bug that doesn't exist in this file today — not part of this refactor's scope.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Two existing files change:
- `frontend/src/pages/orgChartUtils.ts` — add `export function filterPositions(...)`, placed after `getAllParentPositionIds` (its dependency) for readability, before or after `buildTree`/`getChildren` — ordering among the four/five exports is not load-bearing, keep them together.
- `frontend/src/pages/OrgChartPage.tsx` — remove the inline IIFE block (current lines 133-164); add `filterPositions` to the existing `orgChartUtils` import; insert the new `useMemo` call above the early-return block (natural spot: immediately after the existing `orgData` `useMemo`, before `getElementPosition`).
- `frontend/src/pages/__tests__/orgChartUtils.test.ts` — add one new `describe('filterPositions', ...)` block, following the existing `makePosition`/AAA-comment conventions already in the file.

### Interfaces and Contracts

```ts
// frontend/src/pages/orgChartUtils.ts
export function filterPositions(
  positions: Position[],
  departmentFilter: string,
  levelFilter: string,
): Position[]
```

Contract (pure function, no side effects, must not mutate `positions` or its elements):
1. `departmentFilter === 'all'` → skip department narrowing entirely.
2. Else → build the ancestor-inclusive set using `getAllParentPositionIds(id, positions)` against the **original, unfiltered** `positions` array (not the department-matched subset) for every position whose `department === departmentFilter`, union with the direct department matches, then filter `positions` down to that set.
3. `levelFilter === 'all'` → skip level narrowing.
4. Else → filter the *result of step 2* (not the original `positions`) by `!p.level || p.level <= parseInt(levelFilter, 10)`. The `!p.level` short-circuit (an undefined *or zero* level always passes) is existing, intentional-per-spec behavior — do not "fix" it in this change.
5. Filters compose via sequential narrowing (department first, then level on the already-narrowed set) — not independent union. This is the one subtlety a reviewer could get wrong; it's why the spec calls for a dedicated combined-filter test case with an ancestor that gets excluded by the level cutoff.

Call site contract in `OrgChartPage.tsx`:

```ts
const filteredPositions = useMemo(
  () => (orgData ? filterPositions(orgData.organization.positions, filters.department, filters.level) : []),
  [orgData, filters],
);
```

Positioned directly after the existing `orgData` `useMemo` block, before `getElementPosition` and before the `isLoading`/`queryError`/`!orgData` early returns.

Remove the `getAllParentPositionIds` import from `OrgChartPage.tsx` only after confirming (build/lint) it has no other use in the file — a scan of the current file shows its only use is inside the IIFE being deleted, so it should be removed, but this must be confirmed post-edit rather than assumed, per the spec.

### Data Flow

1. `useOrgChart()` fetches raw data → `orgChartResponse`.
2. `orgData` memo transforms + runs `calculateLevels` → `OrganizationData | null`.
3. **(new position)** `filteredPositions` memo runs `filterPositions(orgData.organization.positions, filters.department, filters.level)` whenever `orgData` or `filters` changes; returns `[]` while `orgData` is still `null`.
4. Early returns handle loading/error/no-data states (unaffected by the reordering — they still return before any JSX that reads `filteredPositions`).
5. Render body reads `filteredPositions` for `totalEmployees`, `getChildren`, `renderConnections`, `buildTree(filteredPositions)`, and the position-count stat tile — all unchanged call sites, now fed by a memoized value instead of a per-render recomputation.
6. The `positionRects` `useEffect` (lines 70-104) keeps its own `[orgData, filters, zoom]` dependency array unchanged, per spec — it is not wired to `filteredPositions` and that is explicitly out of scope for this fix.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hoisting the `useMemo` above the early returns is done carelessly (e.g. forgetting the `orgData ? ... : []` guard), causing a runtime crash on `orgData.organization.positions` when `orgData` is `null` during the loading state | High | Spec's acceptance criteria explicitly require the null-guarded form; a build/typecheck (`orgData.organization.positions` on a possibly-null `orgData`) will also fail to compile without the guard, since TS will flag the null access — this is a natural compile-time backstop |
| Silent behavior drift in the department+level composition (independent union instead of sequential narrowing) | Medium | Spec's FR-2 acceptance criteria mandate a test case specifically for an ancestor retained-by-department but excluded-by-level, which pins the composition order; write that test first (TDD) so a regression is caught immediately |
| `getAllParentPositionIds` import left dangling (unused) in `OrgChartPage.tsx` after extraction, or removed while still needed elsewhere in the file | Low | `npm run lint` (already a required validation gate per project rules) will flag an unused import; a full-file read before deleting confirms no other call site — already verified in this review, single use at line 146 |
| `parseInt(filters.level)` → `parseInt(levelFilter, 10)` radix change alters behavior for some unexpected filter value | Low | Spec confirms values are always `'1'`-`'4'` or `'all'`, decimal, no leading zeros — adding an explicit radix is a documented no-op for this domain; no mitigation needed beyond the existing unit tests covering `'1'`-`'4'` |
| Byte-for-byte behavioral parity claim (spec's "output... must be byte-identical to the pre-refactor IIFE") not actually verified | Medium | The new unit tests substitute for this since the old IIFE was never independently testable; additionally, a manual smoke check of the OrgChart page in the browser (all four filter combinations) before merge closes the gap the unit tests can't reach (they don't exercise `buildTree`/rendering integration) |

## Specification Amendments

None required. The spec is unusually precise and already resolves the one real technical risk (Rules-of-Hooks placement) that the brief's suggested fix glossed over. One clarifying note for the implementer, not a spec change: place the new `filterPositions` export in `orgChartUtils.ts` near `getAllParentPositionIds` (its dependency) for local readability — the spec doesn't mandate ordering and this is a non-binding suggestion, not a requirement.

## Prerequisites

None. No new dependencies, no config, no migrations, no infrastructure changes, no feature flag. This can start immediately as a self-contained frontend-only change. Standard validation gates apply before completion: `npm run build`, `npm run lint`, and the touched unit test file passing (`orgChartUtils.test.ts`); no backend build/tests are affected since no C# code, DTO, or OpenAPI contract changes.
