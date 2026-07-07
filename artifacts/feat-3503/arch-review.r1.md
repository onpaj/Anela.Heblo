# Architecture Review: Unit test coverage for `urlUtils.ts`

## Skip Design: true

No UI, screen, layout, or visual component work is involved. This task adds Jest unit tests for two existing, unmodified pure functions (`createFilteredUrl`, `isTileClickable`) in `frontend/src/utils/urlUtils.ts`. There is no new or changed user-facing surface to design.

## Architectural Fit Assessment

This is a pure test-authoring task with zero production code impact — it fits cleanly and low-risk into the existing frontend testing setup. `frontend/src/utils/urlUtils.ts` currently has no colocated test file at all (confirmed: `frontend/src/utils/__tests__/` contains `dateUtils.test.ts`, `downloadTextFile.test.ts`, `errorHandler.test.ts`, `sharepointLink.test.ts`, but no `urlUtils.test.ts`), which is why coverage sits at 50%.

The only architectural decision of substance is **where the test file goes and what conventions it follows** — the spec's suggestion (`frontend/src/utils/urlUtils.test.ts`, colocated directly beside the source) conflicts with the repository's established convention, verified by inspecting the sibling utils tests: every existing test for a file in `frontend/src/utils/` lives in the `__tests__/` subdirectory, not colocated next to the source file. This review corrects that in the Specification Amendments section below — developers should follow the existing `__tests__/` pattern, not the spec's literal path.

No new dependencies, no jest config changes are needed: the project uses Create React App's built-in Jest runner (no standalone `jest.config.*` at the frontend root), invoked via `npm test`, and existing sibling tests use plain `describe`/`it`/`expect` with no additional test utilities (no RTL needed here since these are pure functions, not components).

## Proposed Architecture

### Component Overview

```
frontend/src/utils/
├── urlUtils.ts                  (existing, UNMODIFIED)
└── __tests__/
    ├── dateUtils.test.ts         (existing, reference for conventions)
    ├── downloadTextFile.test.ts  (existing)
    ├── errorHandler.test.ts      (existing)
    ├── sharepointLink.test.ts    (existing)
    └── urlUtils.test.ts          (NEW — this task's deliverable)
```

No other components are touched. `urlUtils.test.ts` imports `createFilteredUrl` and `isTileClickable` (and, incidentally, may reference `DrillDownInfo`/`TileDataWithDrillDown` types for typed fixtures) from `../urlUtils` and exercises them in isolation — no mocks, no providers, no async handling required, since both functions are synchronous and side-effect free.

### Key Design Decisions

#### Decision 1: Test file location
**Options considered:**
- (a) Colocate as `frontend/src/utils/urlUtils.test.ts` (spec's literal suggestion, FR-1 wording).
- (b) Place under `frontend/src/utils/__tests__/urlUtils.test.ts`, matching the four existing sibling test files.

**Chosen approach:** (b) — `frontend/src/utils/__tests__/urlUtils.test.ts`.

**Rationale:** Every existing unit test for a `frontend/src/utils/*.ts` file in this repository lives in the `__tests__/` subdirectory (verified by directory listing). CRA's Jest config auto-discovers both `__tests__/` folders and colocated `*.test.ts` files, so either works mechanically — but consistency with the established convention is what a reviewer and future maintainers will expect. The spec's exact filename wording is a suggestion, not a hard requirement (the spec's own acceptance criteria only describe test *behavior*, not the literal path), so this is a low-risk, appropriate deviation.

#### Decision 2: Test structure and grouping
**Options considered:**
- One flat `describe('urlUtils', ...)` block with all tests.
- Two top-level `describe` blocks, one per function, matching FR-1/FR-2 in the spec and mirroring `dateUtils.test.ts`'s per-function nested `describe` structure.

**Chosen approach:** Two `describe` blocks — `describe('createFilteredUrl', ...)` and `describe('isTileClickable', ...)` — each with individual `it(...)` cases per acceptance criterion.

**Rationale:** Matches the existing convention in `dateUtils.test.ts` (nested `describe` per function under tests), keeps failure output scoped to the specific function/scenario, and maps 1:1 to the spec's FR-1/FR-2 acceptance criteria for easy traceability.

#### Decision 3: No mocking or test utilities beyond Jest globals
**Options considered:** Use React Testing Library / MSW (per `testing-strategy.md`'s general frontend stack) vs. plain Jest.

**Chosen approach:** Plain Jest (`describe`/`it`/`expect`), no RTL, no MSW, no fixtures/factories beyond inline literals.

**Rationale:** Both functions are pure, synchronous, and have no React or network dependency. `testing-strategy.md`'s RTL/MSW guidance applies to component and API-integration tests; these are plain utility-function unit tests, the same category as the existing `dateUtils.test.ts`/`errorHandler.test.ts`, which also use no RTL/MSW.

## Implementation Guidance

### Directory / Module Structure

Create exactly one new file:
- `frontend/src/utils/__tests__/urlUtils.test.ts`

No other files are created or modified. `frontend/src/utils/urlUtils.ts` remains byte-for-byte unchanged (per spec's explicit out-of-scope statement).

### Interfaces and Contracts

Test against the existing, unmodified exports of `frontend/src/utils/urlUtils.ts`:

```typescript
export const createFilteredUrl = (baseUrl: string, filters: Record<string, any>): string => ...

export interface DrillDownInfo {
  filters?: Record<string, any>;
  enabled: boolean;
  tooltip?: string;
}

export interface TileDataWithDrillDown {
  status?: string;
  data?: { count?: number; [key: string]: any };
  error?: string;
  drillDown?: DrillDownInfo;
  [key: string]: any;
}

export const isTileClickable = (tileData: TileDataWithDrillDown): boolean => ...
```

Import line for the new test file:
```typescript
import { createFilteredUrl, isTileClickable } from '../urlUtils';
```

No new interfaces, types, or contracts are introduced by this task.

### Data Flow

N/A in the runtime sense — these are pure functions with no I/O. The "data flow" here is purely test-input → function → assertion:

1. **`createFilteredUrl(baseUrl, filters)`**: test constructs a `filters` object literal per acceptance criterion (e.g. `{ enabled: false }`, `{ page: 0 }`, `{ a: null, b: undefined, c: '' }`, mixed cases), calls the function, and asserts on the returned string (either exact equality against an expected `baseUrl?key=value&...` string, or `.toContain('key=value')` / `.not.toContain(...)` checks — prefer exact-string assertions for the single-key cases per FR-1, and `toContain`/`not.toContain` for the mixed-object case since `URLSearchParams` key order is insertion-order-stable but easiest to assert piecewise).
2. **`isTileClickable(tileData)`**: test constructs a `TileDataWithDrillDown` (or partial) object literal per FR-2's five cases and asserts the boolean return value with `toBe(true)`/`toBe(false)`.

No provider wrapping, no async/`waitFor`, no network mocking is required.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file placed at wrong path (`urlUtils.test.ts` colocated) diverging from repo convention, causing reviewer friction or inconsistent codebase | Low | Use `frontend/src/utils/__tests__/urlUtils.test.ts` per Decision 1 above; CI/Jest discovery is unaffected either way |
| Assertions written against `URLSearchParams`-generated string using a brittle exact match that breaks if param order changes | Low | For single-filter cases assert exact strings (order is deterministic for one key); for the mixed-values case in FR-1, assert via `toContain`/`not.toContain` on substrings rather than one rigid full-string equality |
| Tests inadvertently assert on `isTileClickable`'s current behavior in a way that reads as "this is definitely correct business behavior" rather than "this documents current, possibly-debatable behavior" (the `filters: {}` → clickable case) | Low | Name the test descriptively, e.g. `it('treats an empty-but-present filters object as clickable (documents current behavior)', ...)`, consistent with spec's own framing in FR-2 |
| Coverage threshold (60%) not actually met after adding tests, if other untested branches remain in the file | Low | Out of scope per spec, but developer should run `npm test -- --coverage` on this file after implementation to confirm ≥60% is reached; if not, flag back rather than expanding scope silently |

## Specification Amendments

- **FR-1 file path amendment**: Replace "create `frontend/src/utils/urlUtils.test.ts` if it does not already exist" with **create `frontend/src/utils/__tests__/urlUtils.test.ts`** to match the established convention for every other test file under `frontend/src/utils/` (`dateUtils.test.ts`, `downloadTextFile.test.ts`, `errorHandler.test.ts`, `sharepointLink.test.ts` all live in `__tests__/`, none are colocated). This is a path-only correction; all acceptance criteria in FR-1 and FR-2 remain unchanged and fully apply.
- No other amendments. The spec's functional requirements, acceptance criteria, and out-of-scope list are architecturally sound as written and require no further changes.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed. The Jest test runner, TypeScript tooling, and `npm test` command are already fully configured in `frontend/`; the developer can start immediately by creating the single new test file described above.
