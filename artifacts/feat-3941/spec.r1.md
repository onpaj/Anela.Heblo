# Specification: ManufactureBatchCalculator test coverage for computePercentage edge cases and batch-size fallback

## Summary
`frontend/src/components/pages/ManufactureBatchCalculator.tsx` has 29.3% line coverage against a 60% threshold. This spec defines the unit and component tests needed to close the gap by exercising four specific, currently-untested behaviors: `computePercentage` boundary conditions, the MMQ/BOM batch-size fallback order, URL-parameter-driven product auto-selection, and the batch-size/ingredient calculation-mode toggle. No production code changes are required — this is a test-only addition.

## Background
An existing test file (`frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx`) already covers most `computePercentage` edge cases (null, undefined, zero, negative, NaN, normal values, rounding) and a single smoke-render test for the empty state. It does **not** cover:
1. The explicit `Infinity`/`-Infinity` (non-finite but non-NaN) input to `computePercentage`.
2. The `templateData.newBatchSize || templateData.originalBatchSize || 0` fallback precedence used when a template loads.
3. The `?productCode=X&batchSize=Y` URL-driven auto-selection flow (the entry point from `ManufactureOrder`).
4. The batch-size ↔ ingredient calculation-mode toggle and which calculation function each mode invokes.

`computePercentage` (lines 15–21) drives the "%" column operators use to sanity-check ingredient ratios during manufacturing; a regression here produces a silently wrong or `'N/A'` value on the shop floor. The batch-size fallback (lines 89–92, mirrored at line 179 in `resetCalculation`) determines which default quantity a manufacturing operator sees when a template first loads — a swapped precedence silently proposes the wrong batch size. The URL flow (lines 116–136) is how `ManufactureOrder` hands off to this calculator; a broken auto-selection means the operator lands on an empty calculator instead of a pre-loaded one.

This spec targets **test additions only**, appended to (or extending) the existing test file, following the file's established mocking conventions (`jest.mock` of `useManufactureBatch`, `CatalogAutocomplete`, `InventoryStatusCell`, `ManufactureInventoryDetail`, `CatalogDetail`) and the repo's established `MemoryRouter`-with-`initialEntries` pattern for URL-driven tests (see `frontend/src/components/terminal/lot-identification/__tests__/PoLinePickStep.test.tsx` for precedent).

## Functional Requirements

### FR-1: `computePercentage` non-finite edge case
Add the one edge case from the brief's "non-finite" wording not yet covered by the existing suite: `Infinity` (and, for symmetry, `-Infinity`) as `newBatchSize`. The existing tests cover `null`, `undefined`, `0`, negative numbers, and `NaN`, but `NaN` is not the only non-finite value the `!isFinite(newBatchSize)` guard exists for.

**Acceptance criteria:**
- `computePercentage(100, Infinity)` returns `'N/A'`.
- `computePercentage(100, -Infinity)` returns `'N/A'`.

### FR-2: Batch-size fallback order (MMQ wins, then BOM, then zero)
Verify the precedence `templateData.newBatchSize || templateData.originalBatchSize || 0` used in `handleProductSelect` (lines 89–92) when a template successfully loads for a selected product. This must be tested as a component test: mock `useManufactureBatch().getBatchTemplate` to resolve with a `CalculatedBatchSizeResponse`-shaped object (`success: true`, plus `newBatchSize`/`originalBatchSize`), trigger product selection through the mocked `CatalogAutocomplete`'s `onSelect` callback, and assert on the resulting `desiredBatchSize` input value and/or the arguments `calculateBySize` was called with.

Three precedence cases must be covered:
1. Both `newBatchSize` (e.g. `100`) and `originalBatchSize` (e.g. `200`) present and truthy → the batch-size input is pre-filled with `100` and `calculateBySize` is called with `(productCode, 100)`.
2. `newBatchSize` absent/`0` (falsy) and `originalBatchSize` present (e.g. `200`) → the batch-size input is pre-filled with `200` and `calculateBySize` is called with `(productCode, 200)`.
3. Both `newBatchSize` and `originalBatchSize` absent/`0` → the batch-size input is empty (`""`), and `calculateBySize` is **not** called (the `batchSizeToUse > 0` guard at line 95 prevents the auto-calculation call).

**Acceptance criteria:**
- Case 1 (MMQ present) results in batch size `100`, not `200`.
- Case 2 (MMQ falsy, BOM present) results in batch size `200`.
- Case 3 (both falsy) results in an empty batch-size field and no `calculateBySize` call.

### FR-3: URL parameter auto-selection
Verify the `useEffect` at lines 117–136: when the component mounts with `?productCode=X&batchSize=Y` in the URL and no product is yet selected, it must construct a `CatalogItemDto` (`productCode: X`, `productName: X` as a placeholder, `type: ProductType.SemiProduct`) and drive it through the same `handleProductSelect` flow as a manual selection — including that the URL's `batchSize` overrides whatever the loaded template would otherwise default to.

This is a component test using `MemoryRouter` with `initialEntries={['/manufacturing/batch-calculator?productCode=X&batchSize=500']}` (route path is not load-bearing for this component since it reads `location.search` directly, but should mirror how the component is actually mounted, e.g. via a `Route`). Mock `getBatchTemplate` to resolve with a template whose `newBatchSize` differs from the URL's batch size (e.g. template `newBatchSize: 1000`, URL `batchSize=500`), so the override is unambiguous.

**Acceptance criteria:**
- On mount with `?productCode=X&batchSize=500` and no prior selection, `getBatchTemplate` is called with `'X'`.
- The batch-size input is pre-filled with `'500'` (the URL value), not `'1000'` (the template's `newBatchSize`).
- `calculateBySize` is called with `('X', 500)` — the URL value, confirming the override — not `1000`.
- The auto-selected product's placeholder name renders where `selectedProduct` is used before the template resolves (e.g., product code `X` is visible), since `productName` is seeded as `productCode` until template data arrives (the component does not currently overwrite `selectedProduct.productName` from template data — this is existing, unchanged behavior and should be asserted as-is, not treated as a defect).
- If `selectedProduct` is already set (simulated by prior manual selection in the same render), the effect does not re-trigger auto-selection (guarded by `!selectedProduct` at line 122) — a lower-priority case, include if practical, otherwise document as not covered.

### FR-4: Calculation-mode toggle
Verify the `calculationMode` radio toggle (lines 280–306) once a template is loaded (the toggle only renders when `template` is truthy). Correcting the brief's wording: the implementation does not *disable* the other mode's inputs — it conditionally renders only one input group at a time via a ternary (lines 314–416), so switching modes unmounts the previous mode's inputs entirely rather than disabling them. Tests must assert against the actual behavior (absence from the DOM), not a `disabled` attribute.

Cover:
1. Default mode on template load is `"batch-size"` — the batch-size input (label "Požadovaná velikost dávky (g)") is present and the ingredient controls (label "Ingredience" / "Požadované množství (g)") are absent.
2. Clicking the "Podle ingredience" radio switches to ingredient mode — the ingredient select and amount input become present, and the batch-size input becomes absent.
3. In batch-size mode, filling the batch-size input and clicking "Vypočítat" invokes `calculateBySize` (not `calculateByIngredient`).
4. In ingredient mode, selecting an ingredient, filling the amount, and clicking "Vypočítat" invokes `calculateByIngredient` (not `calculateBySize`).

**Acceptance criteria:**
- Radio for "Podle velikosti dávky" is checked by default once a template loads.
- After clicking "Podle ingredience", the batch-size input (`Požadovaná velikost dávky (g)`) is no longer in the document, and the ingredient `<select>` and amount input are present instead.
- Triggering the compute action in batch-size mode calls the mocked `calculateBySize` and does not call `calculateByIngredient`.
- Triggering the compute action in ingredient mode calls the mocked `calculateByIngredient` and does not call `calculateBySize`.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a test-only change. The added component tests should each run in well under 1 second per RTL convention in this codebase (no real network calls; all API-touching hooks are mocked).

### NFR-2: Security
Not applicable — no production code, auth, or data-sensitivity surface is touched.

### NFR-3: Test isolation and determinism
- All tests must mock `useManufactureBatch` (and, where a test triggers the "Tisk receptury" / PDF path indirectly, `useSemiproductRecipePdf` if needed — though none of FR-1–FR-4 require exercising that path) rather than hitting `getAuthenticatedApiClient`.
- Tests must not depend on execution order; each test that needs a product selected must set up its own mocked `CatalogAutocomplete` interaction or URL, independent of other tests in the file.
- The `CatalogAutocomplete` mock must be extended (from the current inert `<div data-testid="catalog-autocomplete" />`) to accept and invoke its `onSelect` prop so FR-2 and FR-4 tests can simulate a manual product selection. This mock enhancement is in scope for this change since FR-2/FR-4 cannot be tested without it.

## Data Model
No new or changed data entities. Tests will construct instances of existing generated DTOs:
- `CatalogItemDto` (`productCode`, `productName`, `type: ProductType.SemiProduct`) — for the auto-selected product (FR-3) and for driving the enhanced `CatalogAutocomplete` mock's `onSelect` (FR-2, FR-4).
- `CalculatedBatchSizeResponse`-shaped plain objects (`success`, `productCode`, `productName`, `originalBatchSize`, `newBatchSize`, `scaleFactor`, `ingredients: []`) — as the resolved value of the mocked `getBatchTemplate` and `calculateBySize`.
- `CalculateBatchByIngredientResponse`-shaped plain object (`success`, plus the ingredient-mode fields) — as the resolved value of the mocked `calculateByIngredient` for FR-4.

Mocks may use plain object literals matching these shapes (as the existing test file already does with `{ success: false }`) rather than constructing full class instances, since the component only reads properties off the resolved value and never calls DTO instance methods on it.

## API / Interface Design
No API or interface changes. Test-facing surface only:
- `computePercentage` — already exported from the component module; FR-1 imports it exactly as the existing tests do.
- `ManufactureBatchCalculator` (default export) — rendered via RTL for FR-2/FR-3/FR-4, wrapped in `BrowserRouter` (FR-2, FR-4, matching existing convention) or `MemoryRouter` with `initialEntries` (FR-3, for URL-parameter control).
- The `useManufactureBatch` mock's `getBatchTemplate`, `calculateBySize`, `calculateByIngredient` become per-test configurable (via `jest.fn().mockResolvedValue(...)` or `mockResolvedValueOnce`) rather than the current fixed `{ success: false }` returned for every test — the existing top-level `jest.mock` factory should be restructured (e.g., via `jest.fn()` references captured in an outer scope, reset in `beforeEach`) so individual tests can override return values without interfering with each other.

## Dependencies
- `@testing-library/react`, `@testing-library/jest-dom` — already in use.
- `react-router-dom`'s `MemoryRouter`, `Route`, `Routes` (v6.30.4, already a project dependency) — needed for FR-3's URL-parameter control; precedent in `frontend/src/components/terminal/lot-identification/__tests__/PoLinePickStep.test.tsx`.
- No new npm packages required.

## Out of Scope
- Any production code changes to `ManufactureBatchCalculator.tsx` or `useManufactureBatch.ts` — this is a test-only change. (The one narrow exception, per NFR-3, is extending the local `CatalogAutocomplete` jest mock inside the test file, not the real component.)
- Testing the "Tisk receptury" (PDF export) button, `handleGoToBatchPlanning` navigation, `handleIngredientClick`/`handleInventoryClick` modals, or the phase-grouping row rendering (lines 582–598) — none of these were called out in the brief and are not part of the four listed gaps.
- Reaching the full 60% coverage threshold is the outcome of implementing FR-1–FR-4 but is not itself a separate requirement to design around; if these four areas' tests still leave coverage under 60%, that is a signal for a follow-up coverage pass, not a reason to expand this spec's scope speculatively.
- Visual/snapshot testing, accessibility audits, and E2E coverage of this component — out of scope for a unit/component-test coverage gap fix.

## Open Questions
None.

## Status: COMPLETE
