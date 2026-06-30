# Implementation: fix-invoice-id-filter-assertion

## What was implemented

Narrowed the else-branch assertion in test 3 ("Invoice ID filter with Enter key") so that it reads the text content of only the first `<td>` cell in the first row rather than the entire row. This prevents false failures when the year "2024" appears in non-ID columns (e.g., date fields formatted differently) but the invoice ID column still correctly contains the filter term.

The variable was renamed from `firstRowText` to `firstCellText` to match the narrowed scope.

## Files created/modified

- `frontend/test/e2e/issued-invoices/filters.spec.ts` — replaced `tableRows.first().textContent()` with `tableRows.first().locator("td").first().textContent()` and renamed the variable from `firstRowText` to `firstCellText` in the else-branch of test 3 (lines 63–65). No other lines were touched.

## Tests

N/A — this IS the test fix

## How to verify

1. Run the E2E suite against staging: `./scripts/run-playwright-tests.sh`
2. Confirm test "3: Invoice ID filter with Enter key" passes even when invoice rows contain "2024" in date columns but the ID column value is the primary match.
3. Alternatively, inspect the diff: `git show HEAD -- frontend/test/e2e/issued-invoices/filters.spec.ts`

## Notes

No deviations. The change is exactly as specified: only the two lines in the else-branch of test 3 were modified. Tests 4–11 and all other content remain untouched.

`npm run lint` targets `src/` only, so the E2E test file is out of scope for lint — no lint issues introduced.

## PR Summary

Tighten the else-branch assertion in the "Invoice ID filter with Enter key" E2E test so it checks only the first `<td>` cell rather than the entire row text. This prevents false failures when "2024" appears in date or other columns on rows where the invoice ID column correctly matches the filter.

### Changes

- `frontend/test/e2e/issued-invoices/filters.spec.ts` — narrowed assertion to `tableRows.first().locator("td").first().textContent()`; renamed variable from `firstRowText` to `firstCellText`

## Status

DONE
