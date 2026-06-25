**Source:** Nightly E2E triage — run 28147951139 (2026-06-25). Clears **56** failures (entire stock-operations module).

## Problem
Every `stock-operations` E2E test fails in `waitForTableUpdate` (`frontend/test/e2e/helpers/stock-operations-test-helpers.ts:26`) because the page is blank — the failure screenshot shows no sidebar and no content.

## Root cause (confirmed)
Tests navigate to `/stock-operations`, which is **not a real route**. The app route is `/stock-up-operations` (`frontend/src/App.tsx:449`; the sidebar links to `/stock-up-operations` in `frontend/src/components/Layout/Sidebar.tsx:345`). No `/stock-operations` route or redirect exists, so navigation lands on a blank page and the table never renders.

## Scope / files
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` — `navigateToStockOperations` `page.goto(...)` (~line 270): `/stock-operations` → `/stock-up-operations`.
- `frontend/test/e2e/stock-operations/navigation.spec.ts` — `expect(page.url()).toContain('/stock-operations')` (line 13) and the direct `page.goto(.../stock-operations)` (line 96).
- Grep `frontend/test/e2e/stock-operations/` for any other `/stock-operations` occurrences.

## Acceptance criteria
- Stock-operations specs navigate to `/stock-up-operations`.
- The table (or the "Žádné výsledky" empty state) renders.
- The stock-operations module passes against staging.
