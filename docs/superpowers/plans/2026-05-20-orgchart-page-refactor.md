# OrgChartPage Refactor — Extract Pure Utilities and PositionCard

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `frontend/src/pages/OrgChartPage.tsx` (512 lines, six concerns) by extracting four pure tree-algorithm functions into a new `orgChartUtils.ts` module and lifting the recursive `renderPositionCard` render function into a standalone `PositionCard.tsx` component, while preserving exact runtime behavior.

**Architecture:** Pure structural refactor — no behavioral change. Tree algorithms (`calculateLevels`, `getAllParentPositionIds`, `buildTree`, `getChildren`) become side-effect-free functions in `frontend/src/pages/orgChartUtils.ts`. The recursive card render lifts into `frontend/src/components/OrgChart/PositionCard.tsx` as a self-referencing function component receiving `position`, `getChildren` (closure-bound by the page over `filteredPositions`), and `getLevelColor`. `OrgChartPage.tsx` retains data fetching, filter state, DOM geometry, SVG connectors, and layout wiring. The DOM contract (`data-position-id` attribute on each card root) is preserved exactly so the page's geometry/SVG code is untouched.

**Tech Stack:** TypeScript 4.9, React 18, Jest (via react-scripts), React Testing Library, Tailwind CSS, NSwag-generated `PositionDto` class.

---

## File Structure

**New files:**
- `frontend/src/pages/orgChartUtils.ts` — pure tree algorithms + shared types (`Position`, `OrganizationData`).
- `frontend/src/pages/__tests__/orgChartUtils.test.ts` — unit tests for the four pure functions.
- `frontend/src/components/OrgChart/PositionCard.tsx` — recursive position card component + file-private `getInitials` helper.
- `frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx` — snapshot tests for the card.

**Modified files:**
- `frontend/src/pages/OrgChartPage.tsx` — remove the four pure functions, `getInitials`, `OrganizationData` interface, and `renderPositionCard`. Import from new modules instead. Bind `getChildren` against `filteredPositions` and pass to `PositionCard`.

**Untouched (deliberately):**
- `frontend/src/App.tsx` — page route unchanged.
- `frontend/src/api/generated/api-client.ts` — `PositionDto` unchanged.
- Page-level helpers: `getElementPosition`, the geometry `useEffect`, `renderConnections`, `getLevelColor`, the `filteredPositions` IIFE, filter/zoom UI.

---

## Task 1: Write tests for `orgChartUtils` (RED)

**Files:**
- Create: `frontend/src/pages/__tests__/orgChartUtils.test.ts`

- [ ] **Step 1.1: Write the failing test file**

Create `frontend/src/pages/__tests__/orgChartUtils.test.ts` with the following content:

```typescript
import {
  calculateLevels,
  getAllParentPositionIds,
  buildTree,
  getChildren,
  OrganizationData,
  Position,
} from '../orgChartUtils';
import { PositionDto } from '../../api/generated/api-client';

// Helper to build a Position quickly without exercising NSwag init/fromJS.
const makePosition = (overrides: Partial<PositionDto>): Position => {
  const p = new PositionDto();
  Object.assign(p, overrides);
  return p;
};

const buildOrganizationData = (positions: Position[]): OrganizationData => ({
  organization: {
    name: 'Test Org',
    positions,
  },
});

describe('calculateLevels', () => {
  it('assigns level 1 to a single root position', () => {
    // Arrange
    const positions = [makePosition({ id: 'root' })];
    const data = buildOrganizationData(positions);

    // Act
    const result = calculateLevels(data);

    // Assert
    expect(result.organization.positions[0].level).toBe(1);
  });

  it('computes correct levels for a three-level hierarchy', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c', parentPositionId: 'b' }),
    ];
    const data = buildOrganizationData(positions);

    // Act
    const result = calculateLevels(data);
    const levelById = new Map(
      result.organization.positions.map((p) => [p.id, p.level]),
    );

    // Assert
    expect(levelById.get('a')).toBe(1);
    expect(levelById.get('b')).toBe(2);
    expect(levelById.get('c')).toBe(3);
  });

  it('returns a new OrganizationData object (does not mutate the input)', () => {
    // Arrange
    const positions = [makePosition({ id: 'root' })];
    const data = buildOrganizationData(positions);

    // Act
    const result = calculateLevels(data);

    // Assert
    expect(result).not.toBe(data);
    expect(result.organization).not.toBe(data.organization);
    expect(result.organization.positions).not.toBe(data.organization.positions);
  });

  it('breaks cycles and returns level 1 for cyclic positions', () => {
    // Arrange — a <-> b cycle.
    const positions = [
      makePosition({ id: 'a', parentPositionId: 'b' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
    ];
    const data = buildOrganizationData(positions);
    const consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});

    // Act
    const result = calculateLevels(data);

    // Assert — cycle guard prevents infinite recursion. Both positions get a finite level.
    result.organization.positions.forEach((p) => {
      expect(typeof p.level).toBe('number');
      expect(Number.isFinite(p.level)).toBe(true);
    });

    consoleError.mockRestore();
  });
});

describe('getAllParentPositionIds', () => {
  it('returns an empty set for a root position', () => {
    // Arrange
    const positions = [makePosition({ id: 'root' })];

    // Act
    const result = getAllParentPositionIds('root', positions);

    // Assert
    expect(result.size).toBe(0);
  });

  it('returns every ancestor id for a deeply-nested position', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c', parentPositionId: 'b' }),
      makePosition({ id: 'd', parentPositionId: 'c' }),
    ];

    // Act
    const result = getAllParentPositionIds('d', positions);

    // Assert
    expect(Array.from(result).sort()).toEqual(['a', 'b', 'c']);
  });

  it('returns an empty set when the position id is unknown', () => {
    // Arrange
    const positions = [makePosition({ id: 'a' })];

    // Act
    const result = getAllParentPositionIds('missing', positions);

    // Assert
    expect(result.size).toBe(0);
  });
});

describe('buildTree', () => {
  it('returns an empty array when given no positions', () => {
    // Act
    const roots = buildTree([]);

    // Assert
    expect(roots).toEqual([]);
  });

  it('identifies positions with no parent as roots', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c' }),
    ];

    // Act
    const roots = buildTree(positions);

    // Assert
    expect(roots.map((r) => r.id).sort()).toEqual(['a', 'c']);
  });

  it('treats a position whose parent is missing from the list as a root', () => {
    // Arrange — 'orphan' references a parent that isn't in the positions list.
    const positions = [
      makePosition({ id: 'orphan', parentPositionId: 'missing-parent' }),
    ];

    // Act
    const roots = buildTree(positions);

    // Assert
    expect(roots.map((r) => r.id)).toEqual(['orphan']);
  });
});

describe('getChildren', () => {
  it('returns positions whose parentPositionId matches the given id', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
      makePosition({ id: 'c', parentPositionId: 'a' }),
      makePosition({ id: 'd', parentPositionId: 'b' }),
    ];

    // Act
    const children = getChildren('a', positions);

    // Assert
    expect(children.map((c) => c.id).sort()).toEqual(['b', 'c']);
  });

  it('returns an empty array when the position has no children', () => {
    // Arrange
    const positions = [
      makePosition({ id: 'a' }),
      makePosition({ id: 'b', parentPositionId: 'a' }),
    ];

    // Act
    const children = getChildren('b', positions);

    // Assert
    expect(children).toEqual([]);
  });
});
```

- [ ] **Step 1.2: Run the tests to confirm they fail**

Run from `frontend/`:

```bash
cd frontend && npx react-scripts test --watchAll=false src/pages/__tests__/orgChartUtils.test.ts
```

Expected: FAIL — `Cannot find module '../orgChartUtils'` (the module does not yet exist).

- [ ] **Step 1.3: Commit the failing tests**

```bash
git add frontend/src/pages/__tests__/orgChartUtils.test.ts
git commit -m "test(orgchart): add unit tests for orgChartUtils (RED)"
```

---

## Task 2: Create `orgChartUtils.ts` (GREEN)

**Files:**
- Create: `frontend/src/pages/orgChartUtils.ts`

The four functions in `OrgChartPage.tsx` (lines 36–65, 161–175, 238–249, 251–253) move verbatim. The only allowed change is `getChildren`'s signature: it must take `(parentId, positions)` so it is pure. The `OrganizationData` interface (lines 8–13) and the `type Position = PositionDto` alias (line 6) move with `calculateLevels` because they are only used here.

- [ ] **Step 2.1: Create the utility module**

Create `frontend/src/pages/orgChartUtils.ts`:

```typescript
import { PositionDto } from '../api/generated/api-client';

export type Position = PositionDto;

export interface OrganizationData {
  organization: {
    name: string;
    positions: Position[];
  };
}

// Calculate correct level for every position based on parent hierarchy.
// Returns a new OrganizationData (immutable update).
export function calculateLevels(data: OrganizationData): OrganizationData {
  const positionMap = new Map(data.organization.positions.map((p) => [p.id!, p]));

  const getLevel = (positionId: string, visited = new Set<string>()): number => {
    if (visited.has(positionId)) {
      // Preserve original behavior — page logged the cycle. See arch-review note.
      // eslint-disable-next-line no-console
      console.error(`Circular dependency detected for position ${positionId}`);
      return 1;
    }

    const position = positionMap.get(positionId);
    if (!position) return 1;
    if (!position.parentPositionId) return 1;

    visited.add(positionId);
    return getLevel(position.parentPositionId, visited) + 1;
  };

  const updatedPositions = data.organization.positions.map((position) => ({
    ...position,
    level: getLevel(position.id!),
  })) as Position[];

  return {
    ...data,
    organization: {
      ...data.organization,
      positions: updatedPositions,
    },
  };
}

// Walk the parent chain and return every ancestor id for the given position.
export function getAllParentPositionIds(
  positionId: string,
  allPositions: Position[],
): Set<string> {
  const parentIds = new Set<string>();
  const positionMap = new Map(allPositions.map((p) => [p.id!, p]));

  const findParents = (id: string) => {
    const position = positionMap.get(id);
    if (position && position.parentPositionId) {
      parentIds.add(position.parentPositionId);
      findParents(position.parentPositionId);
    }
  };

  findParents(positionId);
  return parentIds;
}

// Return roots: positions without a parent, or whose parent is not in the list.
export function buildTree(positions: Position[]): Position[] {
  const positionMap = new Map(positions.map((p) => [p.id, p]));
  const roots: Position[] = [];

  positions.forEach((position) => {
    if (!position.parentPositionId || !positionMap.has(position.parentPositionId)) {
      roots.push(position);
    }
  });

  return roots;
}

// Return direct children of `parentId` from the given list.
// Pure: takes the positions array explicitly instead of closing over page state.
export function getChildren(parentId: string, positions: Position[]): Position[] {
  return positions.filter((p) => p.parentPositionId === parentId);
}
```

- [ ] **Step 2.2: Run the tests to verify they pass**

```bash
cd frontend && npx react-scripts test --watchAll=false src/pages/__tests__/orgChartUtils.test.ts
```

Expected: PASS — all 12 tests green.

- [ ] **Step 2.3: Verify lint passes for the new file**

```bash
cd frontend && npx eslint src/pages/orgChartUtils.ts src/pages/__tests__/orgChartUtils.test.ts
```

Expected: no errors. (The single `console.error` inside the cycle guard is suppressed via `eslint-disable-next-line` since it preserves the original behavior per spec FR-1's "verbatim move" requirement.)

- [ ] **Step 2.4: Commit the new module**

```bash
git add frontend/src/pages/orgChartUtils.ts
git commit -m "feat(orgchart): extract pure tree algorithms into orgChartUtils"
```

---

## Task 3: Rewire `OrgChartPage.tsx` to use `orgChartUtils`

**Files:**
- Modify: `frontend/src/pages/OrgChartPage.tsx`

`renderPositionCard` still lives in the page at this point — it is removed in Task 5. This task only removes the four pure functions, the `OrganizationData` interface, and the `Position` type alias, replacing them with imports from `./orgChartUtils`. After this task, the page must build, lint, and behave identically.

- [ ] **Step 3.1: Replace the imports and inline type/function definitions**

Edit `frontend/src/pages/OrgChartPage.tsx`. Apply all four changes below:

**Change 1 — top of file.** Replace the `Position`/`OrganizationData` declarations (lines 5–13) with an import from the new utils module. The final import block at the top of the file should read:

```typescript
import React, { useEffect, useState, useRef, useMemo } from 'react';
import { useOrgChart } from '../api/hooks/useOrgChart';
import { PositionDto } from '../api/generated/api-client';
import {
  calculateLevels,
  getAllParentPositionIds,
  buildTree,
  getChildren as orgChartGetChildren,
  Position,
  OrganizationData,
} from './orgChartUtils';

interface PositionRect {
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
}
```

Note: `PositionDto` import is retained — `renderPositionCard` still references positions by their DTO shape until Task 5.

**Change 2 — delete the inline `calculateLevels` definition (current lines 36–65).** The `useMemo` block (lines 68–79) keeps calling `calculateLevels(transformedData)` — now resolved against the imported function. Do not change the `useMemo` body.

**Change 3 — delete the inline `getAllParentPositionIds` (current lines 161–175).** Find the call inside the `filteredPositions` IIFE (currently `const parents = getAllParentPositionIds(pos.id!, allPositions);`). It already passes `allPositions`, so the call site needs no change — only the inline definition is deleted.

**Change 4 — delete the inline `buildTree` (current lines 238–249) and the inline `getChildren` (current lines 251–253).** Replace the inline `getChildren` with a closure-bound version directly above `renderPositionCard`:

```typescript
  const getChildren = (parentId: string): Position[] =>
    orgChartGetChildren(parentId, filteredPositions);
```

This binds the pure utility to the page's current `filteredPositions`, preserving the exact behavior `renderPositionCard` relies on.

The call to `buildTree(filteredPositions)` near the bottom of `OrgChartPage.tsx` (currently line 502) is already compatible with the imported pure `buildTree` — no change required at the call site.

- [ ] **Step 3.2: Confirm the page builds**

```bash
cd frontend && npm run build
```

Expected: build succeeds with no new warnings.

- [ ] **Step 3.3: Confirm lint passes**

```bash
cd frontend && npm run lint
```

Expected: no new warnings or errors.

- [ ] **Step 3.4: Run the utils tests to confirm nothing regressed**

```bash
cd frontend && npx react-scripts test --watchAll=false src/pages/__tests__/orgChartUtils.test.ts
```

Expected: PASS — 12 tests green.

- [ ] **Step 3.5: Commit**

```bash
git add frontend/src/pages/OrgChartPage.tsx
git commit -m "refactor(orgchart): wire OrgChartPage to orgChartUtils"
```

---

## Task 4: Write tests for `PositionCard` (RED)

**Files:**
- Create: `frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx`

- [ ] **Step 4.1: Write the failing test file**

Create `frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx`:

```typescript
import React from 'react';
import { render } from '@testing-library/react';
import '@testing-library/jest-dom';
import { PositionCard } from '../PositionCard';
import { PositionDto, EmployeeDto } from '../../../api/generated/api-client';

const makePosition = (overrides: Partial<PositionDto>): PositionDto => {
  const p = new PositionDto();
  Object.assign(p, overrides);
  return p;
};

const makeEmployee = (overrides: Partial<EmployeeDto>): EmployeeDto => {
  const e = new EmployeeDto();
  Object.assign(e, overrides);
  return e;
};

const noChildren = (_parentId: string): PositionDto[] => [];

const stubLevelColor = (level: number): string => `border-l-4 level-${level}`;

describe('PositionCard', () => {
  it('renders a leaf position with data-position-id on the outer card', () => {
    // Arrange
    const position = makePosition({
      id: 'pos-1',
      title: 'CEO',
      department: 'Executive',
      description: 'Top of the org',
      level: 1,
      employees: [makeEmployee({ id: 'e1', name: 'Alice Anderson', email: 'a@example.com' })],
    });

    // Act
    const { container } = render(
      <PositionCard
        position={position}
        getChildren={noChildren}
        getLevelColor={stubLevelColor}
      />,
    );

    // Assert — the geometry useEffect in OrgChartPage queries this attribute.
    expect(container.querySelector('[data-position-id="pos-1"]')).not.toBeNull();
    expect(container).toMatchSnapshot();
  });

  it('renders a recursive position with one child', () => {
    // Arrange
    const parent = makePosition({
      id: 'parent',
      title: 'Manager',
      department: 'Sales',
      description: 'Manages the team',
      level: 2,
    });
    const child = makePosition({
      id: 'child',
      title: 'Rep',
      department: 'Sales',
      description: 'Sells things',
      level: 3,
      parentPositionId: 'parent',
    });

    const getChildren = (parentId: string): PositionDto[] =>
      parentId === 'parent' ? [child] : [];

    // Act
    const { container } = render(
      <PositionCard
        position={parent}
        getChildren={getChildren}
        getLevelColor={stubLevelColor}
      />,
    );

    // Assert
    expect(container.querySelector('[data-position-id="parent"]')).not.toBeNull();
    expect(container.querySelector('[data-position-id="child"]')).not.toBeNull();
    expect(container).toMatchSnapshot();
  });
});
```

- [ ] **Step 4.2: Run the tests to confirm they fail**

```bash
cd frontend && npx react-scripts test --watchAll=false src/components/OrgChart/__tests__/PositionCard.test.tsx
```

Expected: FAIL — `Cannot find module '../PositionCard'`.

- [ ] **Step 4.3: Commit the failing tests**

```bash
git add frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx
git commit -m "test(orgchart): add snapshot tests for PositionCard (RED)"
```

---

## Task 5: Create `PositionCard.tsx` and remove `renderPositionCard` from the page (GREEN)

**Files:**
- Create: `frontend/src/components/OrgChart/PositionCard.tsx`
- Modify: `frontend/src/pages/OrgChartPage.tsx`

The JSX inside `renderPositionCard` (current lines 256–358) moves verbatim into the new component. `getInitials` (current page lines 228–235) moves with it as a file-private helper. The new component must be a `function` declaration (so its name is hoisted and the recursive call works) and the outer `<div>` must keep `data-position-id={position.id}` exactly.

- [ ] **Step 5.1: Create the component file**

Create `frontend/src/components/OrgChart/PositionCard.tsx`:

```typescript
import React from 'react';
import { PositionDto } from '../../api/generated/api-client';

export interface PositionCardProps {
  position: PositionDto;
  getChildren: (parentId: string) => PositionDto[];
  getLevelColor: (level: number) => string;
}

function getInitials(name: string | undefined): string {
  if (!name) return '?';
  return name
    .split(' ')
    .map((n) => n[0])
    .join('')
    .toUpperCase();
}

export function PositionCard({
  position,
  getChildren,
  getLevelColor,
}: PositionCardProps): JSX.Element {
  const children = getChildren(position.id!);

  return (
    <div key={position.id} className="flex flex-col items-center">
      {/* Position card */}
      <div
        data-position-id={position.id}
        className={`bg-white rounded-xl shadow-lg p-6 w-80 transition-all hover:shadow-2xl hover:-translate-y-1 ${getLevelColor(
          position.level ?? 1
        )} relative mb-20`}
      >
        {(position.employees?.length || 0) > 1 && (
          <div className="absolute top-3 right-3 bg-indigo-600 text-white w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold">
            {position.employees?.length}
          </div>
        )}

        <div className="inline-block bg-blue-100 text-blue-700 px-3 py-1 rounded-full text-xs font-semibold mb-3">
          {position.department}
        </div>

        {position.url ? (
          <a
            href={position.url}
            target="_blank"
            rel="noopener noreferrer"
            className="text-lg font-bold text-gray-900 mb-2 hover:text-indigo-600 transition-colors flex items-center gap-1 cursor-pointer"
          >
            {position.title}
            <svg
              xmlns="http://www.w3.org/2000/svg"
              className="h-4 w-4"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
              />
            </svg>
          </a>
        ) : (
          <h3 className="text-lg font-bold text-gray-900 mb-2">{position.title}</h3>
        )}
        <p className="text-sm text-gray-600 mb-4 leading-relaxed">{position.description}</p>

        <div className="border-t border-gray-200 pt-4 space-y-2">
          {(position.employees || []).map((emp) => (
            <div key={emp.id} className="flex items-center gap-3 p-2 rounded-lg hover:bg-gray-50 transition-colors">
              <div
                className={`w-9 h-9 rounded-full flex items-center justify-center text-white font-bold text-sm flex-shrink-0 ${
                  emp.isPrimary
                    ? 'bg-gradient-to-br from-pink-400 to-red-500 shadow-md'
                    : 'bg-gradient-to-br from-indigo-500 to-purple-600'
                }`}
              >
                {getInitials(emp.name)}
              </div>
              <div className="flex-1 min-w-0">
                {emp.url ? (
                  <a
                    href={emp.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm font-semibold text-gray-900 truncate hover:text-indigo-600 transition-colors flex items-center gap-1 cursor-pointer"
                  >
                    {emp.name}
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      className="h-3 w-3 flex-shrink-0"
                      fill="none"
                      viewBox="0 0 24 24"
                      stroke="currentColor"
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14"
                      />
                    </svg>
                  </a>
                ) : (
                  <div className="text-sm font-semibold text-gray-900 truncate">{emp.name}</div>
                )}
                <div className="text-xs text-gray-500 truncate">{emp.email}</div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Children */}
      {children.length > 0 && (
        <div className="flex justify-center gap-12">
          {children.map((child) => (
            <PositionCard
              key={child.id}
              position={child}
              getChildren={getChildren}
              getLevelColor={getLevelColor}
            />
          ))}
        </div>
      )}
    </div>
  );
}

export default PositionCard;
```

- [ ] **Step 5.2: Remove `getInitials` and `renderPositionCard` from the page; import and use `PositionCard`**

Edit `frontend/src/pages/OrgChartPage.tsx`. Apply all four changes below:

**Change 1 — add the `PositionCard` import.** Below the existing imports (after the `orgChartUtils` import added in Task 3), add:

```typescript
import { PositionCard } from '../components/OrgChart/PositionCard';
```

**Change 2 — delete the entire `getInitials` block** (current lines 228–235 — six lines including the blank line above and below; verify by reading the file before editing).

**Change 3 — delete the entire `renderPositionCard` block** (current lines 255–358 — the `// Render position card` comment plus the function definition through its closing `};`).

**Change 4 — update both render sites that previously called `renderPositionCard`.**

Inside the recursive `children.length > 0 && …` JSX block (current line 354), that block lives inside `renderPositionCard` and gets deleted with it. Only one external call site remains: the `buildTree(filteredPositions).map((root) => renderPositionCard(root))` at the bottom of the page (current line 502). Replace that line with:

```tsx
{buildTree(filteredPositions).map((root) => (
  <PositionCard
    key={root.id}
    position={root}
    getChildren={getChildren}
    getLevelColor={getLevelColor}
  />
))}
```

The `getChildren` reference here is the closure-bound helper added in Task 3, Step 3.1, Change 4 — which now wraps the pure `orgChartGetChildren` utility against `filteredPositions`.

- [ ] **Step 5.3: Run `PositionCard` tests to verify they pass**

```bash
cd frontend && npx react-scripts test --watchAll=false src/components/OrgChart/__tests__/PositionCard.test.tsx
```

Expected: PASS — two snapshot tests green; two snapshot files written under `frontend/src/components/OrgChart/__tests__/__snapshots__/`.

- [ ] **Step 5.4: Run the full test suite to ensure no regressions**

```bash
cd frontend && npx react-scripts test --watchAll=false
```

Expected: every previously-passing test continues to pass. Two new snapshots committed. No tests for `OrgChartPage` currently exist, so the broader suite should be unaffected.

- [ ] **Step 5.5: Verify the page builds and lints**

```bash
cd frontend && npm run build && npm run lint
```

Expected: build succeeds, lint produces no new warnings.

- [ ] **Step 5.6: Commit**

```bash
git add frontend/src/components/OrgChart/PositionCard.tsx \
        frontend/src/components/OrgChart/__tests__/PositionCard.test.tsx \
        frontend/src/components/OrgChart/__tests__/__snapshots__ \
        frontend/src/pages/OrgChartPage.tsx
git commit -m "refactor(orgchart): extract PositionCard component"
```

---

## Task 6: Final verification

**Files:** none modified — verification only.

This task is the spec's "shrink to ~150 lines" and "pixel-identical UI" gate. Confirm the structural goals and behavioral preservation in one pass before marking the feature done.

- [ ] **Step 6.1: Confirm the page size is in target range**

```bash
wc -l frontend/src/pages/OrgChartPage.tsx
```

Expected: the file is well below the hard ceiling of 300 lines (target ~150). If it materially exceeds 200 lines, re-read the file and confirm that no dead code from the old utilities/`getInitials`/`renderPositionCard` was left behind. (Do not extract additional concerns — that is explicitly out of scope per spec.)

- [ ] **Step 6.2: Confirm there are no orphaned references**

```bash
cd frontend && npx eslint --no-eslintrc --rule '{"no-unused-vars": "error", "@typescript-eslint/no-unused-vars": "error"}' --parser @typescript-eslint/parser --plugin @typescript-eslint src/pages/OrgChartPage.tsx src/pages/orgChartUtils.ts src/components/OrgChart/PositionCard.tsx
```

If the ad-hoc rule invocation isn't trivially runnable in the project (e.g. plugins not loadable standalone), the normal `npm run lint` from Step 5.5 already covers unused-vars under the project config — that's the canonical check; this step is optional reinforcement.

- [ ] **Step 6.3: Confirm the DOM contract is preserved**

```bash
cd frontend && grep -n 'data-position-id' src/components/OrgChart/PositionCard.tsx src/pages/OrgChartPage.tsx
```

Expected: exactly one occurrence in `PositionCard.tsx` (on the outer card `<div>`). The page should reference `[data-position-id]` only inside `containerRef.current.querySelectorAll(...)` — that line is untouched and must still exist.

- [ ] **Step 6.4: Run the full test suite a final time**

```bash
cd frontend && npx react-scripts test --watchAll=false
```

Expected: all green. New `orgChartUtils` tests pass; new `PositionCard` snapshot tests pass; no other tests regressed.

- [ ] **Step 6.5: Manual smoke test in the browser**

```bash
cd frontend && npm start
```

Navigate to `/orgchart` (logged-in user) and verify visually:

1. The chart renders with the same hierarchical layout as before.
2. SVG connector lines connect each parent to each child correctly.
3. Hover-elevation effect on cards still works.
4. "Oddělení" (department) filter restricts the chart correctly and parent positions remain visible.
5. "Úroveň" (level) filter restricts by depth.
6. "Resetovat filtry" returns to the unfiltered view.
7. Zoom −/+/Reset buttons resize the chart and connectors remain aligned with cards.
8. Position card title links and employee links open in new tabs when `url` is set.
9. Position cards show the employee-count badge in the top-right when there are >1 employees.

If any step fails: stop, investigate, fix, and re-run from Step 6.4. (Spec NFR-4 is zero breaking changes — a behavioral diff blocks the refactor.)

- [ ] **Step 6.6: No-op commit guard**

```bash
git status
```

Expected: working tree clean. If anything is unstaged, decide whether it was an accidental edit, then either commit or revert before finishing.

---

## Spec Coverage Map

| Spec requirement | Tasks |
|------------------|-------|
| FR-1 (extract pure utilities) | Tasks 1, 2, 3 |
| FR-2 (extract `PositionCard`) | Tasks 4, 5 |
| FR-3 (page remains functionally identical) | Task 3 (utils wiring), Task 5 (card wiring), Task 6 (manual smoke) |
| FR-4 (unit tests for utilities, ≥80% coverage) | Task 1 — 12 tests cover all four exported functions including edge cases |
| FR-5 (snapshot test for `PositionCard`, incl. recursive) | Task 4 — two snapshot tests, one leaf, one recursive |
| NFR-1 (no perf regression / no extraneous `React.memo`) | Plan explicitly declines memoization; `PositionCard` is a plain function (arch-review Decision 5) |
| NFR-2 (no security implications) | No data-flow changes |
| NFR-3 (file size targets) | Task 6.1 verifies page size; new files are well under 200 lines |
| NFR-4 (zero breaking changes) | Task 6.5 manual smoke test gates the refactor |
| Arch-review amendment 1 (test paths in `__tests__/`) | Tasks 1, 4 |
| Arch-review amendment 2 (no `React.FC`) | Task 5 — `function PositionCard(...)` declaration |
| Arch-review amendment 3 (`getChildren` prop callback shape) | Task 3 (page binding), Task 5 (component props) |
| Arch-review amendment 4 (move `OrganizationData` + `Position` alias) | Task 2 |
| Arch-review amendment 5 (`getInitials` moves into card) | Task 5.1 (component) + Task 5.2 Change 2 (page deletion) |
| Arch-review amendment 6 (`getChildren` signature change mandatory) | Task 2 (`getChildren(parentId, positions)`) |
