### task: harden-stock-operations-error-state-e2e-test

**Files:**
- Modify: `frontend/test/e2e/stock-operations/navigation.spec.ts:83-115`
- Modify: `frontend/test/e2e/helpers/stock-operations-test-helpers.ts:22-27`

**Goal:** Fix the case-mismatched route-intercept glob and replace the soft, no-op-on-failure
assertion in the `'should display error state on API failure'` test so it actually exercises and
verifies the error path, independent of Task 1's fix.

- [ ] Step 1: Open `frontend/test/e2e/stock-operations/navigation.spec.ts` and replace the
  `'should display error state on API failure'` test body (currently lines 83-115) — replace this
  exact block:
  ```typescript
    // Intercept API calls and force failure
    await page.route('**/api/stock-up-operations**', route => {
      route.abort('failed');
    });

    // Navigate to stock operations page (will trigger failed API call)
    const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/stock-up-operations`);
    await page.waitForTimeout(3000);

    // Check for error message
    const errorMessage = page.locator('text="Chyba při načítání operací"');
    const isErrorVisible = await errorMessage.isVisible();

    if (isErrorVisible) {
      console.log('   ✅ Error state displayed');

      // Verify retry button exists
      const retryButton = page.getByRole('button', { name: /Zkusit znovu/i });
      await expect(retryButton).toBeVisible();
      console.log('   ✅ Retry button present');
    } else {
      console.log('   ℹ️ Error state not triggered (possible caching)');
    }
  ```
  with:
  ```typescript
    // Intercept API calls and force failure.
    // The generated client builds this.baseUrl + "/api/StockUpOperations?" (PascalCase, no
    // dashes — see frontend/src/api/generated/api-client.ts:12051), so the glob must match
    // that literal casing or the route never intercepts and the real request goes through.
    await page.route('**/api/StockUpOperations**', route => {
      route.abort('failed');
    });

    // Navigate to stock operations page (will trigger failed API call)
    const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/stock-up-operations`);

    // Check for error message — hard assertion, so the test fails (not silently passes)
    // if the intercepted/aborted request doesn't produce the error UI.
    const errorMessage = page.locator('text="Chyba při načítání operací"');
    await expect(errorMessage).toBeVisible({ timeout: 15000 });
    console.log('   ✅ Error state displayed');

    // Verify retry button exists
    const retryButton = page.getByRole('button', { name: /Zkusit znovu/i });
    await expect(retryButton).toBeVisible();
    console.log('   ✅ Retry button present');
  ```
  Note this removes the `await page.waitForTimeout(3000);` line — the hard `expect(...).toBeVisible({ timeout: 15000 })` on the next line already waits, making the fixed timeout redundant and flake-prone.

- [ ] Step 2: Confirm the edited test file has no leftover references to the old
  `isErrorVisible` variable or the old glob pattern:
  ```
  grep -n "stock-up-operations\|isErrorVisible" frontend/test/e2e/stock-operations/navigation.spec.ts
  ```
  Expect no matches (the only `stock-up-operations` occurrences left should be the URL path
  `/stock-up-operations`, not the API glob — re-check the grep output line by line, since that URL
  string will legitimately still match `stock-up-operations` as a substring; confirm no
  `**/api/stock-up-operations**` glob remains).

- [ ] Step 3: Type-check the edited spec file so the change is syntactically valid (this repo has
  no dedicated `tsconfig.json` under `frontend/test/e2e/`, so type-check directly against the root
  config):
  ```
  cd frontend && npx tsc --noEmit -p tsconfig.json --skipLibCheck test/e2e/stock-operations/navigation.spec.ts
  ```
  If this reports unrelated pre-existing errors elsewhere in the E2E suite (common, since E2E specs
  aren't part of the production build/tsconfig include list), confirm there are zero errors
  reported specifically for `navigation.spec.ts` and move on — E2E specs are not covered by
  `npm run build`, so this step is a best-effort syntax check, not a hard gate.

- [ ] Step 4: In `frontend/test/e2e/helpers/stock-operations-test-helpers.ts`, harden the shared
  `waitForTableUpdate` wait so it fails fast with a clear message instead of the generic 15s timeout
  whenever the page lands on the error card instead of rows/empty-state — this directly prevents a
  repeat of the exact failure mode this ticket investigated (56 tests all timing out with the same
  unhelpful "element not found" error). Replace this exact block:
  ```typescript
  export async function waitForTableUpdate(page: Page): Promise<void> {
    // Wait for either at least one data row or the empty-state message to appear
    await expect(
      page.locator('tbody tr').first().or(page.locator('h3').filter({ hasText: 'Žádné výsledky' }))
    ).toBeVisible({ timeout: 15000 });
  }
  ```
  with:
  ```typescript
  export async function waitForTableUpdate(page: Page): Promise<void> {
    // Wait for a data row, the empty-state message, or the error card to appear. Failing fast
    // on the error card (instead of only on the generic 15s timeout) gives a clear diagnostic
    // — "Chyba při načítání operací" almost always means the E2E principal lacks a required
    // permission (see feat-3540) rather than a genuine UI bug.
    const success = page.locator('tbody tr').first().or(page.locator('h3').filter({ hasText: 'Žádné výsledky' }));
    const errorHeading = page.locator('h3').filter({ hasText: 'Chyba při načítání operací' });
    const result = success.or(errorHeading);
    await expect(result).toBeVisible({ timeout: 15000 });

    if (await errorHeading.isVisible().catch(() => false)) {
      throw new Error(
        'Stock Operations page rendered the error card ("Chyba při načítání operací") instead ' +
        'of data rows or the empty state. This usually means the caller lacks a required ' +
        'permission (e.g. warehouse.stock_up.read) rather than a genuine data/UI bug.'
      );
    }
  }
  ```

- [ ] Step 5: Confirm no other spec file relies on `waitForTableUpdate` resolving successfully when
  the page is in the error state (it shouldn't — the dedicated error-state test in Step 1 asserts
  on `errorMessage` directly, not via this helper):
  ```
  grep -rln "waitForTableUpdate" frontend/test/e2e/stock-operations/
  ```
  Read each matching file's usage and confirm none of them intentionally expect an error state at
  the point they call `waitForTableUpdate` (the error-state test itself, from Step 1, does not call
  `waitForTableUpdate` at all, so it is unaffected by this change).

- [ ] Step 6: This repo's `npm run lint` script (`eslint src --ext .ts,.tsx`) only covers
  `frontend/src`, not `frontend/test/e2e`, so it will not pick up these two files — skip it for
  this task and rely on Step 3's type-check plus manual review of the diff instead.

- [ ] Step 7: Commit with message `fix(e2e): correct StockUpOperations route-intercept casing and use hard assertions in error-state test (feat-3540)`.

---
