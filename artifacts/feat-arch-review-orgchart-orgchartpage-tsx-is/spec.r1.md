# Specification: OrgChartPage Refactor — Extract Pure Utilities and PositionCard

## Summary
Refactor `frontend/src/pages/OrgChartPage.tsx` (currently 512 lines mixing six distinct concerns) by extracting four pure tree-algorithm functions into a new `orgChartUtils.ts` module and lifting the recursive `renderPositionCard` render function into a standalone `PositionCard.tsx` component. The page component will shrink to ~150 lines focused on data fetching, filter state, and layout wiring. No behavioral changes — pure structural refactor enabling unit testability and easier maintenance.

## Background
`OrgChartPage.tsx` violates the Single Responsibility Principle by combining hierarchy calculation, parent-chain traversal, filter logic, tree root detection, DOM geometry measurement, SVG line rendering, and card rendering in a single 512-line file. The tree algorithm functions (`calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren`) are pure — they operate on plain data with deterministic outputs and have no React/hook dependencies — yet they cannot be unit-tested without mounting the full page. The 100-line recursive `renderPositionCard` cannot be snapshot-tested or reused. This refactor was flagged by the daily arch-review routine on 2026-05-19 as a targeted extraction (no architectural overhaul) to restore SRP and enable focused testing.

## Functional Requirements

### FR-1: Extract pure tree-algorithm functions to `orgChartUtils.ts`
Move the following four functions verbatim from `frontend/src/pages/OrgChartPage.tsx` into a new file `frontend/src/pages/orgChartUtils.ts`:

- `calculateLevels` (currently lines 36–65) — computes hierarchy level for each position
- `getAllParentPositionIds` (currently lines 161–175) — traverses parent chain
- `buildTree` (currently lines 238–253, root detection portion) — identifies root nodes
- `getChildren` (currently lines 238–253, child lookup portion) — returns children of a given position

These functions have no React, hook, JSX, or DOM dependencies. They must move verbatim — no signature changes, no logic changes.

**Acceptance criteria:**
- New file `frontend/src/pages/orgChartUtils.ts` exists and exports all four functions.
- All four functions are removed from `OrgChartPage.tsx`.
- `OrgChartPage.tsx` imports the functions from `./orgChartUtils`.
- Function signatures, parameter names, and return types are identical to the originals.
- Function bodies are byte-identical (or differ only in trivially required import statements).
- TypeScript types used by these functions (e.g. position DTO type) are imported, not duplicated.
- `npm run build` succeeds without new warnings.
- `npm run lint` succeeds without new warnings.

### FR-2: Extract `renderPositionCard` into a standalone `PositionCard.tsx` component
Lift the recursive `renderPositionCard` function (currently lines 256–358) out of `OrgChartPage.tsx` into a new file `frontend/src/components/OrgChart/PositionCard.tsx`.

The new component:
- Accepts `position`, `children`, and `getLevelColor` as props (matching the closure variables the current function uses).
- Preserves the existing recursive rendering structure — `PositionCard` may render `PositionCard` instances for its children, or accept a pre-rendered `children` ReactNode (architect's call based on what the current code closes over).
- Carries over all existing JSX, class names, styles, and event handlers without modification.

**Acceptance criteria:**
- New file `frontend/src/components/OrgChart/PositionCard.tsx` exists and default-exports the `PositionCard` component.
- `renderPositionCard` is removed from `OrgChartPage.tsx`.
- `OrgChartPage.tsx` imports and uses `PositionCard` where `renderPositionCard` was previously invoked.
- Visual output of the org chart is pixel-identical to pre-refactor (verified manually in browser).
- Filter, expand/collapse, hover, and click behaviors are unchanged.
- TypeScript prop types are explicit and exported alongside the component.
- `npm run build` succeeds without new warnings.
- `npm run lint` succeeds without new warnings.

### FR-3: Page component remains functionally identical
After both extractions, `OrgChartPage.tsx` must:
- Continue to handle data fetching (API hooks), filter state, DOM geometry measurement (`getElementPosition` + `useEffect`), SVG line rendering (`renderConnections`), and overall layout wiring.
- Produce identical UI behavior to the pre-refactor version.
- Shrink to approximately 150 lines (target; not a hard ceiling — the goal is focused responsibility, not a line-count gate).

**Acceptance criteria:**
- All existing user-visible features of the org chart page work identically: hierarchy display, filtering, parent-chain highlighting, SVG connectors, card rendering, level coloring.
- No new props are required by `OrgChartPage` from its callers.
- The page route and any consumers of the page are unchanged.

### FR-4: Unit tests for extracted pure functions
Add unit tests for the four extracted functions in `frontend/src/pages/orgChartUtils.test.ts` (or the project's standard test file location adjacent to the source).

**Acceptance criteria:**
- Each of `calculateLevels`, `getAllParentPositionIds`, `buildTree`, and `getChildren` has at least one happy-path test and at least one edge-case test (empty input, single node, orphaned node, or cycle if applicable — match what the function actually handles).
- Tests follow the Arrange-Act-Assert pattern.
- Tests do not mount React components.
- Tests pass under the project's existing test runner (Jest/Vitest as configured).
- Coverage for the new utils file is ≥ 80%.

### FR-5: Snapshot test for `PositionCard`
Add a snapshot test for `PositionCard.tsx` in `frontend/src/components/OrgChart/PositionCard.test.tsx`.

**Acceptance criteria:**
- One snapshot test renders `PositionCard` in isolation with a representative position prop.
- One additional test verifies the recursive case (a position with at least one child).
- Tests pass under the project's existing test runner.

## Non-Functional Requirements

### NFR-1: Performance
No performance regression. The refactor is structural; rendered output and runtime complexity must be identical. If `PositionCard` is extracted as a function component, it should not introduce additional re-renders compared to the inline render function. If memoization (`React.memo`) is needed to preserve render behavior, apply it minimally and document why.

### NFR-2: Security
No security implications. No data flow, auth, or input handling changes.

### NFR-3: Maintainability
- New utility file should be < 200 lines.
- New `PositionCard.tsx` should be < 200 lines.
- `OrgChartPage.tsx` post-refactor target: ~150 lines, hard ceiling 300 lines.
- All new files must have TypeScript types explicitly declared on exports.

### NFR-4: Backward compatibility
Zero breaking changes. The page's external contract (route, props, behavior) is unchanged.

## Data Model
No data model changes. The refactor operates on the existing position DTO type already in use by `OrgChartPage.tsx`. The position type should be imported by both `orgChartUtils.ts` and `PositionCard.tsx` from its current location (do not duplicate the type definition).

## API / Interface Design

### Module boundaries

**`frontend/src/pages/orgChartUtils.ts`** — pure functions:
- `calculateLevels(positions: Position[]): Map<string, number>` (signature must match current implementation)
- `getAllParentPositionIds(positionId: string, positions: Position[]): string[]` (signature must match current implementation)
- `buildTree(positions: Position[]): Position[]` (returns roots — signature must match current implementation)
- `getChildren(positionId: string, positions: Position[]): Position[]` (signature must match current implementation)

Note: exact signatures and parameter names must mirror the current in-file implementations. The architect/implementer should adjust the signatures above only if the current code uses a different shape.

**`frontend/src/components/OrgChart/PositionCard.tsx`** — sub-component:
```typescript
export interface PositionCardProps {
  position: Position;
  children?: React.ReactNode;  // or Position[], depending on current closure structure
  getLevelColor: (level: number) => string;  // signature mirrors current page-level helper
}

export const PositionCard: React.FC<PositionCardProps> = (props) => { ... };
```

The implementer must inspect the current `renderPositionCard` closure to determine the precise prop shape — the props above are illustrative. Any additional values that `renderPositionCard` reads from the page's scope (filter state, expanded state, click handlers) must become props.

### File placement rationale
- `orgChartUtils.ts` lives next to the page (`frontend/src/pages/`) because it is page-specific logic, not a shared utility. Promotion to a shared `utils/` directory is out of scope.
- `PositionCard.tsx` lives in `frontend/src/components/OrgChart/` to establish a future home for any further OrgChart sub-components (per the brief's suggested path).

## Dependencies
- No new runtime dependencies.
- No new dev dependencies (uses existing test runner, TypeScript, ESLint).
- Depends on the existing position DTO type already imported by `OrgChartPage.tsx`.

## Out of Scope
- Refactoring `getElementPosition`, the geometry `useEffect`, or `renderConnections` — these remain in `OrgChartPage.tsx`.
- Splitting the filter logic (`filteredPositions` IIFE) into a separate hook or module.
- Adding new filter types, card styles, or org chart features.
- Promoting `orgChartUtils.ts` to a shared utilities directory.
- Memoization or performance tuning beyond what is needed to preserve current behavior.
- Migrating to a different rendering approach (e.g., virtualization, canvas).
- Storybook stories for `PositionCard` (snapshot test is sufficient for this pass).
- E2E test changes — existing E2E coverage of the org chart page must continue to pass unchanged.

## Open Questions
None.

## Status: COMPLETE