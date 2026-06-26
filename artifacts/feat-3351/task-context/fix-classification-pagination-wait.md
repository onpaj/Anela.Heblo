### task: fix-classification-pagination-wait

**Files:**
- Modify: `frontend/test/e2e/core/invoice-classification-history.spec.ts`

**Failing test:** `'pagination functionality'` (line 24) — `nextButton.isDisabled()` on line 61 times out at 30 s. The `beforeEach` calls `page.waitForTimeout(2000)` which is not long enough for the table to render, so `isDisabled()` is called on the button before pagination controls are present.

**Note on empty-state text:** The `beforeEach` in `invoice-classification-history.spec.ts` already uses the shorter string `"Nebyly nalezeny žádné záznamy"` (line 27 inside the test, not the `beforeEach`). The helper `classification-history-helpers.ts` also uses the shorter string (line 26 and line 141). The spec text `"Nebyly nalezeny žádné záznamy klasifikace"` mentioned in the spec was a misquote — use the shorter form already present in the codebase.

**Before (lines 18–21 in `beforeEach`):**
```typescript
    // Wait for content to load - give enough time for table to load or "no records" message to appear
    // The page shows "Loading..." initially, then renders table or empty state
    await page.waitForTimeout(2000);

    console.log('✅ Invoice classification page loaded');
```

**After:**
```typescript
    // Wait for content to load: either the table or the no-records message must be visible.
    // waitForTimeout is unreliable — replace with a deterministic selector wait.
    await page.waitForSelector('table, :text("Nebyly nalezeny žádné záznamy")', { timeout: 15000 });

    console.log('✅ Invoice classification page loaded');
```

Steps:
- [ ] Open `frontend/test/e2e/core/invoice-classification-history.spec.ts`.
- [ ] In the `beforeEach` block (lines 5–22), find:
  ```typescript
      // Wait for content to load - give enough time for table to load or "no records" message to appear
      // The page shows "Loading..." initially, then renders table or empty state
      await page.waitForTimeout(2000);
  ```
- [ ] Replace those three lines with:
  ```typescript
      // Wait for content to load: either the table or the no-records message must be visible.
      // waitForTimeout is unreliable — replace with a deterministic selector wait.
      await page.waitForSelector('table, :text("Nebyly nalezeny žádné záznamy")', { timeout: 15000 });
  ```
- [ ] The `console.log` on line 21 (`'✅ Invoice classification page loaded'`) is preserved unchanged immediately after.
- [ ] The `'pagination functionality'` test already has its own `await expect(tableLocator.or(emptyStateLocator).first()).toBeVisible({ timeout: 15000 })` guard on line 29, which is consistent and does not need to change.
- [ ] Commit:
  ```
  test(e2e): replace fixed timeout in invoice classification beforeEach with deterministic wait

  page.waitForTimeout(2000) was not long enough for the table to render
  on staging, causing pagination assertions to time out. Replace with
  waitForSelector that resolves as soon as the table or no-records
  message appears (up to 15s), matching the pattern in the helper.
  ```
