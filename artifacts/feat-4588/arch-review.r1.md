# Architecture Review: Sales multiplier cookie tests

## Skip Design: true
Test-only change, no UI.

## Architectural Fit Assessment
Fits existing pattern: the page test file `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` already drives cookie scenarios by setting `document.cookie` then rendering with mocked `useManufacturingStockAnalysisQuery`. Verified in source: `readSalesMultiplierCookie` is module-private and used only as the initial `salesMultiplier` filter state.

## Proposed Architecture

### Component Overview
Test file -> render `ManufacturingStockAnalysis` (mocked query hook, grid-layout, CatalogDetail) -> assert displayed "N.Nx" text and the filters passed to the mocked hook.

### Key Design Decisions

#### Decision 1: Test through the component, do not export helpers
**Options considered:** (a) export `readSalesMultiplierCookie`/`getCookie` and unit test directly; (b) extract to a util module; (c) test via render.
**Chosen approach:** (c).
**Rationale:** No production change; matches existing tests; behaviour (display + hook args) is what matters. Mild cost: per-case render, acceptable.

#### Decision 2: Table-driven `it.each` in a new nested `describe("sales multiplier cookie validation")`
Placed inside the existing top-level describe so existing `beforeEach` mocks apply.

## Implementation Guidance

### Directory / Module Structure
Edit only `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx`. Add the describe block next to the existing "reads initial sales multiplier from cookie" test (~line 536).

### Interfaces and Contracts
- Cookie name: `manufacturing-stock-sales-multiplier`.
- Helper in test: `setMultiplierCookie(raw)` and `clearMultiplierCookie()` (`...=; max-age=0; path=/`); `afterEach` clears.
- Display format: `salesMultiplier.toFixed(1) + "x"`.
- Hook arg: `mockUseManufacturingStockAnalysisQuery` first-call arg `objectContaining({ salesMultiplier })`.
- Note: jsdom ignores/stores `document.cookie` values with `;` specially; to set an empty value use `name=`. Setting NaN-like strings is fine.

### Data Flow
cookie string -> getCookie -> parseFloat -> range check -> initial filters.salesMultiplier -> rendered text + hook arg.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Cookie leakage between tests | Medium | clear in beforeEach/afterEach |
| Text "1.0x" ambiguity with other elements | Low | existing tests use `getByText("1.0x")`; same here |
| jsdom cookie quirks for odd values | Low | use simple values; verify locally |

## Specification Amendments
None.

## Prerequisites
`cd frontend && npm ci` available for running jest.
