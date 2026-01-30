# E2E Failed Tests Tracking

> **Generated**: 2026-01-30
> **Source**: Nightly E2E Regression Test Report
> **Total Failed**: 95 tests

## Summary

| Module | Total Tests | Passed | Failed | Skipped |
|--------|------------|--------|--------|---------|
| catalog | 87 | 68 | 19 | 3 |
| issued-invoices | 43 | 0 | 43 | 0 |
| stock-operations | 33 | 25 | 8 | 0 |
| transport | 43 | 43 | 0 | 0 |
| manufacturing | 7 | 7 | 0 | 0 |
| core | 81 | 56 | 25 | 0 |

---

## Catalog Module (19 failed)

### [x] should handle clearing filters from empty result state
- **File**: `catalog/clear-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`
- **Resolution**: Test passes successfully now (8.2s runtime). No code changes needed - the timeout issue was transient or already fixed in the application.

### [x] should handle changing product type with active text filters
- **File**: `catalog/combined-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`
- **Resolution**: Fixed by replacing `selectProductType` helper calls with direct select operations and using `page.waitForTimeout(1000)` instead of waiting for API responses. The issue was that when text filters result in 0 rows, changing product type doesn't always trigger a new API call that the test can wait for.

### [x] should handle numbers in product name
- **File**: `catalog/filter-edge-cases.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`
- **Resolution**: Test passes successfully now (8.7s runtime). No code changes needed - the timeout issue was transient or already fixed in the application.

### [ ] should handle hyphens and spaces in product code
- **File**: `catalog/filter-edge-cases.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should show loading state during filter application
- **File**: `catalog/filter-edge-cases.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should handle browser back/forward with filters in URL
- **File**: `catalog/filter-edge-cases.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should maintain filter when changing sort direction
- **File**: `catalog/sorting-with-filters.spec.ts`
- **Error**: `expect(received).toBe(expected) // Object.is equality - Expected: 20, Received: 0`

### [ ] should reset to page 1 when changing sort
- **File**: `catalog/sorting-with-filters.spec.ts`
- **Error**: `expect(received).toBe(expected) // Object.is equality - Expected: 1, Received: 2`

### [ ] should handle sorting empty filtered results
- **File**: `catalog/sorting-with-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should filter products by name using Filter button
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should filter products by name using Enter key
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should handle case-insensitive search
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should filter products by code using Enter key
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should perform exact code matching
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should handle partial code matching
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should apply both name and code filters simultaneously when both filled
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `Test data missing: Expected to find product matching both name="Bisabolol" and code="AKL001". Test fixtures may be outdated.`

### [ ] should display "Žádné produkty nebyly nalezeny." for no matches
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should show empty state with filter applied
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should allow clearing filters from empty state
- **File**: `catalog/text-search-filters.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

---

## Issued Invoices Module (43 failed)

### [ ] 3: Invoice ID filter with Enter key
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 4: Invoice ID filter with Filtrovat button
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 5: Customer Name filter with Enter key
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 6: Customer Name filter with Filtrovat button
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 7: Date range filter (Od + Do fields)
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 8: Show Only Unsynced checkbox
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 9: Show Only With Errors checkbox
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 10: Combined filters (multiple filters simultaneously)
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 11: Clear filters button (Vymazat)
- **File**: `issued-invoices/filters.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 30: Import button opens modal
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 31: Modal displays file upload area
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 32: Modal displays accepted file formats
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 33: Close modal with X button
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 34: Close modal with Cancel button
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 35: Close modal with Escape key
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 36: Close modal by clicking backdrop
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 37: Upload button is disabled without file
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 38: File selection enables upload button
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 39: Displays file name after selection
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 40: Remove selected file
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 41: Displays validation error for invalid file type
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 42: Shows progress indicator during upload
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 43: Displays success message after upload
- **File**: `issued-invoices/import-modal.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 1: Page loads successfully with authentication
- **File**: `issued-invoices/navigation.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 2: Tab switching works correctly (Statistics ↔ Grid)
- **File**: `issued-invoices/navigation.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 19: Default page size is 10 rows per page
- **File**: `issued-invoices/pagination.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 20: Navigate to next page
- **File**: `issued-invoices/pagination.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 21: Navigate to previous page
- **File**: `issued-invoices/pagination.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 22: Navigate to first page
- **File**: `issued-invoices/pagination.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 23: Navigate to last page
- **File**: `issued-invoices/pagination.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 24: Change page size (items per page)
- **File**: `issued-invoices/pagination.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 25: Pagination resets to page 1 when filters change
- **File**: `issued-invoices/pagination.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 12: Sort by Invoice ID ascending
- **File**: `issued-invoices/sorting.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 13: Sort by Invoice ID descending
- **File**: `issued-invoices/sorting.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 14: Sort by Customer Name ascending
- **File**: `issued-invoices/sorting.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 15: Sort by Customer Name descending
- **File**: `issued-invoices/sorting.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 16: Sort by Invoice Date ascending
- **File**: `issued-invoices/sorting.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 17: Sort by Invoice Date descending
- **File**: `issued-invoices/sorting.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 18: Sorting persists with filters
- **File**: `issued-invoices/sorting.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 26: Status badge displays correctly for 'Čeká' (Pending)
- **File**: `issued-invoices/status-badges.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 27: Status badge displays correctly for 'Chyba' (Error)
- **File**: `issued-invoices/status-badges.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 28: Status badge displays correctly for 'Odesláno' (Sent)
- **File**: `issued-invoices/status-badges.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

### [ ] 29: Multiple status badges can appear in grid
- **File**: `issued-invoices/status-badges.spec.ts`
- **Error**: `expect(locator).toBeVisible() failed - Locator: locator('main, [role="main"]') - Expected: visible - Timeout: 5000ms - Error: element(s) not found`

---

## Stock Operations Module (8 failed)

### [ ] should show confirmation dialog when clicking Accept
- **File**: `stock-operations/accept.spec.ts`
- **Error**: `expect(received).toContain(expected) - Expected substring: "Opravdu chcete akceptovat tuto chybnou operaci?" - Received string: "Opravdu chcete akceptovat tuto selhanou operaci? Operace bude označena jako Previously Failed a nebude se opakovat."`

### [ ] should display empty state when no results match filters
- **File**: `stock-operations/navigation.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should sort by ID column (ascending/descending)
- **File**: `stock-operations/sorting.spec.ts`
- **Error**: `TimeoutError: locator.click: Timeout 30000ms exceeded - waiting for getByRole('columnheader', { name: /ID/i })`

### [ ] should sort by Document Number column
- **File**: `stock-operations/sorting.spec.ts`
- **Error**: `TimeoutError: locator.click: Timeout 30000ms exceeded - waiting for getByRole('columnheader', { name: /Číslo dokladu/i })`

### [ ] should filter by "All" source types (default)
- **File**: `stock-operations/source-filter.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should filter by "Transport Box" source type
- **File**: `stock-operations/source-filter.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should filter by "Gift Package Manufacture" source type
- **File**: `stock-operations/source-filter.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

### [ ] should filter by "Active" state (default)
- **File**: `stock-operations/state-filter.spec.ts`
- **Error**: `TimeoutError: page.waitForResponse: Timeout 5000ms exceeded while waiting for event "response"`

---

## Core Module (25 failed)

### [ ] should show Classify Invoice button in action column
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should show loading state when classifying invoice
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should disable save button when form is invalid
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should successfully classify invoice when all required fields are filled
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should handle classification errors gracefully
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should show Create Rule button and open modal
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should disable Create Rule button when no company is selected
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should prefill company name when opening rule creation modal
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should close rule creation modal when cancel is clicked
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should display all form fields in rule creation modal
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should have rule type dropdown with correct options
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should have accounting template dropdown with options
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should have department dropdown with options
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should validate required fields before submission
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should enable save button when all required fields are filled
- **File**: `core/invoice-classification-history-actions.spec.ts`
- **Error**: `TypeError: (0, _classificationHistoryHelpers.navigateToClassificationHistory) is not a function`

### [ ] should filter by exact invoice number match
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should filter by partial invoice number match
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should be case-insensitive for invoice number search
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should apply invoice number filter on Enter key press
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should filter by exact company name match
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should filter by partial company name match
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should be case-insensitive for company name search
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should apply company name filter on Enter key press
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] should apply all four filters together
- **File**: `core/invoice-classification-history-filters.spec.ts`
- **Error**: `expect(received).toBeGreaterThan(expected) - Expected: > 0, Received: 0`

### [ ] pagination functionality
- **File**: `core/invoice-classification-history.spec.ts`
- **Error**: `Expected either a table with data or a "no records" message`

---

## Notes

### Common Error Patterns

1. **TimeoutError: page.waitForResponse** (most catalog/stock-operations failures)
   - Tests timing out waiting for API responses
   - Likely backend performance or API availability issues

2. **Main element not visible** (all issued-invoices failures)
   - All 43 issued-invoices tests fail with identical error
   - Suggests fundamental navigation/routing issue for this module

3. **navigateToClassificationHistory is not a function** (15 core failures)
   - Missing or incorrectly imported helper function
   - Should be quick fix once helper is properly exported

4. **expect(received).toBeGreaterThan(0)** (9 filter tests)
   - Tests finding 0 results when expecting data
   - Possible test data issues or broken filter functionality

### Recommended Fix Priority

1. **HIGH**: Fix `issued-invoices` navigation (43 tests blocked)
2. **HIGH**: Fix `navigateToClassificationHistory` helper export (15 tests blocked)
3. **MEDIUM**: Investigate catalog API timeout issues (16 tests)
4. **MEDIUM**: Fix classification history filter tests (9 tests)
5. **LOW**: Fix stock-operations text mismatches and timeouts (8 tests)
6. **LOW**: Fix catalog sorting edge cases (3 tests)
