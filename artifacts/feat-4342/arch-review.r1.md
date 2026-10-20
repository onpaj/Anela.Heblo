# Architecture Review: Unit test coverage for MarginsChart's monthly data mapping

## Skip Design: true
This is a test-only, no-behavior-change coverage task confined to one existing component's internal data-mapping logic. There is no new or changed UI, screen, layout, or visual design decision involved — the extraction target is a pure function move, not a component redesign. The designer phase should be skipped.

## Architectural Fit Assessment
The codebase already has an established convention for exactly this situation: pure, chart-support logic that started inline in a component and was pulled out into a colocated, directly-testable module once it needed test coverage. `frontend/src/components/catalog/detail/charts/ChartHelpers.tsx` (tested by `frontend/src/components/catalog/detail/charts/__tests__/ChartHelpers.test.ts`) is the closest analog: it holds `generatePointStyling` and `generateTooltipCallback`, both of which `MarginsChart.tsx` already imports and uses. There is also a broader, repo-wide pattern of colocated `*Utils.ts` / `utils.ts` modules next to the components that use them (`frontend/src/components/pages/financial-overview/utils.ts`, `frontend/src/utils/dateUtils.ts`, `frontend/src/pages/orgChartUtils.ts`). Extracting `mapMarginDataToMonthlyArrays` follows both conventions directly. No new architectural pattern, dependency, or module boundary is introduced.

## Proposed Architecture

### Component Overview
```
MarginsTab/
├── MarginsChart.tsx                 (existing — component, shrinks to import + call the extracted fn)
├── MarginsChart.utils.ts            (NEW — extracted pure function + its types)
├── MarginsSummary.tsx               (existing, unrelated)
└── __tests__/
    ├── MarginsSummary.test.tsx      (existing)
    └── MarginsChart.utils.test.ts   (NEW — unit tests for the extracted function)
```
No changes outside `MarginsTab/`. No new dependencies, no API/contract changes, no backend involvement.

### Key Design Decisions

#### Decision 1: Extraction target — colocated sibling file vs. shared `charts/ChartHelpers.tsx`
**Options considered:**
1. Add `mapMarginDataToMonthlyArrays` into the existing shared `frontend/src/components/catalog/detail/charts/ChartHelpers.tsx`, alongside `generatePointStyling`/`generateTooltipCallback`.
2. Create a new colocated file `MarginsChart.utils.ts` inside `MarginsTab/`, next to `MarginsChart.tsx`.

**Chosen approach:** Option 2 — colocated `MarginsChart.utils.ts`.

**Rationale:** `ChartHelpers.tsx` is a *shared, cross-chart* module — both `generatePointStyling` and `generateTooltipCallback` are generic and consumed by multiple chart components (confirm at implementation time, but they are written generically, taking no `MarginsChart`-specific types). `mapMarginDataToMonthlyArrays`, by contrast, is specific to `MarginsChart`'s exact data shape (`MarginHistoryDto`, the fixed 8-array/12-slot output, the "12 months excluding current" windowing convention) and is not reused elsewhere in the codebase (confirmed: no other references to `MonthlyArrays`-named logic exist). Colocating it next to the one component that uses it, following the `financial-overview/utils.ts`-style convention, keeps the shared `ChartHelpers.tsx` module free of component-specific logic and keeps the new code discoverable exactly where a developer would look for it.

#### Decision 2: Deterministic "now" for tests — inject `now` parameter vs. fake timers only
**Options considered:**
1. Leave `now = new Date()` internal to the function; control it purely via `jest.useFakeTimers().setSystemTime(...)` in tests.
2. Refactor the function signature to accept an optional `now: Date = new Date()` parameter, used instead of reading `Date` directly, and pass fixed dates explicitly in tests (no fake timers needed).

**Chosen approach:** Option 2 — add an optional `now` parameter.

**Rationale:** The spec's NFR-1 (determinism) and NFR-2 (no behavior change, byte-for-byte identical output for the same inputs) are both satisfiable either way, but an injected parameter is simpler and more robust: it avoids any interaction between Jest fake timers and other libraries used in the same test file or suite (e.g. `react-chartjs-2`/`Chart.js`, which the wider test suite loads elsewhere and which can be sensitive to global timer mocking), makes each test's "current date" explicit and self-documenting at the call site, and requires no `beforeEach`/`afterEach` fake-timer setup/teardown. Defaulting the parameter to `new Date()` preserves the existing call site in `MarginsChart.tsx` (`mapMarginDataToMonthlyArrays()`, no args) unchanged, satisfying NFR-2. This is a minimal, additive signature change to a function that is being extracted anyway — not a new public contract to coordinate elsewhere, since nothing outside `MarginsChart.tsx` calls it today.

#### Decision 3: Scope of extraction — whole function vs. only the year-boundary math
**Options considered:**
1. Extract only the small year-boundary calculation (lines 97-101) as its own tiny helper (e.g. `adjustMonthForYearBoundary`), leaving the rest of `mapMarginDataToMonthlyArrays` (the Map-building loop, the fallback-to-0 lookups, the current-month filter) inline in the component.
2. Extract the entire `mapMarginDataToMonthlyArrays` function as one unit, as the spec assumes.

**Chosen approach:** Option 2 — extract the whole function.

**Rationale:** The three untested behaviors named in the issue (year-boundary correction, current-month exclusion, fallback-to-0) are not independent — they all operate over the same `marginHistory` traversal and the same lookup maps within one function body. Splitting out only the boundary math would still leave the exclusion filter and the fallback behavior untestable without rendering the component, defeating the purpose. Extracting the whole function is also the only way tests can assert on the *result* of the year-boundary correction (i.e., that the right map key ends up populated in the right output slot) rather than just the arithmetic in isolation, which is what FR-1/FR-2 in the spec actually require.

## Implementation Guidance

### Directory / Module Structure
- New file: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.utils.ts`
  - Exports `mapMarginDataToMonthlyArrays(marginHistory: MarginHistoryDto[], now: Date = new Date())`, returning the same object shape as today (`{ m0PercentageData, m1PercentageData, m2PercentageData, m3PercentageData, m0CostLevelData, m1CostLevelData, m2CostLevelData, m3CostLevelData }`).
  - Also a reasonable place to export a `MonthlyMarginArrays` (or similar) return-type interface if useful for the test file's type-checking, but this is optional polish, not required by the spec.
  - Import `MarginHistoryDto` from the same relative path (`../../../../../api/generated/api-client`, adjusted for the new file's location — same directory as `MarginsChart.tsx`, so the relative path is unchanged).
- New file: `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts` — matches the existing `__tests__/MarginsSummary.test.tsx` colocation pattern in the same directory.
- Modified file: `MarginsChart.tsx` — remove the inline `mapMarginDataToMonthlyArrays` definition (lines ~41-124), import it from `./MarginsChart.utils`, and call it as `mapMarginDataToMonthlyArrays(marginHistory)` (no `now` arg, so it defaults to `new Date()` exactly as today). `generateMonthLabelsExcludingCurrent` (lines 23-36) stays inline in the component — it is not named in the issue and is out of scope per the spec.

### Interfaces and Contracts
```typescript
// MarginsChart.utils.ts
export function mapMarginDataToMonthlyArrays(
  marginHistory: MarginHistoryDto[],
  now: Date = new Date(),
): {
  m0PercentageData: number[];
  m1PercentageData: number[];
  m2PercentageData: number[];
  m3PercentageData: number[];
  m0CostLevelData: number[];
  m1CostLevelData: number[];
  m2CostLevelData: number[];
  m3CostLevelData: number[];
};
```
Internals (Map-building loop, current-month skip, year-boundary correction, `.get(key) || 0` fallback) move verbatim — no logic changes, only `now`/`currentYear`/`currentMonth` derivation switches from reading `new Date()` directly to using the `now` parameter.

### Data Flow
Unchanged at the component boundary: `MarginsChart` still receives `marginHistory: MarginHistoryDto[]` as a prop and still produces the same 8 arrays consumed by `chartData.datasets`. The only new data flow is internal to testing: test files call `mapMarginDataToMonthlyArrays(fixtureData, fixedDate)` directly and assert on the returned arrays, with no rendering, no `react-chartjs-2`/`Chart.js` involvement, and no jsdom concerns — this is the main practical benefit of extraction over the render-and-inspect alternative considered in the spec.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Extraction accidentally changes behavior (e.g. reordering the Map-building loop, mis-copying the `\|\| 0` fallback) | Medium | Move the function body verbatim (copy-paste, then only touch the 3 lines that reference `new Date()`/`currentYear`/`currentMonth` derivation); the new tests must pass against the extracted code unchanged, and existing `MarginsChart` rendering (if any snapshot/rendering tests exist) must continue to pass unmodified. |
| Off-by-one in the *test's own* expected-value derivation makes a wrong implementation look correct (self-fulfilling test) | Medium | Tests must derive expected month/year for each of the 12 slots independently by reasoning about calendar arithmetic (e.g. hand-enumerating month sequences per FR-1/FR-2's acceptance criteria), not by re-running the same `currentMonth - monthsBack` formula under test. |
| `now` parameter default (`new Date()`) is itself impossible to unit-test without fake timers | Low | Not required — the spec only requires deterministic tests via explicit `now` arguments; the default-parameter fallback path is exercised implicitly by `MarginsChart`'s own existing (non-date-sensitive) rendering behavior and does not need its own dedicated "uses real Date()" test. |
| Import path breakage — `MarginsChart.tsx`'s existing relative imports (`MarginHistoryDto`, `JournalEntryDto`, `ChartHelpers`) must still resolve correctly after the function moves out | Low | New file lives in the exact same directory as `MarginsChart.tsx`, so `MarginHistoryDto`'s import path (`../../../../../api/generated/api-client`) is copied unchanged into `MarginsChart.utils.ts`; `MarginsChart.tsx`'s own imports for `JournalEntryDto`/`ChartHelpers` are untouched since those stay used directly in the component. |

## Specification Amendments
- FR-1's acceptance-criteria text is dense and partially self-contradicts mid-sentence (it starts describing one key sequence, calls it wrong, then restates the correct one). Implementers should treat the final, concrete sentence in FR-1 ("concretely: for `currentMonth=1, currentYear=Y`...") as the authoritative requirement and may disregard the earlier false-start sentence when writing the actual test cases and table of expected slot values.
- Add to Interfaces and Contracts (this document, above): the extracted function takes an optional `now: Date` parameter defaulting to `new Date()`. This is an implementation-structure addition consistent with the spec's Open-Questions resolution (extraction chosen) and NFR-1 (determinism); it does not change the spec's functional requirements.

## Prerequisites
None. No migrations, no config, no infrastructure changes. Existing Jest + React Testing Library setup (already used by `MarginsSummary.test.tsx`) is sufficient; no new dependency needs to be added to `frontend/package.json`.
