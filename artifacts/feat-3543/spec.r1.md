# Specification: Fix Batch-Planning Error-Handling E2E Tests (Calculator/Modal Never Renders)

## Summary
Two E2E tests in `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts` fail on staging because the batch-planning page (heading + product combobox) never renders for them, while the sibling `batch-planning-workflow.spec.ts` passes. Root cause is a **test-side navigation/selector defect**: the error-handling tests locate the sidebar entry with `text=/Plánovač|Kalkulačka dávek/i`, which does not match the real batch-planning link "Plánování dávek" and instead only matches "Kalkulačka dávek" — a *different* feature that routes to `/manufacturing/batch-calculator`. The fix is to align the error-handling tests' navigation, heading, and load-wait strategy with the proven, passing workflow test so they reliably reach `/manufacturing/batch-planning`.

## Background

The manufacturing module exposes two visually similar but distinct pages, wired in `frontend/src/App.tsx`:

- `/manufacturing/batch-calculator` → `ManufactureBatchCalculator` (component `frontend/src/components/pages/ManufactureBatchCalculator.tsx`, `<h1>` = "Kalkulačka dávek pro výrobu"). Sidebar label: **"Kalkulačka dávek"** (`Sidebar.tsx`, id `kalkulator-davek`).
- `/manufacturing/batch-planning` → `BatchPlanningCalculator` (component `frontend/src/components/pages/ManufactureBatchPlanning.tsx`, `<h1>` = "Plánovač výrobních dávek"). Sidebar label: **"Plánování dávek"** (`Sidebar.tsx`, id `planovani-davek`). This is the page under test — it renders the semiproduct/product `react-select` combobox, the product table, fixed-quantity checkboxes, and the "Přepočítat" recalculation used by the error-handling scenario.

In the sidebar's "Výroba" section, "Kalkulačka dávek" appears **before** "Plánování dávek" in DOM order.

The **passing** `batch-planning-workflow.spec.ts` navigates correctly:
```ts
await page.getByRole('button', { name: 'Výroba' }).click();
await page.getByRole('link', { name: /plánovač výrobních dávek|plánování dávek/i }).click();
// heading assertion:
page.locator('h1').filter({ hasText: /plánovač výrobních dávek/i });
```
It also waits with `waitForLoadState('domcontentloaded')`.

The **failing** `batch-planning-error-handling.spec.ts` navigates incorrectly:
```ts
await page.getByRole('button', { name: 'Výroba' }).click();
const batchPlanningLink = page.locator('text=/Plánovač|Kalkulačka dávek/i').first();
if (await batchPlanningLink.isVisible({ timeout: 5000 })) {
  await batchPlanningLink.click();
} else {
  await page.goto('/manufacturing/batch-planning');
}
// heading assertion (too permissive):
page.locator('h1, h2').filter({ hasText: /Plánovač|Planning|Dávek|Kalkulačka/i });
```
It waits with `waitForLoadState('networkidle')`.

### Why it fails
1. The regex alternatives are `Plánovač` and `Kalkulačka dávek`. The real link text is **"Plánování dávek"** — it contains neither `Plánovač` (note the different suffix: *-ování* vs *-ovač*) nor `Kalkulačka dávek`. So the intended link is **never matched**.
2. The only sidebar item that *does* match is **"Kalkulačka dávek"**, which navigates to the wrong route `/manufacturing/batch-calculator`. That page has no product table / fixed-quantity flow the error-handling test needs, so the test cannot reach the calculator/modal state it asserts on. Depending on staging menu visibility, the click may also land the app somewhere with no matching heading at all (the `.first()` fallback `page.goto('/manufacturing/batch-planning')` only runs when the wrong link is *not* visible — an inconsistent, fragile branch).
3. The heading assertion `/Plánovač|Planning|Dávek|Kalkulačka/i` is too loose: it would also pass on the calculator page ("Kalkulačka…"), masking the wrong-page navigation and making the failure harder to diagnose.
4. `waitForLoadState('networkidle')` is flaky on staging (telemetry / polling requests keep the network active); the passing test uses `domcontentloaded`.

Both `react-select` comboboxes (calculator and planning pages) expose `role="combobox"` via `CatalogAutocomplete` (`frontend/src/components/common/CatalogAutocomplete.tsx`), so `[role="combobox"]` is a valid selector *once the correct page is loaded*. The Playwright `baseURL` is `https://heblo.stg.anela.cz` (`frontend/playwright.config.ts`), so `page.goto('/manufacturing/batch-planning')` resolves correctly. Test data (`MAS001001M` / "Hedvábný pan Jasmín", `TestCatalogItems.hedvabnyPan`) is confirmed to exist on staging because the workflow test uses it successfully.

This is a **test-only bug**: the product feature works (proven by the passing workflow test). No application/product code change is required.

## Functional Requirements

### FR-1: Correct sidebar navigation to the batch-planning page
Both failing tests must navigate to `/manufacturing/batch-planning` (the `BatchPlanningCalculator` page), not `/manufacturing/batch-calculator`. Navigation must use the same robust, role-based approach as the passing workflow test.

**Acceptance criteria:**
- Navigation clicks the "Výroba" section button, then a link resolved by accessible name matching the real label, e.g. `page.getByRole('link', { name: /plánování dávek/i })` (optionally also allowing `/plánovač výrobních dávek/i` for parity with the workflow test).
- The navigation selector no longer matches "Kalkulačka dávek" and never routes to `/manufacturing/batch-calculator`.
- After navigation, `page.url()` ends with `/manufacturing/batch-planning`.
- If a direct-URL fallback is retained, it targets `/manufacturing/batch-planning` and is only used when the link is genuinely absent (not as a masked recovery from clicking the wrong link).

### FR-2: Precise page-load (heading) assertion
The heading assertion must uniquely identify the batch-planning page and fail loudly if the wrong page loads.

**Acceptance criteria:**
- Heading is asserted via `page.locator('h1').filter({ hasText: /plánovač výrobních dávek/i })` (matching the actual `<h1>` "Plánovač výrobních dávek"), consistent with the workflow test.
- The assertion no longer accepts "Kalkulačka…" (i.e., it would fail on the calculator page), so a wrong-page regression is caught immediately.
- Assertion passes on staging within the existing 10s timeout for both tests (test at line 25 explicitly; the correction test at line 248 should add/keep an equivalent page-loaded guard before interacting with the combobox).

### FR-3: Reliable combobox interaction
Once on the correct page, both tests must locate and drive the semiproduct/product `react-select` combobox to select `MAS001001M` / "Hedvábný pan Jasmín".

**Acceptance criteria:**
- The combobox is located via `role="combobox"` (react-select renders this role) and is visible within timeout on the batch-planning page.
- Selecting the fixture semiproduct yields `[role="option"]` results and populates the product table (`tbody tr` rows > 0), consistent with the existing test steps.
- The existing "test data missing" guard `throw`s (does not silently skip) when no options appear.

### FR-4: Stable load-state waiting
Replace flaky `networkidle` waits in this spec's `beforeEach` and navigation steps with the load strategy proven on staging.

**Acceptance criteria:**
- `beforeEach` uses `waitForLoadState('domcontentloaded')` (matching the workflow spec) rather than `networkidle`.
- Post-navigation waits do not rely on `networkidle` to gate the heading/combobox assertions.

### FR-5: Preserve error-handling scenario coverage
The behavioral intent of both tests must be preserved after the navigation fix.

**Acceptance criteria:**
- Test 1 ("fixed products exceed volume") still: checks fixed checkboxes, sets an over-volume quantity (e.g. `9999`), clicks "Přepočítat", and asserts the table/rows remain visible; toaster/visual-indicator checks remain (non-critical warnings may stay as logged-only as they are today, unless FR-6 tightens them).
- Test 2 ("correct fixed quantities and recalculate") still: sets a reasonable quantity (e.g. `10`), recalculates, and asserts no error toaster (`.toast, .Toastify__toast, [role="alert"]` filtered by error text) appears.
- No assertions are weakened merely to make the tests pass; changes are limited to navigation, page-load gating, and wait strategy.

### FR-6: (Optional hardening) Shared navigation helper
To prevent recurrence and drift between the two manufacturing specs, extract the batch-planning navigation into a reusable helper.

**Acceptance criteria:**
- If implemented, a helper (e.g. `navigateToBatchPlanning(page)` under `frontend/test/e2e/helpers/`) encapsulates the "Výroba" → "Plánování dávek" click + heading wait, and both error-handling tests (and optionally the workflow test) use it.
- If not implemented, this is explicitly deferred; it must not block the fix.

## Non-Functional Requirements

### NFR-1: Performance
- No change to application runtime performance (test-only change).
- Both tests should complete well within Playwright's per-test timeout on staging; removing `networkidle` should reduce flaky long waits.

### NFR-2: Security
- No security impact. No new secrets, no auth changes. Tests continue to authenticate via `navigateToApp(page)` (per `helpers/e2e-auth-helper` and the CLAUDE.md rule that E2E auth must go through `navigateToApp()`).

### NFR-3: Reliability / Maintainability
- Selectors must be resilient to unrelated UI text changes: prefer role + accessible-name matching over broad `text=` regexes.
- Regexes used for navigation must be verified against the actual sidebar labels in `frontend/src/components/Layout/Sidebar.tsx` ("Plánování dávek", "Kalkulačka dávek") to avoid accidental cross-matching between the two manufacturing pages.

## Data Model
No data-model changes. Relevant existing entities/fixtures only:
- `TestCatalogItems.hedvabnyPan` = `{ code: 'MAS001001M', name: 'Hedvábný pan Jasmín', type: 'Polotovar' }` (`frontend/test/e2e/fixtures/test-data.ts`) — the semiproduct selected in both tests; confirmed present on staging via the passing workflow test.

## API / Interface Design
No API changes. Relevant UI/routing surface (for grounding, unchanged):
- Route map (`frontend/src/App.tsx`): `/manufacturing/batch-planning` → `BatchPlanningCalculator`; `/manufacturing/batch-calculator` → `ManufactureBatchCalculator` (both guarded by `RequireMenuPath`).
- Sidebar labels (`frontend/src/components/Layout/Sidebar.tsx`): section "Výroba"; items "Kalkulačka dávek" (calculator) and "Plánování dávek" (planning).
- Page landmarks on the planning page: `<h1>` "Plánovač výrobních dávek"; `react-select` combobox (`role="combobox"`) from `CatalogAutocomplete`; product `<table>`; "Přepočítat" button.

## Dependencies
- Playwright E2E harness and `frontend/playwright.config.ts` (`baseURL` = `https://heblo.stg.anela.cz`).
- `frontend/test/e2e/helpers/e2e-auth-helper` (`navigateToApp`).
- Staging environment reachable with valid E2E auth and containing the `MAS001001M` semiproduct with configured products.
- E2E run script `./scripts/run-playwright-tests.sh` (per CLAUDE.md) for verification.

## Out of Scope
- Any change to application/product code (`ManufactureBatchPlanning.tsx`, `ManufactureBatchCalculator.tsx`, `Sidebar.tsx`, routing, or `CatalogAutocomplete`) — the feature is confirmed working.
- Fixing or refactoring the passing `batch-planning-workflow.spec.ts` or other manufacturing specs beyond the optional shared-helper adoption in FR-6.
- Adding `data-testid` attributes to the sidebar or page (could be a future hardening; not required and would touch product code).
- Broadening error-handling coverage (new toaster/validation assertions) beyond restoring the existing intent.

## Open Questions
None. (Assumptions taken, all low-risk: (1) the correct fix mirrors the passing workflow test — verified against both spec files; (2) `MAS001001M` test data exists on staging — verified via the passing workflow test using the same fixture; (3) `role="combobox"` is valid — verified in `CatalogAutocomplete` via react-select. Final confirmation is the staging E2E run per the validation step.)

## Status: COMPLETE
