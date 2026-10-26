# Specification: Unit tests for ManufacturingStockAnalysis sales multiplier cookie handling

## Summary
Add frontend unit tests covering cookie-based initialisation of the sales multiplier in `ManufacturingStockAnalysis.tsx` (`getCookie` / `readSalesMultiplierCookie`). Tests pin down the valid range [0.1, 3.0], the fallback to 1.0, and the cookie parsing edge cases. No production behaviour changes.

## Background
Issue #4588 (coverage-gap routine): file line coverage is 47.4%. `readSalesMultiplierCookie` (module-private, lines ~55-61) returns 1.0 when the cookie is absent, NaN, or outside [0.1, 3.0]. A silent reset to 1.0 yields wrong reorder signals with no visible error, so regressions (e.g. `<` vs `<=`) must be caught. Existing tests in `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` already cover: no cookie -> "1.0x", cookie 1.5 -> "1.5x", cookie 0.1 -> minus disabled, cookie 3.0 -> plus disabled, +/- clicks. Not covered: out-of-range values, NaN/garbage, empty value, multiple cookies, values containing `=`/`; `.

## Functional Requirements

### FR-1: Boundary values accepted
Cookie 0.1 renders "0.1x" and cookie 3.0 renders "3.0x" (the displayed value, not only the disabled state).
**Acceptance criteria:**
- Test with cookie `0.1` asserts text "0.1x".
- Test with cookie `3.0` asserts text "3.0x".

### FR-2: Out-of-range values fall back to 1.0
**Acceptance criteria:**
- Cookie `0.09` renders "1.0x".
- Cookie `3.01` renders "1.0x".
- Also covered: `0`, negative (`-1`), and large (`100`) -> "1.0x" (table-driven).

### FR-3: Non-numeric values fall back to 1.0
**Acceptance criteria:**
- Cookie `abc`, `NaN`, and empty value (`name=`) render "1.0x".

### FR-4: Cookie lookup among multiple cookies
**Acceptance criteria:**
- With other cookies set before and after (`a=1; manufacturing-stock-sales-multiplier=2.5; b=2`), "2.5x" renders.
- A cookie whose name merely shares a suffix/prefix (e.g. `x-manufacturing-stock-sales-multiplier=2.5`) is not matched -> "1.0x".

### FR-5: Query receives the initial multiplier
**Acceptance criteria:**
- For a valid cookie (e.g. 2.5) the mocked `useManufacturingStockAnalysisQuery` is called with `salesMultiplier: 2.5`; for an invalid one with `1`.

### FR-6: Documented limitation (optional)
`getCookie` splits on `"; "` and `match.split("=")[1]`, so values containing `=` are truncated. Values written by the app are plain numbers, so this is not reachable. Add at most one test documenting current behaviour (value `1.5=junk` -> parseFloat("1.5") -> "1.5x"); do not change production code.

## Non-Functional Requirements

### NFR-1: Performance
Tests are fast, pure jsdom; reuse existing mocks.

### NFR-2: Security
None; no secrets or network.

### NFR-3: Isolation
Each test must reset the cookie (`max-age=0`) in `beforeEach`/`afterEach` so tests don't leak state.

## Data Model
None.

## API / Interface Design
No production API change. `readSalesMultiplierCookie` stays module-private; tested through the rendered component (consistent with existing tests and CLAUDE.md "surgical changes").

## Dependencies
Jest + React Testing Library (already used), existing mocks in the test file.

## Out of Scope
- Exporting helpers or refactoring cookie code.
- Fixing the `; `/`=` parsing limitation.
- Other uncovered regions of the component.
- E2E tests.

## Open Questions
None.

## Status: COMPLETE
