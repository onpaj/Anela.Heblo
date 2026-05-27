# Architecture Review: OrgChartPage Refactor — Extract Pure Utilities and PositionCard

## Skip Design: true

## Architectural Fit Assessment

This is a pure structural refactor that aligns cleanly with existing conventions:

- **Filesystem convention** (`docs/architecture/filesystem.md`): `components/` for React components, `pages/` for page components, tests **co-located in `__tests__/` folders** — this is a hard rule the spec gets slightly wrong (see Specification Amendments).
- **TypeScript style** (`~/.claude/rules/typescript-coding-style.md`): explicit prop interfaces, no `React.FC` unless required, immutable updates — already how the rest of the codebase is written. The spec's illustrative `React.FC` example must not be carried into implementation.
- **Generated DTO** (`PositionDto` from `frontend/src/api/generated/api-client.ts` line 27548): an NSwag-generated **class** with optional fields (`id?`, `parentPositionId?`, `level?`). All extracted utilities must continue to use it directly — no re-typing.
- **DOM coupling** (`data-position-id` attribute on the card root, queried by the page's geometry `useEffect` via `containerRef`): the only contract the page requires from the extracted card. As long as `PositionCard` renders `data-position-id={position.id}` on the same outer `div`, the geometry/SVG code keeps working without any wiring changes.

Integration points are minimal: the page imports utils + `PositionCard`; everything else stays put.

## Proposed Architecture

### Component Overview

```
frontend/src/
├── pages/
│   ├── OrgChartPage.tsx                 (slimmed — fetching, filters, geometry, SVG, layout)
│   ├── orgChartUtils.ts                 (NEW — pure tree algorithms)
│   └── __tests__/
│       └── orgChartUtils.test.ts        (NEW)
└── components/
    └── OrgChart/
        ├── PositionCard.tsx             (NEW — recursive card component)
        └── __tests__/
            └── PositionCard.test.tsx    (NEW)
```

Data ownership stays in `OrgChartPage`. `PositionCard` is a leaf-of-recursion presentation component that calls a `getChildren` callback supplied by the page. `orgChartUtils` is stateless and side-effect-free.

### Key Design Decisions

#### Decision 1: How `PositionCard` traverses children

**Options considered:**
- **A. Pre-build children tree in the page**, pass each card a `children: Position[]` prop and let `PositionCard` map+recurse over it.
- **B. Pass a `getChildren: (parentId: string) => Position[]` callback prop** that closes over the page's `filteredPositions`. `PositionCard` recursively renders `PositionCard` instances by calling this on its own id.
- **C. Pass the full filtered list down** and have `PositionCard` filter on each render.

**Chosen approach:** **B** — pass `getChildren` as a callback prop.

**Rationale:** Mirrors the current closure (`renderPositionCard` calls `getChildren(position.id!)` which closes over `filteredPositions`) — preserves runtime behavior exactly, no extra work pre-computing a tree the algorithm already walks lazily. Option A duplicates traversal logic; option C leaks an array equality concern and increases re-render churn. Option B keeps `filteredPositions` ownership in the page and keeps the component purely presentational.

#### Decision 2: Signature shape for `orgChartUtils.getChildren`

**Options considered:**
- **A.** Keep the current closure shape (impossible — closure depends on page state).
- **B.** New pure signature `getChildren(parentId: string, positions: Position[]): Position[]`.

**Chosen approach:** **B**.

**Rationale:** Required to make the function pure and testable. The spec already authorizes this signature change ("signatures … must mirror the current in-file implementations … adjust only if the current code uses a different shape"). The page then binds it: `const getChildren = (id: string) => orgChartGetChildren(id, filteredPositions);` — and passes that bound function to `PositionCard`.

#### Decision 3: Type/helper migration alongside the functions

**Options considered:**
- **A.** Move `OrganizationData` interface and the `Position` type alias with `calculateLevels` into `orgChartUtils.ts`; keep `getInitials` inside `PositionCard.tsx` (only used there).
- **B.** Leave types in the page and only move function bodies.

**Chosen approach:** **A**.

**Rationale:** `OrganizationData` is consumed only by `calculateLevels`; co-locating prevents the page from re-importing a type it no longer cares about. `getInitials` (currently lines 228–235) is only used inside the recursive card render — move it as a private (non-exported) helper inside `PositionCard.tsx`. The `Position` type alias (`= PositionDto`) is duplicated in both new files since both legitimately work with `PositionDto`; the alias is one line and aids readability locally.

#### Decision 4: Component declaration style

**Options considered:**
- **A.** `export const PositionCard: React.FC<PositionCardProps> = (props) => …` (as written in spec).
- **B.** `export function PositionCard({ position, getChildren, getLevelColor }: PositionCardProps): JSX.Element { … }`.

**Chosen approach:** **B**.

**Rationale:** Project rule explicitly: "Do not use `React.FC` unless there is a specific reason to do so". A function declaration with a destructured prop parameter is the established style in this codebase. The spec's `React.FC` is illustrative only.

#### Decision 5: Memoization

**Options considered:**
- **A.** Wrap `PositionCard` in `React.memo`.
- **B.** No memoization.

**Chosen approach:** **B**.

**Rationale:** NFR-1 explicitly says no perf regression and no memoization beyond what's needed. The current inline `renderPositionCard` re-renders the entire tree whenever the page renders (no `useMemo`/`memo`). Extracting it into a function component with the same re-render frequency is identical. `React.memo` would actually *change* behavior since `getChildren` is a freshly-created bound function each render and would always invalidate memo — adding cost. Skip it.

## Implementation Guidance

### Directory / Module Structure

```
frontend/src/pages/orgChartUtils.ts                       (NEW)
frontend/src/pages/__tests__/orgChartUtils.test.ts        (NEW)
frontend/src/components/OrgChart/PositionCard.tsx         (NEW)
frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx  (NEW)
frontend/src/pages/OrgChartPage.tsx                       (MODIFY)
```

Co-located `__tests__/` is the project standard (see `docs/architecture/filesystem.md` lines 54–55, 188–191).

### Interfaces and Contracts

**`frontend/src/pages/orgChartUtils.ts`**

```typescript
import { PositionDto } from '../api/generated/api-client';

export type Position = PositionDto;

export interface OrganizationData {
  organization: {
    name: string;
    positions: Position[];
  };
}

export function calculateLevels(data: OrganizationData): OrganizationData;

export function getAllParentPositionIds(
  positionId: string,
  allPositions: Position[],
): Set<string>;

export function buildTree(positions: Position[]): Position[];

export function getChildren(
  parentId: string,
  positions: Position[],
): Position[];
```

Bodies move verbatim from the page (line ranges listed in spec FR-1), with the single allowed adjustment to `getChildren`'s signature (Decision 2). `calculateLevels` retains its inner `getLevel` recursion and cycle guard.

**`frontend/src/components/OrgChart/PositionCard.tsx`**

```typescript
import { PositionDto } from '../../api/generated/api-client';

export interface PositionCardProps {
  position: PositionDto;
  getChildren: (parentId: string) => PositionDto[];
  getLevelColor: (level: number) => string;
}

export function PositionCard({
  position,
  getChildren,
  getLevelColor,
}: PositionCardProps): JSX.Element;

export default PositionCard;
```

The recursive `children.length > 0 && (<div>…<PositionCard … /></div>)` block stays inside the component. `getInitials` is a file-private helper. The outer card `<div>` must keep `data-position-id={position.id}` exactly — the page's geometry code depends on it.

### Data Flow

For the main render path:

```
OrgChartPage
  ├─ useOrgChart() → orgChartResponse
  ├─ useMemo: orgData = calculateLevels(transform(orgChartResponse))     [utils]
  ├─ filteredPositions = (department/level filter using getAllParentPositionIds)  [utils]
  ├─ const boundGetChildren = (id) => getChildren(id, filteredPositions)         [utils + page closure]
  ├─ buildTree(filteredPositions)                                                 [utils]
  │     .map(root => <PositionCard
  │         position={root}
  │         getChildren={boundGetChildren}
  │         getLevelColor={getLevelColor} />)
  ├─ useEffect (geometry): queries [data-position-id] in containerRef → positionRects
  └─ renderConnections(): draws SVG between positionRects (unchanged)
```

`PositionCard` recurses internally:

```
PositionCard({position, getChildren, getLevelColor})
  ├─ render card div with data-position-id={position.id}
  ├─ render employees list (uses local getInitials)
  └─ const children = getChildren(position.id!)
     children.length > 0 && children.map(c =>
       <PositionCard
         key={c.id}
         position={c}
         getChildren={getChildren}
         getLevelColor={getLevelColor} />)
```

The page's geometry/SVG logic is **unaffected** because (a) `data-position-id` is preserved on the card root, (b) the DOM tree shape is identical, (c) `containerRef` still wraps the same outer container.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Moving `getChildren` from closure to a bound prop changes referential identity each render; if anything downstream memoized on it, behavior could shift. | Low | No existing memoization closes over `getChildren`. NFR-1 forbids adding `React.memo`. Verify after refactor by smoke-testing filter changes (expected: identical re-render behavior). |
| `data-position-id` accidentally dropped or renamed during extraction breaks SVG connectors silently (lines render to wrong coordinates / not at all). | High | Treat the `data-position-id={position.id}` attribute as a contract. Reviewer must confirm it appears on the outer card `<div>` in `PositionCard.tsx`. Snapshot test (FR-5) will catch removal. |
| `PositionDto` is a generated class with optional fields (`id?`, `parentPositionId?`, `level?`); util tests must construct instances correctly or the type system will reject literal mocks. | Medium | In tests, construct via `new PositionDto({...})` or use `as PositionDto` casts on partial literals — matches existing patterns elsewhere. Pure functions don't need full DTO fidelity; minimal field subsets suffice. |
| `calculateLevels` cycle-detection branch logs `console.error` (line 41) — project rule discourages `console.log`/`console.error` in production code. | Low | Out of scope: the spec mandates verbatim move. Mention in PR description but do not change behavior. |
| Test file path mismatch (spec puts tests next to source, project convention puts them in `__tests__/`). | Medium | See Specification Amendments — place tests in `__tests__/` folders. |
| Recursive `PositionCard` calling `PositionCard` requires the function name in scope before its definition — works fine with `function` declarations (hoisted), but breaks if implemented as a `const = () =>`. | Low | Use a `function` declaration (Decision 4) — self-reference works naturally. |

## Specification Amendments

1. **Test file locations (FR-4, FR-5):** Per `docs/architecture/filesystem.md` (lines 54–55, 188–191), tests live in co-located `__tests__/` folders, not adjacent to source files.
   - Change `frontend/src/pages/orgChartUtils.test.ts` → **`frontend/src/pages/__tests__/orgChartUtils.test.ts`**.
   - Change `frontend/src/components/OrgChart/PositionCard.test.tsx` → **`frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx`**.

2. **Component declaration style (FR-2 example):** The illustrative `export const PositionCard: React.FC<PositionCardProps>` in the spec contradicts the project's TypeScript style rule ("Do not use `React.FC` unless there is a specific reason"). Implement as a named function declaration with a destructured prop parameter (see Decision 4). The exported `interface PositionCardProps` requirement stands.

3. **Prop shape for `PositionCard` (FR-2):** The spec leaves the prop shape "architect's call". Final shape (Decision 1):
   ```typescript
   interface PositionCardProps {
     position: PositionDto;
     getChildren: (parentId: string) => PositionDto[];
     getLevelColor: (level: number) => string;
   }
   ```
   No `children` prop. No filter/expanded/handler props — `renderPositionCard` does not currently read any such state from page scope; the only closure dependencies are `getChildren` (page-bound) and `getLevelColor` (already inlined as a page helper).

4. **Types co-located with `calculateLevels` (FR-1):** Move the `OrganizationData` interface and the `type Position = PositionDto` alias into `orgChartUtils.ts` and export them. The page imports `OrganizationData` from utils (it still needs it for the `useMemo` transform).

5. **`getInitials` helper:** The spec lists six "concerns" but doesn't mention `getInitials` (page lines 228–235). It is used **only** inside `renderPositionCard`. Move it into `PositionCard.tsx` as a file-private helper (not exported, not a prop). Mention this explicitly in the implementation plan so it isn't left orphaned in the page.

6. **`getChildren` signature change is mandatory, not optional:** The spec hedges ("adjust the signatures … only if the current code uses a different shape"). It does — current is `(parentId: string) => Position[]` closing over `filteredPositions`. The new signature `(parentId: string, positions: Position[]): Position[]` is **required** for purity; record this as a definite decision, not an "if needed".

## Prerequisites

None. This refactor:

- Adds no runtime or dev dependencies.
- Requires no migrations, config changes, or infrastructure work.
- Operates entirely within `frontend/src/`.
- Has no API-client regeneration impact (no DTO changes).
- Has no backend touch points.

Implementation can begin immediately.