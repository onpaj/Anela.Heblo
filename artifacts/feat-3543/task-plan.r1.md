# Task Plan: Fix Batch-Planning Error-Handling E2E Tests

## Overview
A single Playwright E2E spec (`batch-planning-error-handling.spec.ts`) fails on staging because its sidebar navigation selector, heading assertion, and load-wait strategy are wrong — it never reaches `/manufacturing/batch-planning`. The fix realigns those three concerns with the proven, passing sibling spec `batch-planning-workflow.spec.ts`. No application/product code changes. This is exactly one developer task.

### task: fix-batch-planning-error-handling-e2e-navigation
## Goal
Make both tests in `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts` reliably navigate to and load the batch-planning page (`/manufacturing/batch-planning`, component `BatchPlanningCalculator`, `<h1>` = "Plánovač výrobních dávek"), so the error-handling scenarios run against the correct page. Preserve all existing behavioral assertions — only navigation, page-load gating, and wait strategy change. The root cause is a test-only defect: the current selector `text=/Plánovač|Kalkulačka dávek/i` never matches the real sidebar link "Plánování dávek" and instead matches "Kalkulačka dávek", which routes to the wrong page `/manufacturing/batch-calculator`.

## Files to change
- **Only** `frontend/test/e2e/manufacturing/batch-planning-error-handling.spec.ts`.

Do NOT touch any of these (confirmed working / out of scope): `ManufactureBatchPlanning.tsx`, `ManufactureBatchCalculator.tsx`, `Sidebar.tsx`, `App.tsx`, `CatalogAutocomplete.tsx`, `test-data.ts`, `playwright.config.ts`, and the passing `batch-planning-workflow.spec.ts`.

Reference the passing sibling `frontend/test/e2e/manufacturing/batch-planning-workflow.spec.ts` for the exact patterns to copy — its navigation, heading assertion, and wait strategy are proven on staging with the same auth and the same `MAS001001M` fixture.

## Changes
Apply these edits to `batch-planning-error-handling.spec.ts`. Read the current file first to locate the exact lines (the navigation block is around line 27, test 1's page-load assertion around line 25, and test 2 around line 248), then make each change surgically.

1. **Navigation selector (FR-1).** Replace the broad/incorrect navigation:
   ```ts
   const batchPlanningLink = page.locator('text=/Plánovač|Kalkulačka dávek/i').first();
   if (await batchPlanningLink.isVisible({ timeout: 5000 })) { ... } else { await page.goto('/manufacturing/batch-planning'); }
   ```
   with the role-based, accessible-name approach used by the workflow spec:
   ```ts
   await page.getByRole('button', { name: 'Výroba' }).click();
   await page.getByRole('link', { name: /plánování dávek/i }).click();
   ```
   `/plánování dávek/i` is the primary matcher — it matches the real sidebar label and, critically, does NOT match "Kalkulačka dávek" (which routes to the wrong page). You may include `/plánovač výrobních dávek|plánování dávek/i` for exact parity with the workflow spec, but that is optional. If a `page.goto('/manufacturing/batch-planning')` fallback is retained, invoke it ONLY when the link is genuinely absent (link `isVisible` is false) — never as a silent recovery after clicking a link. After navigation `page.url()` must end with `/manufacturing/batch-planning`.

2. **Heading assertion (FR-2).** Replace the permissive assertion:
   ```ts
   page.locator('h1, h2').filter({ hasText: /Plánovač|Planning|Dávek|Kalkulačka/i })
   ```
   with the exact one from the workflow spec:
   ```ts
   await expect(page.locator('h1').filter({ hasText: /plánovač výrobních dávek/i })).toBeVisible({ timeout: 10000 });
   ```
   This must NOT accept "Kalkulačka…" so a wrong-page landing fails loudly at the navigation step. Apply this heading guard in **both** tests. Test 2 (around line 248) currently has NO page-loaded guard before it touches the combobox — you MUST add this exact-`h1` assertion there after navigation (per arch-review clarification 1, this is required, not optional).

3. **Load-wait strategy (FR-4).** Replace every `page.waitForLoadState('networkidle')` in this file (in `beforeEach` and after navigation) with `page.waitForLoadState('domcontentloaded')`, matching the workflow spec. Staging emits background telemetry/polling that keeps `networkidle` from settling. Rely on the existing `toBeVisible({ timeout })` element waits for real synchronization; keep any modest `waitForTimeout` React-init pauses the workflow spec uses.

4. **Preserve everything else (FR-3, FR-5).** Do NOT change: the combobox interaction (`[role="combobox"]` → type "Hedvábný pan Jasmín" → select first `[role="option"]` → assert `tbody tr` rows > 0), the `TestCatalogItems.hedvabnyPan` / `MAS001001M` fixture usage, or the "missing test data" `throw` guard (it must `throw`, never `test.skip`). Keep test 1's behavior (check fixed checkboxes, set `9999` over-volume quantity, click "Přepočítat" — button matched by `/Přepočítat|Calculate|Vypočítat/i` — assert table/rows remain visible; toaster/visual-indicator checks stay as they are today, logged-only if currently non-critical). Keep test 2's behavior (set reasonable `10`, recalculate, assert no error toaster via `.toast, .Toastify__toast, [role="alert"]` filtered by error text). Do NOT weaken any assertion to make tests pass.

5. **FR-6 (shared nav helper) is explicitly deferred** — do NOT extract a `navigateToBatchPlanning` helper. It must not block or expand this fix.

6. Continue authenticating via `navigateToApp(page)` (already in the spec) — never `createE2EAuthSession()` alone (CLAUDE.md rule).

## Verification
1. **Lint (must pass):** from `frontend/`, run `npm run lint`.
2. **E2E run against staging (per CLAUDE.md testing docs):** run `./scripts/run-playwright-tests.sh manufacturing`.
   - Both error-handling tests in `batch-planning-error-handling.spec.ts` must pass:
     - Test 1: "fixed products exceed volume" (checks fixed checkboxes, sets `9999`, recalculates, table/rows remain visible).
     - Test 2: "correct fixed quantities and recalculate" (sets `10`, recalculates, asserts no error toaster).
   - The sibling `batch-planning-workflow.spec.ts` must remain green (no regression).
3. **Spot checks:** after navigation each test lands on a URL ending in `/manufacturing/batch-planning`; the exact `<h1>` "Plánovač výrobních dávek" is visible; the `[role="combobox"]` selection of `MAS001001M` populates the product table (`tbody tr` > 0). If the exact-heading assertion ever fails, it now surfaces the wrong-page problem immediately at the navigation step rather than as a downstream combobox timeout.

Prerequisites (already satisfied, no action needed): staging `https://heblo.stg.anela.cz` reachable with valid E2E auth (`E2E_CLIENT_ID` / `E2E_CLIENT_SECRET` / `AZURE_TENANT_ID`) and the `MAS001001M` semiproduct present with configured products — all proven by the passing workflow spec. No migrations, config, feature flags, or infrastructure changes.
