### task: fix-checkbox-locators

**Goal:** Replace the broken `.filter({ hasText })` checkbox selectors in tests 8 and 9 with `page.getByLabel()` so Playwright resolves the label-wrapped inputs correctly.

**Files:**
- `frontend/test/e2e/issued-invoices/filters.spec.ts` — replace two checkbox locator expressions

**Steps:**

1. In test `"8: Show Only Unsynced checkbox"` (lines 179–181), replace:
   ```ts
   const unsyncedCheckbox = page
     .locator('input[type="checkbox"]')
     .filter({ hasText: "Nesync" });
   ```
   with:
   ```ts
   const unsyncedCheckbox = page.getByLabel("Nesync");
   ```

2. In test `"9: Show Only With Errors checkbox"` (lines 204–206), replace:
   ```ts
   const errorsCheckbox = page
     .locator('input[type="checkbox"]')
     .filter({ hasText: "Chyby" });
   ```
   with:
   ```ts
   const errorsCheckbox = page.getByLabel("Chyby");
   ```

**Acceptance criteria:**
- `page.getByLabel("Nesync")` is the locator for `unsyncedCheckbox` in test 8.
- `page.getByLabel("Chyby")` is the locator for `errorsCheckbox` in test 9.
- No other lines in the file are modified.
- `npm run lint` passes with no new errors.
