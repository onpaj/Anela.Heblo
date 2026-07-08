# Design: Fix Batch-Planning Error-Handling E2E Tests

## Component Design

No application or UI components change. The only artifact modified is the test file
`frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts` (both `test(...)` blocks
and the shared `beforeEach`). No new files are required unless FR-6 (optional, deferred) is
adopted, in which case a helper `navigateToBatchPlanning(page)` would live under
`frontend/test/e2e/helpers/`.

Changes to make in the spec file, mirroring the proven, passing sibling
`frontend/test/e2e/manufacturing/batch-planning-workflow.spec.ts`:

- **Navigation selector**: replace the broad/incorrect
  `page.locator('text=/Plánovač|Kalkulačka dávek/i').first()` with role-based, accessible-name
  matching: `page.getByRole('button', { name: 'Výroba' }).click()` then
  `page.getByRole('link', { name: /plánování dávek/i }).click()`. Keep a
  `page.goto('/manufacturing/batch-planning')` fallback only for the case where the link is
  genuinely absent — never as a silent recovery from having clicked the wrong link.
- **Heading assertion**: replace the permissive `page.locator('h1, h2').filter({ hasText:
  /Plánovač|Planning|Dávek|Kalkulačka/i })` with the exact
  `page.locator('h1').filter({ hasText: /plánovač výrobních dávek/i })`, applied after navigation
  in **both** tests (test 2, currently missing this guard entirely, gets it added per FR-2/arch
  review clarification 1).
- **Load-wait strategy**: replace `page.waitForLoadState('networkidle')` with
  `page.waitForLoadState('domcontentloaded')` in `beforeEach` and after navigation, matching the
  workflow spec; rely on existing `toBeVisible({ timeout })` element waits for synchronization.
- **Combobox / table / recalculation interactions, fixture usage
  (`TestCatalogItems.hedvabnyPan` / `MAS001001M`), and the "missing test data" `throw` guard are
  unchanged** — these already work once the correct page loads.
- **Assertions preserved (FR-5)**: test 1 (fixed products exceed volume) still checks fixed
  checkboxes, sets `9999`, clicks "Přepočítat", and asserts table/rows remain visible; test 2
  (correct fixed quantities) still sets `10`, recalculates, and asserts no error toaster appears.
  No assertions are weakened — only navigation, page-load gating, and wait strategy change.

Explicitly out of scope / untouched: `ManufactureBatchPlanning.tsx`,
`ManufactureBatchCalculator.tsx`, `Sidebar.tsx`, `App.tsx`, `CatalogAutocomplete.tsx`,
`test-data.ts`, `playwright.config.ts`, and `batch-planning-workflow.spec.ts`.

## Data Schemas

Not applicable — no database, API, or event-payload changes. No new or modified data schemas;
the existing `TestCatalogItems.hedvabnyPan` fixture (`code: 'MAS001001M'`,
`name: 'Hedvábný pan Jasmín'`, `type: 'Polotovar'`) is reused unchanged.
