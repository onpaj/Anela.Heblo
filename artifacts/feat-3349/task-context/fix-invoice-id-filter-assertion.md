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
