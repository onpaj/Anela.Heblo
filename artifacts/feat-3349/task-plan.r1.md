# Task Plan: Fix Issued-Invoices Filter E2E Selectors

## Overview

Three E2E tests in `frontend/test/e2e/issued-invoices/filters.spec.ts` fail on the nightly staging run. Tests 8 and 9 time out because their checkbox locators use `.filter({ hasText })` on `input[type="checkbox"]` elements that carry no text — the label text lives in a sibling `<span>` inside the wrapping `<label>`. Test 3's else-branch asserts on the full first row's `textContent()`, which is fragile and may not contain "2024" in all cell formats. All three fixes are isolated to a single file.

## Tasks

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

---

### task: fix-invoice-id-filter-assertion

**Goal:** Tighten the else-branch assertion in test 3 to check only the first `<td>` cell of the first row rather than the entire row's text content, so it does not false-fail when invoice IDs containing "2024" appear only in the ID column but other columns happen to repeat the year in different formats.

**Files:**
- `frontend/test/e2e/issued-invoices/filters.spec.ts` — narrow the else-branch assertion in test 3

**Steps:**

1. In test `"3: Invoice ID filter with Enter key"` (lines 62–65), replace:
   ```ts
   const firstRowText = await tableRows.first().textContent();
   // eslint-disable-next-line jest/no-conditional-expect
   expect(firstRowText).toContain("2024");
   ```
   with:
   ```ts
   const firstCellText = await tableRows.first().locator("td").first().textContent();
   // eslint-disable-next-line jest/no-conditional-expect
   expect(firstCellText).toContain("2024");
   ```

**Acceptance criteria:**
- The else-branch in test 3 reads the text of the first `<td>` inside the first `<tr>`, not the entire row.
- The variable is named `firstCellText` (or equivalently clear); `firstRowText` is removed.
- No other lines in the file are modified.
- `npm run lint` passes with no new errors.
