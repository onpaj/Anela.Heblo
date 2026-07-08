# Task Plan: Fix Catalog Page Navigation E2E Regression (navigateToCatalog)

## Overview

This is a single, tightly-scoped bug fix confined to one function — `navigateToCatalog` in `frontend/test/e2e/helpers/e2e-auth-helper.ts` (lines 234-260) — with no product-code changes and no changes to any other helper or spec file. The spec, arch-review, and design all agree on the same three-part fix and the same target pattern (mirror `navigateToTransportBoxes`/`navigateToTransportBoxReceive` in the same file), so there is nothing to decompose: splitting this into multiple tasks would just fragment one cohesive edit. This plan is therefore a single task that restructures the function per FR-1/NFR-1/NFR-2 of `spec.r1.md` and Decisions 1-3 of `arch-review.r1.md`.

### task: fix-navigate-to-catalog-fallback

**Goal:** Restructure `navigateToCatalog` so the direct-navigation fallback runs whenever UI navigation did not already confirm landing on `/catalog` (not only inside `catch`), raise the `isVisible` timeouts for "Produkty"/"Katalog" from 2000ms to 5000ms to match sibling helpers, and throw a descriptive error if the URL still doesn't contain `/catalog` after both the UI path and the fallback have been attempted.

**Files:**
- `frontend/test/e2e/helpers/e2e-auth-helper.ts` (modify lines 234-260, the `navigateToCatalog` function only)

**Details:**

Replace the current `navigateToCatalog` function (current lines 234-260):

```ts
export async function navigateToCatalog(page: any): Promise<void> {
  await navigateToApp(page);

  // Navigate to catalog via UI
  // First, try to find and click on "Produkty" section
  const produktySelector = page.locator('text="Produkty"').first();
  try {
    if (await produktySelector.isVisible({ timeout: 2000 })) {
      await produktySelector.click();
      await waitForLoadingComplete(page);

      // Then click on "Katalog" sub-item
      const katalog = page.locator('text="Katalog"').first();
      if (await katalog.isVisible({ timeout: 2000 })) {
        await katalog.click();
        await page.waitForLoadState('domcontentloaded');
        await waitForLoadingComplete(page);
      }
    }
  } catch (e) {
    // If UI navigation fails, go directly to the path
    const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/catalog`);
    await page.waitForLoadState('domcontentloaded');
    await waitForLoadingComplete(page);
  }
}
```

with:

```ts
export async function navigateToCatalog(page: any): Promise<void> {
  await navigateToApp(page);

  // Navigate to catalog via UI
  // First, try to find and click on "Produkty" section
  const produktySelector = page.locator('text="Produkty"').first();
  try {
    console.log('🧭 Attempting UI navigation to catalog via Produkty...');
    if (await produktySelector.isVisible({ timeout: 5000 })) {
      console.log('✅ Found Produkty menu item, clicking...');
      await produktySelector.click();
      await waitForLoadingComplete(page);

      // Then click on "Katalog" sub-item
      const katalog = page.locator('text="Katalog"').first();
      if (await katalog.isVisible({ timeout: 5000 })) {
        console.log('✅ Found Katalog submenu, clicking...');
        await katalog.click();
        await page.waitForLoadState('domcontentloaded');
        await waitForLoadingComplete(page);

        // Verify we actually landed on the catalog page (a RequireMenuPath
        // redirect-to-"/" on insufficient permission would still resolve the
        // click without an exception, so a URL check is required here).
        if (page.url().includes('/catalog')) {
          console.log('✅ UI navigation successful');
          return;
        }
        console.log('❌ Katalog click did not land on /catalog, current URL:', page.url());
      } else {
        console.log('❌ Katalog submenu not found under Produkty');
      }
    } else {
      console.log('❌ Produkty menu item not found');
    }
  } catch (e) {
    console.log('❌ UI navigation failed:', e.message);
  }

  // Unconditional fallback — reached on an isVisible timeout-miss, a UI click
  // that didn't land on /catalog, and a thrown exception alike.
  console.log('🔄 Trying direct navigation to catalog...');
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/catalog`);
  await page.waitForLoadState('domcontentloaded');
  await waitForLoadingComplete(page);

  // Self-verification: turn a silent no-op into a fast, diagnosable failure
  // instead of leaving the caller on a non-/catalog URL.
  if (!page.url().includes('/catalog')) {
    throw new Error(
      `navigateToCatalog: failed to reach /catalog via UI navigation (Produkty > Katalog) or direct goto fallback (${baseUrl}/catalog); final URL was ${page.url()}`
    );
  }

  console.log('✅ Direct navigation to catalog completed');
}
```

Key points the developer must preserve exactly:
- The `try` block now only performs UI navigation and, on confirmed success (`page.url().includes('/catalog')` after the "Katalog" click), `return`s early — mirroring `navigateToTransportBoxes` (lines 178-232) and `navigateToTransportBoxReceive` (lines 277-319) in the same file.
- The `catch` block only logs (`console.log('❌ UI navigation failed:', e.message);`) and falls through — it must **not** perform navigation itself anymore, since that logic now lives unconditionally below.
- The fallback `page.goto` block sits **after** the `try/catch`, unconditionally, so it runs whenever the `try` block didn't already `return` — whether because `isVisible` timed out (returned `false`), the "Katalog" click landed somewhere other than `/catalog` (e.g. a `RequireMenuPath` redirect to `/`), or an exception was thrown and caught.
- `baseUrl` resolution is unchanged: `process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'`.
- The final `if (!page.url().includes('/catalog'))` throw is new behavior (per spec FR-1 item 3 / NFR-2) — it must reference both attempted paths and the final URL in the message, as shown, so a failure is diagnosable at the helper call site instead of surfacing as a generic downstream assertion failure in each of the 9 spec files.
- Do not touch any other function in this file (`navigateToApp`, `navigateToTransportBoxes`, `navigateToStockOperations`, `navigateToTransportBoxReceive`, `navigateToInvoiceClassification`, `navigateToIssuedInvoices`, `navigateToMarketingCalendar`) and do not touch `frontend/test/e2e/catalog/*.spec.ts` — their existing `expect(page.url()).toContain('/catalog')` assertions remain valid and are now redundant-but-harmless.
- Do not modify `frontend/src/components/Layout/Sidebar.tsx`, `frontend/src/App.tsx`, `frontend/src/components/auth/RequireMenuPath.tsx`, or `frontend/src/auth/PermissionsContext.tsx` — all four were inspected during spec/arch-review and confirmed correctly wired; this fix is scoped entirely to the test helper.

**Validation (no isolated unit test harness exists for this Playwright helper, so validate via read-through plus staging verification):**

1. **Targeted read-through against all four failure/success paths** — before moving on, trace the new function by hand against each scenario and confirm the code does what's claimed:
   - *UI success*: `produktySelector` visible within 5000ms → click → `katalog` visible within 5000ms → click → `page.url()` contains `/catalog` → early `return`, fallback block never runs.
   - *Timeout miss* (the actual staging failure mode from the incident, per spec Background item 1): `produktySelector.isVisible({ timeout: 5000 })` resolves to `false` (no throw) → both `if` bodies skipped → `try` completes normally → `catch` does not run → falls through past the `try/catch` into the unconditional fallback → `page.goto(.../catalog)` → self-check passes since goto succeeded → function returns normally without throwing.
   - *Click lands off-`/catalog`* (e.g. `RequireMenuPath` redirect if permissions were insufficient): "Katalog" click resolves, but `page.url()` does not contain `/catalog` → no early return → falls through to fallback goto → if the fallback also fails to land on `/catalog` (e.g. genuine permission denial), the final `if (!page.url().includes('/catalog'))` throws the descriptive error.
   - *Thrown exception* (e.g. strict-mode locator violation): caught, logged, falls through to the same unconditional fallback as the timeout-miss case.
   Confirm none of these four paths can return without first satisfying `page.url().includes('/catalog')`, and that the only way out without that guarantee is the explicit `throw`.
2. **Type-check the change** — run `cd frontend && npx tsc --noEmit` (or the project's existing TS check, if `tsconfig.json` scopes differently for `test/e2e`) to confirm the restructured function compiles cleanly (no implicit-any / unreachable-code issues from moving the fallback block).
3. **Lint** — run `cd frontend && npm run lint` and confirm no new violations are introduced in `e2e-auth-helper.ts`.
4. **Staging verification** (required per `docs/testing/playwright-e2e-testing.md` — there is no sandbox for this class of timing issue): run `./scripts/run-playwright-tests.sh catalog` from the repo root against `https://heblo.stg.anela.cz` and confirm:
   - All 9 catalog spec files' `beforeEach`/setup `expect(page.url()).toContain('/catalog')` assertions pass (84 tests total: `filter-edge-cases.spec.ts` 17, `text-search-filters.spec.ts` 16, `combined-filters.spec.ts` 13, `pagination-with-filters.spec.ts` 13, `clear-filters.spec.ts` 10, `sorting-with-filters.spec.ts` 10, `product-type-filter.spec.ts` 3, `margins-chart.spec.ts` 1, `ui.spec.ts` 1).
   - The suite's total run time doesn't regress by more than a few seconds per test (per NFR-1 — the 2000ms→5000ms change only adds latency on the worst-case "not visible" branch, so no multi-minute regression is expected).
   - No regression in `navigateToTransportBoxes`, `navigateToStockOperations`, or `navigateToTransportBoxReceive` — these are untouched, but a targeted run of their specs (e.g. `./scripts/run-playwright-tests.sh transport`, `./scripts/run-playwright-tests.sh stock-operations`) confirms no incidental breakage.
5. **FR-2 out-of-band note** (not a code change, but required before closing the issue per spec FR-2/acceptance criteria): capture in the PR description whether the E2E service principal was confirmed to hold `products.catalog.read` (per `frontend/src/auth/accessMatrix.generated.ts`) and whether the permissions fetch was observed to be abnormally slow in `?e2e=true` mode during the staging run in step 4. If the principal is missing the permission or the fetch is abnormally slow, file that as a separate follow-up ticket rather than silently working around it by raising the timeout alone.
