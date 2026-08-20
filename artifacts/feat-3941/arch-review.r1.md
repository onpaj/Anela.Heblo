# Architecture Review: ManufactureBatchCalculator test coverage for computePercentage edge cases and batch-size fallback

## Skip Design: true

No new or changed UI components, screens, layouts, or visual design decisions are involved. This is a test-only addition to an existing, unmodified component (`frontend/src/components/pages/ManufactureBatchCalculator.tsx`). The one production-adjacent change in scope — extending the local Jest mock of `CatalogAutocomplete` to invoke its `onSelect` prop — is test infrastructure, not a UI change; it never touches the real `CatalogAutocomplete` component or its rendered output. Confirmed against spec.r1.md: all four FRs (computePercentage edge case, batch-size fallback precedence, URL auto-selection, calculation-mode toggle) assert against existing, already-implemented behavior. "Out of Scope" explicitly excludes any production code changes beyond the mock.

## Architectural Fit Assessment

This fits cleanly into the existing frontend unit/component testing pattern described in `docs/architecture/testing-strategy.md` (Jest + React Testing Library, "Purpose-Driven Testing" — validate business logic/edge cases, not just coverage %) and the codebase's established conventions in the target file's own test suite:

- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` already exists and follows the repo's standard shape: `jest.mock` of the API-touching hook (`useManufactureBatch`) plus inert mocks of child components (`CatalogAutocomplete`, `InventoryStatusCell`, `ManufactureInventoryDetail`, `CatalogDetail`), then `describe` blocks split between a pure-function unit-test group (`computePercentage helper`) and a component-render group (`ManufactureBatchCalculator`).
- The needed `MemoryRouter` + `initialEntries` + `Route`/`Routes` pattern for URL-parameter-driven tests has direct precedent in `frontend/src/components/terminal/lot-identification/__tests__/PoLinePickStep.test.tsx` — confirmed by reading that file. It wraps in a `QueryClientProvider` too, but `ManufactureBatchCalculator` doesn't use React Query (it calls hooks directly), so that wrapper is not needed here — only the router pieces apply.
- `useManufactureBatch` (`frontend/src/api/hooks/useManufactureBatch.ts`) is a thin wrapper around `getAuthenticatedApiClient()` calls; it is already fully mocked at the module boundary in the existing test file, which is the correct and only integration point that needs mocking — no real API client, MSAL, or network path is exercised.

No module boundaries, contracts, or shared interfaces are affected. This is entirely additive within one test file.

## Proposed Architecture

### Component Overview

```
ManufactureBatchCalculator.test.tsx  (single file, extended in place)
│
├─ describe('computePercentage helper')          [existing, +2 cases: FR-1]
│
├─ describe('ManufactureBatchCalculator')         [existing smoke test, +new tests]
│   │
│   ├─ jest.mock('useManufactureBatch')  ──restructured──▶  mutable jest.fn() refs,
│   │                                                        reset in beforeEach (NFR-3)
│   │
│   ├─ jest.mock('CatalogAutocomplete')  ──extended──▶  invokes onSelect prop (NFR-3)
│   │
│   ├─ FR-2: batch-size fallback precedence (3 cases)
│   │     renders <BrowserRouter><ManufactureBatchCalculator/></BrowserRouter>
│   │     drives selection via mocked CatalogAutocomplete.onSelect
│   │
│   ├─ FR-3: URL parameter auto-selection
│   │     renders <MemoryRouter initialEntries=[...]><Routes><Route .../></Routes></MemoryRouter>
│   │     asserts getBatchTemplate/calculateBySize call args + rendered input value
│   │
│   └─ FR-4: calculation-mode toggle (4 cases)
│         renders <BrowserRouter>...</BrowserRouter>, selects product via mock,
│         waits for template, toggles radio, asserts DOM presence/absence + call routing
│
└─ No changes to production files.
```

Data flow for the component-under-test is unchanged from production: `CatalogAutocomplete.onSelect` (mocked to fire synchronously with a caller-supplied `CatalogItemDto`) → `handleProductSelect` → `getBatchTemplate` (mocked, per-test resolved value) → state (`template`, `desiredBatchSize`) → conditionally `calculateBySize` (mocked) → `calculationResult` → render.

### Key Design Decisions

#### Decision 1: Single extended test file vs. new test file(s)

**Options considered:**
- (a) Keep everything in the existing `ManufactureBatchCalculator.test.tsx`, adding new `describe` blocks.
- (b) Split into multiple files (e.g. `ManufactureBatchCalculator.urlParams.test.tsx`, `...modeToggle.test.tsx`).

**Chosen approach:** (a) — extend the existing file in place, as spec.r1.md's Background section directs ("appended to (or extending) the existing test file").

**Rationale:** The repo has no precedent for splitting a single component's tests across multiple files, and doing so here would duplicate the mock setup (`jest.mock` calls are file-scoped) for no benefit at this size (~4 new `describe` blocks, well within normal file length for this codebase's test files). Keeping one file also keeps the shared, restructured `useManufactureBatch` mock and the `beforeEach` reset logic in one place, which is exactly what NFR-3 asks for.

#### Decision 2: Mock restructuring strategy for `useManufactureBatch`

**Options considered:**
- (a) Replace the current `jest.mock` factory (which returns fixed `{ success: false }` resolves) with module-scoped `jest.fn()` variables that individual tests configure via `mockResolvedValueOnce`/`mockResolvedValue`, reset in `beforeEach`.
- (b) Use `jest.spyOn` on the hook module per-test without touching the top-level factory.
- (c) Introduce a test-utility factory function that returns a fresh set of mocks per test, injected via a wrapper `jest.mock` with `require` indirection.

**Chosen approach:** (a).

**Rationale:** This is the simplest change that satisfies NFR-3's explicit instruction ("via `jest.fn()` references captured in an outer scope, reset in `beforeEach`"). It requires no new test-utility module and matches how Jest module mocks are conventionally restructured for per-test configurability — declare the `jest.fn()`s above the `jest.mock()` call (Jest hoists `jest.mock` calls, but referencing `jest.fn()` inside the factory itself, or via a `let` declared before use with the factory returning references to it, is the standard pattern; use the `jest.fn()`-inside-factory form to avoid hoisting pitfalls, e.g.:
```ts
const mockGetBatchTemplate = jest.fn();
const mockCalculateBySize = jest.fn();
const mockCalculateByIngredient = jest.fn();
jest.mock('../../../api/hooks/useManufactureBatch', () => ({
  useManufactureBatch: () => ({
    getBatchTemplate: mockGetBatchTemplate,
    calculateBySize: mockCalculateBySize,
    calculateByIngredient: mockCalculateByIngredient,
    isLoading: false,
  }),
}));
```
Note: `jest.mock` factories cannot reference out-of-scope variables unless those variables are prefixed `mock` (Jest's `babel-plugin-jest-hoist` allow-list) — the `mock`-prefixed names above satisfy that constraint and must be preserved by developers implementing this.) `beforeEach(() => { mockGetBatchTemplate.mockReset(); ... })` gives full per-test isolation without inter-test leakage, directly satisfying "Tests must not depend on execution order."

#### Decision 3: `CatalogAutocomplete` mock enhancement scope

**Options considered:**
- (a) Extend the existing inert functional-component mock to accept `onSelect` and render a button/testid that, when clicked, calls `onSelect(mockProduct)` with a test-configurable product.
- (b) Bypass `CatalogAutocomplete` entirely for FR-2/FR-4 and call `handleProductSelect` indirectly some other way (not possible — it's not exported).

**Chosen approach:** (a), exactly as NFR-3 specifies ("must be extended... to accept and invoke its `onSelect` prop").

**Rationale:** `handleProductSelect` is an internal, non-exported callback; the only public seam to trigger it via manual selection (as opposed to the URL-effect path) is through `CatalogAutocomplete`'s `onSelect` prop. The mock must forward it. A minimal implementation exposes a `data-testid="catalog-autocomplete-select"` button that calls `onSelect` with a per-test product fixture (e.g. via a small helper or a `data-testid` + `onClick` wired to a module-level mock the test configures), keeping the mock itself simple and stateless.

## Implementation Guidance

### Directory / Module Structure

No new files. All work lands in:
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` (extended)

No changes to:
- `frontend/src/components/pages/ManufactureBatchCalculator.tsx`
- `frontend/src/api/hooks/useManufactureBatch.ts`
- `frontend/src/components/common/CatalogAutocomplete.tsx` (the real component — only its **test-file-local mock** changes)

### Interfaces and Contracts

- `computePercentage(calculatedAmount: number, newBatchSize: number | null | undefined): string` — already exported; FR-1 imports it as-is.
- `useManufactureBatch()` return shape consumed by the mock: `{ getBatchTemplate, calculateBySize, calculateByIngredient, isLoading }` — the mock factory must keep this exact shape (the real hook also returns `error`, which the component doesn't destructure, so it's fine to omit from the mock, matching current behavior).
- Mocked resolved-value shapes must match `CalculatedBatchSizeResponse` / `CalculateBatchByIngredientResponse` field names used by the component: `success`, `productCode`, `productName`, `originalBatchSize`, `newBatchSize`, `scaleFactor`, `ingredients`. Plain object literals are sufficient (per spec's Data Model section) — no need to construct real class instances via `new CalculatedBatchSizeResponse(...)`.
- `CatalogItemDto` fixtures used to drive `onSelect` and to match the URL-effect's auto-constructed instance must use the real generated class (`import { CatalogItemDto, ProductType } from '../../../api/generated/api-client'`) for type-correctness, consistent with how the component itself constructs it (line 127 of the component).

### Data Flow

1. **FR-1** — direct unit calls to `computePercentage`, no rendering, no mocks beyond the file-level ones already present.
2. **FR-2** — render → mocked `CatalogAutocomplete` exposes a trigger → test fires it with a `CatalogItemDto` → `handleProductSelect` runs → mocked `getBatchTemplate` resolves with the test's `newBatchSize`/`originalBatchSize` combination → assert `desiredBatchSize` input value and `mockCalculateBySize` call args (or non-call, for the both-falsy case) via `waitFor`/`findBy*` since these are async state updates following a `Promise` resolution.
3. **FR-3** — render inside `MemoryRouter` with `initialEntries` containing `?productCode=X&batchSize=500` → mount-time `useEffect` fires → same downstream flow as FR-2 but entered via the URL-parsing path instead of the mock's `onSelect` → assert `getBatchTemplate` called with `'X'`, batch-size input shows `'500'`, `calculateBySize` called with `('X', 500)`.
4. **FR-4** — render, select a product (via the FR-2/FR-3 style trigger) to get `template` populated → assert default radio state and input group → `fireEvent.click` the "Podle ingredience" radio → assert DOM swap → fill inputs and click "Vypočítat" → assert correct mocked function invoked, matching function *not* invoked.

All four are `async` tests requiring `await waitFor(...)` / `findBy*` queries around the `getBatchTemplate`/`calculateBySize`/`calculateByIngredient` promise resolutions, consistent with RTL async-testing conventions already implied by the codebase's use of these hooks elsewhere.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Restructuring the `jest.mock('useManufactureBatch', ...)` factory breaks the existing smoke test (which relies on `{ success: false }` default behavior) | Medium | Set the default `mockResolvedValue` for all three mocked functions to `{ success: false }` in `beforeEach` (or as the `jest.fn()` initializer), preserving the existing smoke test's assumptions unchanged; only override per-test where FR-2/3/4 need different data. |
| `jest.mock` factory referencing out-of-scope variables without the `mock` name prefix fails at Jest's hoist-time static analysis (a common gotcha, not specific to this repo but easy to hit here) | Low | Name every mock reference with a `mock` prefix (`mockGetBatchTemplate`, etc.) as shown in Decision 2 — verify by running `npm test` locally before considering the task done, per this repo's validation checklist. |
| FR-2/FR-3 assertions on `desiredBatchSize` depend on async `Promise` resolution timing (state updates happen after two chained `await`s: `getBatchTemplate` then `calculateBySize`) | Medium | Use `await waitFor(...)` (or `findByDisplayValue`) rather than synchronous assertions immediately after firing selection/mount, matching RTL's standard pattern for effect-driven async UI updates. |
| FR-3's `MemoryRouter`/`Route` path is not load-bearing per spec, but an incorrect `Route path` could still prevent the component from mounting at all inside `Routes` | Low | Mirror the precedent exactly: wrap in `<Routes><Route path="/manufacturing/batch-calculator" element={<ManufactureBatchCalculator />} /></Routes>` with a matching `initialEntries` path segment, as PoLinePickStep.test.tsx does. |
| Coverage still under 60% after FR-1–FR-4 | Low | Explicitly out of scope per spec (Out of Scope section) — not a blocker for this task; flag as a known outcome, not a defect. |

## Specification Amendments

None required. The specification (spec.r1.md) is implementation-ready as written: it already specifies exact line references, exact acceptance criteria per FR, the mock-restructuring approach, and explicitly scopes the one production-adjacent change (the `CatalogAutocomplete` mock). The one clarification worth calling out for the implementing developer (not a spec defect, just an amplification):

- **Amendment (clarifying, non-blocking):** The spec's NFR-3 mock-restructuring guidance and Decision 2's `mock`-prefix requirement should be treated as a hard implementation constraint, not just a suggestion — Jest's factory hoisting will throw `ReferenceError: Cannot access 'X' before initialization` or a hoisting lint error otherwise. Call this out in the PR description if a developer unfamiliar with this Jest quirk picks up the task.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed. All dependencies (`@testing-library/react`, `@testing-library/jest-dom`, `react-router-dom` v6.30.4) are already present in `frontend/package.json` and already used elsewhere in the codebase (confirmed via `PoLinePickStep.test.tsx` precedent). Implementation can start immediately.
