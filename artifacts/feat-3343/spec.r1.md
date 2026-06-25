# Specification: Fix Stock-Operations E2E Navigation URL

## Summary

Every E2E test in the `stock-operations` module fails because test helpers and specs navigate to `/stock-operations`, which does not exist as a route. The real application route is `/stock-up-operations`. This specification covers the surgical URL corrections required across the test helper and spec files to restore the full 56-test module against staging.

## Background

A nightly E2E run (28147951139, 2026-06-25) reported 56 failures across the entire `stock-operations` module. All failures originate in `waitForTableUpdate` because the page is blank — the browser lands on an unmatched route, React renders nothing, and neither a `tbody tr` row nor a `h3[Žádné výsledky]` element ever appears.

Root-cause analysis confirmed that `frontend/src/App.tsx:449` registers the page at `/stock-up-operations`, and `frontend/src/components/Layout/Sidebar.tsx:345` links to the same path. No alias, redirect, or catch-all exists for `/stock-operations`. The test code was written with a truncated path and has never matched the live application.

## Functional Requirements

### FR-1: Correct the navigation helper

The `navigateToStockOperations` function in `frontend/test/e2e/helpers/e2e-auth-helper.ts` (line 270) calls `page.goto` with the path `/stock-operations`. This must be changed to `/stock-up-operations`.

**Acceptance criteria:**
- `navigateToStockOperations` navigates to `{baseUrl}/stock-up-operations`.
- After navigation, `page.url()` contains `/stock-up-operations`.
- The sidebar is visible and the stock-operations page content renders.

### FR-2: Correct the direct `page.goto` in navigation.spec.ts

`frontend/test/e2e/stock-operations/navigation.spec.ts` line 96 contains a bare `page.goto(`${baseUrl}/stock-operations`)` inside the "should display error state on API failure" test. This path must be changed to `/stock-up-operations`.

**Acceptance criteria:**
- The error-state test navigates to `{baseUrl}/stock-up-operations`.
- The API interception route `**/api/stock-up-operations**` (already correct at line 90) continues to match correctly.

### FR-3: Correct URL assertion in navigation.spec.ts

`frontend/test/e2e/stock-operations/navigation.spec.ts` line 13 asserts `expect(page.url()).toContain('/stock-operations')`. Because `/stock-up-operations` still contains the substring `/stock-operations`, this assertion would not fail — but it is semantically misleading. The assertion must be tightened to `/stock-up-operations` for clarity and to prevent future false-positives if a misrouted URL somehow passes the substring check.

**Acceptance criteria:**
- The URL assertion reads `expect(page.url()).toContain('/stock-up-operations')`.

### FR-4: Correct URL assertions in all remaining spec files

The following files each contain `expect(page.url()).toContain('/stock-operations')` in their first `test` block. All must be updated to `/stock-up-operations` for the same reasons as FR-3.

Files (one occurrence each, all in the first test/beforeEach navigation assertion):
- `frontend/test/e2e/stock-operations/badges.spec.ts` — line 15
- `frontend/test/e2e/stock-operations/accept.spec.ts` — line 13
- `frontend/test/e2e/stock-operations/state-filter.spec.ts` — line 14
- `frontend/test/e2e/stock-operations/source-filter.spec.ts` — line 13
- `frontend/test/e2e/stock-operations/sorting.spec.ts` — line 14
- `frontend/test/e2e/stock-operations/retry.spec.ts` — line 15
- `frontend/test/e2e/stock-operations/panel.spec.ts` — line 18

**Acceptance criteria:**
- Every occurrence of `.toContain('/stock-operations')` in the stock-operations module is replaced with `.toContain('/stock-up-operations')`.
- No other spec files outside this module are affected.

### FR-5: Full grep sweep for remaining occurrences

Before declaring the fix complete, grep `frontend/test/e2e/stock-operations/` for any remaining literal string `/stock-operations` (excluding `/stock-up-operations`) to catch occurrences not identified by line-number in the brief.

**Acceptance criteria:**
- `grep -r "'/stock-operations'" frontend/test/e2e/stock-operations/` returns zero matches.
- `grep -r '"/stock-operations"' frontend/test/e2e/stock-operations/` returns zero matches.
- The same grep against `frontend/test/e2e/helpers/e2e-auth-helper.ts` returns zero matches.

## Non-Functional Requirements

### NFR-1: Scope containment

No application source files (`frontend/src/`, `backend/`) are to be modified. This is a pure test-file correction. The application route `/stock-up-operations` is correct and must not be renamed.

### NFR-2: No collateral changes

Each edited line must change only the URL string. Do not reformat, re-order imports, add comments, or alter any logic beyond the string replacement.

### NFR-3: Verification

The fix is verified by running the E2E suite against staging via `./scripts/run-playwright-tests.sh` targeting the `stock-operations` module. All 56 previously failing tests must pass or produce a legitimate test-data failure (not a blank-page navigation failure).

## Data Model

Not applicable. This change touches only test navigation strings; no data model is affected.

## API / Interface Design

Not applicable. The backend API path `**/api/stock-up-operations**` is already correct in the tests and is unchanged.

## Dependencies

- Staging environment `https://heblo.stg.anela.cz` must be reachable and have test data present for the stock-up-operations module.
- No library changes or new dependencies.

## Out of Scope

- Adding a `/stock-operations` redirect in the React router. There is no user-facing need for the old path — it only existed as a test mistake.
- Changes to any backend controllers, DTOs, or API routes.
- Changes to E2E tests outside the `stock-operations/` module directory and the `navigateToStockOperations` helper.
- Fixing any other pre-existing test failures unrelated to the blank-page navigation issue.

## Open Questions

None.

## Status: COMPLETE
