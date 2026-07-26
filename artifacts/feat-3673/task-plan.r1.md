# Task Plan: Move `getLevelColor` from `OrgChartPage` into `PositionCard`

### task: move-getlevelcolor-to-positioncard

## Context

This is a small, purely mechanical frontend refactor confirmed by an architecture review (`arch-review.r1.md`, Skip Design: true — no visual/UX change). `getLevelColor` is a pure `number => string` function that maps a hierarchy level to a Tailwind border-color class. It currently lives in `OrgChartPage.tsx` and is prop-drilled into `PositionCard.tsx`, its only consumer (including its own recursive self-render). It must move to live inside `PositionCard.tsx` as a private, non-exported helper, since the component that renders the border should own the logic that computes it. This is a single cohesive change — do not split it into multiple tasks.

All file/line references below were verified directly against current source (as of this plan) and match the spec and arch-review exactly.

## Files to touch

1. `frontend/src/components/OrgChart/PositionCard.tsx`
2. `frontend/src/pages/OrgChartPage.tsx`
3. `frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx`
4. `frontend/src/components/OrgChart/__tests__/__snapshots__/PositionCard.test.tsx.snap` (regenerate via test runner — do not hand-edit)

## Exact changes

### 1. `frontend/src/components/OrgChart/PositionCard.tsx`

Current relevant content:

```typescript
export interface PositionCardProps {
  position: PositionDto;
  getChildren: (parentId: string) => PositionDto[];
  getLevelColor: (level: number) => string;
}

const getInitials = (name: string | undefined): string => {
  if (!name) return '?';
  return name
    .split(' ')
    .map((n) => n[0])
    .join('')
    .toUpperCase();
};

function PositionCard({ position, getChildren, getLevelColor }: PositionCardProps): JSX.Element {
```

Change to:

- Remove `getLevelColor: (level: number) => string;` from `PositionCardProps` so it becomes:

  ```typescript
  export interface PositionCardProps {
    position: PositionDto;
    getChildren: (parentId: string) => PositionDto[];
  }
  ```

- Add a new private, non-exported module-level function immediately after the `PositionCardProps` interface (before or after `getInitials` is fine; place it directly above or below `getInitials` for consistency with that existing inline-helper pattern):

  ```typescript
  const getLevelColor = (level: number): string => {
    switch (level) {
      case 1:
        return 'border-l-4 border-red-500';
      case 2:
        return 'border-l-4 border-orange-500';
      case 3:
        return 'border-l-4 border-yellow-500';
      case 4:
        return 'border-l-4 border-green-500';
      default:
        return 'border-l-4 border-gray-500';
    }
  };
  ```

  This must be byte-for-byte identical in output to the version currently in `OrgChartPage.tsx` (same switch/case branches, same class strings). Do not change the color mapping or add an explicit return type deviation beyond what's shown (an explicit `(level: number): string =>` signature is fine and matches the spec).

- Update the component signature to drop `getLevelColor` from the destructured props:

  ```typescript
  function PositionCard({ position, getChildren }: PositionCardProps): JSX.Element {
  ```

- In the JSX (currently around line 27-29), the call site:

  ```typescript
  className={`bg-white dark:bg-graphite-surface rounded-xl shadow-lg dark:shadow-soft-dark p-6 w-80 transition-all hover:shadow-2xl hover:-translate-y-1 ${getLevelColor(
    position.level ?? 1
  )} relative mb-20`}
  ```

  stays exactly as-is — it already calls `getLevelColor(position.level ?? 1)`; only the resolution of `getLevelColor` changes from a prop to the local module-level const. Preserve the `?? 1` fallback exactly.

- In the recursive self-render (currently around line 119-124):

  ```typescript
  <PositionCard
    key={child.id}
    position={child}
    getChildren={getChildren}
    getLevelColor={getLevelColor}
  />
  ```

  remove the `getLevelColor={getLevelColor}` line so it becomes:

  ```typescript
  <PositionCard
    key={child.id}
    position={child}
    getChildren={getChildren}
  />
  ```

### 2. `frontend/src/pages/OrgChartPage.tsx`

- Delete the function definition currently at (approximately) lines 168-181:

  ```typescript
  const getLevelColor = (level: number) => {
    switch (level) {
      case 1:
        return 'border-l-4 border-red-500';
      case 2:
        return 'border-l-4 border-orange-500';
      case 3:
        return 'border-l-4 border-yellow-500';
      case 4:
        return 'border-l-4 border-green-500';
      default:
        return 'border-l-4 border-gray-500';
    }
  };
  ```

  Remove this block entirely (it sits between the `totalEmployees` calculation and the `getChildren` const definition — leave both of those untouched).

- At the `<PositionCard>` JSX call site (approximately lines 333-338):

  ```typescript
  <PositionCard
    key={root.id}
    position={root}
    getChildren={getChildren}
    getLevelColor={getLevelColor}
  />
  ```

  remove the `getLevelColor={getLevelColor}` line so it becomes:

  ```typescript
  <PositionCard
    key={root.id}
    position={root}
    getChildren={getChildren}
  />
  ```

- Do not touch anything else in this file: data fetching (`useOrgChart`), filtering logic, zoom state/handlers, connection-line rendering (`renderConnections`), and all other JSX must remain exactly as they are.

### 3. `frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx`

- Remove the stub helper (currently line 21):

  ```typescript
  const stubLevelColor = (level: number): string => `border-l-4 level-${level}`;
  ```

- Remove the `getLevelColor={stubLevelColor}` prop from both `<PositionCard ... />` invocations (currently lines 40 and 76 — one in `'renders a leaf position with data-position-id on the outer card'`, one in `'renders a recursive position with one child'`). E.g.:

  ```typescript
  const { container } = render(
    <PositionCard
      position={position}
      getChildren={noChildren}
    />,
  );
  ```

  and similarly for the second test's `parent`/`getChildren` render call. Do not change any other part of either test (assertions, `data-position-id` queries, `toMatchSnapshot()` calls, test structure/names all stay identical).

### 4. Snapshot file

- Do not hand-edit `frontend/src/components/OrgChart/__tests__/__snapshots__/PositionCard.test.tsx.snap`. After making the changes above, regenerate it by running the test suite with the update flag (from the `frontend/` directory):

  ```
  CI=true npx react-scripts test src/components/OrgChart/__tests__/PositionCard.test.tsx -u --watchAll=false
  ```

  (or equivalent `npm test -- -u` invocation that runs non-interactively and updates snapshots).

- After regeneration, manually diff the snapshot file (`git diff` on the `.snap` file) and confirm the **only** changes are the border-color class strings — e.g. class fragments like `border-l-4 level-1` / `border-l-4 level-2` / `border-l-4 level-3` (from the stub) become the real classes: `border-l-4 border-red-500` (level 1), `border-l-4 border-orange-500` (level 2), `border-l-4 border-yellow-500` (level 3), respectively, matching the two existing tests' `level: 1`, `level: 2`, `level: 3` fixture values. No other markup, structure, or text content in the snapshot should change. If anything else differs, stop and investigate before proceeding — that would indicate an unintended behavioral change, not just the expected stub-to-real-class diff.

## Out of scope (do not do)

- Do not extract `getLevelColor` into a new shared file (e.g. `levelColors.ts`).
- Do not change the color mapping, level thresholds, or any visual/design behavior.
- Do not modify any other `OrgChartPage` responsibility (data fetching, filters, zoom, connection lines).
- Do not add new tests beyond what's needed to keep the two existing tests passing with the prop removed.

## Acceptance criteria

- `PositionCardProps` in `PositionCard.tsx` contains only `position` and `getChildren` — no `getLevelColor` field.
- `PositionCard.tsx` defines a private, non-exported `getLevelColor` module-level function with identical switch/case behavior and class strings as the original (levels 1-4 → red/orange/yellow/green, all others → gray, `border-l-4` prefix on every branch).
- The `PositionCard` component body calls `getLevelColor(position.level ?? 1)` for its own border class, and no longer accepts `getLevelColor` as a prop anywhere, including its recursive self-render.
- `OrgChartPage.tsx` no longer defines or references `getLevelColor` anywhere, and its `<PositionCard>` call site passes only `position` and `getChildren`. No other `OrgChartPage` behavior changes.
- `PositionCard.test.tsx` no longer defines `stubLevelColor` and no longer passes a `getLevelColor` prop in either test; both existing test cases pass with only the stub/prop removed (no other structural changes).
- The Jest snapshot file is regenerated (not hand-edited) and, when diffed, shows only border-color class-string changes (stub `level-N` classes replaced by the real `border-{color}-500` classes) — no other markup differences.
- `npm run build` succeeds with no TypeScript errors (this will also catch any stale `getLevelColor` prop reference via a compile error, since `PositionCardProps` no longer declares it).
- `npm run lint` passes.
- The full existing test suite for `PositionCard.test.tsx` (and ideally the broader `npm test` run, or at minimum any test touching `OrgChartPage`/`PositionCard`) passes.
- A repo-wide search for `getLevelColor` confirms it now appears only inside `PositionCard.tsx` (definition + two call sites: JSX class computation and recursive self-render no longer needed) — zero references remain in `OrgChartPage.tsx` or `PositionCard.test.tsx`.

## Verification steps (run from `frontend/` directory)

1. `npm run build` — must succeed with zero TypeScript errors.
2. `npm run lint` — must pass.
3. Run the updated test with snapshot regeneration as described above, then re-run without `-u` to confirm it now passes cleanly against the regenerated snapshot:
   ```
   CI=true npx react-scripts test src/components/OrgChart/__tests__/PositionCard.test.tsx --watchAll=false
   ```
4. `git diff` the `.snap` file and confirm only the described class-string changes are present.
5. Grep the `frontend/src` tree for `getLevelColor` and confirm the only remaining occurrences are within `PositionCard.tsx`.
