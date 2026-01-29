# IssuedInvoices E2E Test Suite

Comprehensive end-to-end test coverage for the **Vydané faktury** (Issued Invoices) feature.

## Overview

This test suite validates the complete functionality of the IssuedInvoices page, including navigation, filtering, sorting, pagination, status badges, and data import workflows.

**Total Test Coverage: 43 tests**

## Test Files

### 1. Navigation and Tab Switching (2 tests)
**File:** `issued-invoices-navigation.spec.ts`

Tests basic navigation and tab switching functionality:
- ✅ Page loads successfully with authentication
- ✅ Tab switching between Statistics and Grid views

**Key Validations:**
- URL contains `/customer/issued-invoices`
- Page title "Vydané faktury" is visible
- Statistics tab is active by default
- Tab switching updates active state correctly
- Content changes when switching tabs

### 2. Filter Functionality (9 tests)
**File:** `issued-invoices-filters.spec.ts`

Tests all filter types and combinations:
- ✅ Invoice ID filter with Enter key
- ✅ Invoice ID filter with Filtrovat button
- ✅ Customer Name filter with Enter key
- ✅ Customer Name filter with Filtrovat button
- ✅ Date range filter (Od + Do fields)
- ✅ Show Only Unsynced checkbox
- ✅ Show Only With Errors checkbox
- ✅ Combined filters (multiple filters simultaneously)
- ✅ Clear filters button (Vymazat)

**Key Validations:**
- Filters apply correctly via Enter key
- Filters apply correctly via button click
- Multiple filters work together
- Clear button resets all filters
- Empty results show "Žádné faktury nebyly nalezeny"

### 3. Sorting Functionality (7 tests)
**File:** `issued-invoices-sorting.spec.ts`

Tests sorting on all sortable columns:
- ✅ Sort by Invoice ID (ascending/descending)
- ✅ Sort by Issue Date (ascending/descending)
- ✅ Sort by Customer Name (ascending/descending)
- ✅ Sort by Total Amount (ascending/descending)
- ✅ Sort by Status (ascending/descending)
- ✅ Multiple sort cycles (asc → desc → neutral)
- ✅ Sort indicators update correctly

**Key Validations:**
- Sort icons change (↑ ↓ ↕)
- Data reorders correctly
- Multiple sort cycles work
- Sort state persists during navigation

### 4. Pagination (7 tests)
**File:** `issued-invoices-pagination.spec.ts`

Tests pagination controls and behavior:
- ✅ Pagination controls visible when needed
- ✅ Next page button works
- ✅ Previous page button works
- ✅ First page button works
- ✅ Last page button works
- ✅ Page size dropdown works
- ✅ Pagination updates after filtering

**Key Validations:**
- Correct number of rows per page
- Page navigation updates URL
- Disabled states (first/last page)
- Page size changes update display
- Pagination state survives filter changes

### 5. Status Badges (4 tests)
**File:** `issued-invoices-status-badges.spec.ts`

Tests status badge rendering and colors:
- ✅ Status badge types render correctly
- ✅ Pending status (Čeká) shows yellow badge
- ✅ Synced status (Synchronizováno) shows green badge
- ✅ Error status (Chyba) shows red badge

**Key Validations:**
- Badge visibility in table
- Correct color coding
- Badge text accuracy
- Multiple badge states

### 6. Import Modal (14 tests)
**File:** `issued-invoices-import-modal.spec.ts`

Tests the invoice import workflow:
- ✅ Import modal opens via button
- ✅ Modal closes via Cancel button
- ✅ Modal closes via X button
- ✅ File selection clears previous files
- ✅ Upload button disabled without files
- ✅ Upload button enabled with files
- ✅ Successful file upload shows progress
- ✅ Upload success shows confirmation
- ✅ Multiple file upload validation
- ✅ Drag and drop file upload
- ✅ Error handling for invalid files
- ✅ Progress bar updates during upload
- ✅ Modal resets after successful upload
- ✅ Cancel during upload aborts request

**Key Validations:**
- Modal state management
- File input validation
- Upload progress tracking
- Error message display
- Success notification
- Multi-file handling
- Drag-and-drop support

## Running Tests

### Run All IssuedInvoices Tests
```bash
# From frontend directory
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/issue-311-e2e-issued-invoices/frontend

# Run all tests in customer folder
npx playwright test test/e2e/customer/

# Or use the test script
./scripts/run-playwright-tests.sh customer
```

### Run Specific Test Files
```bash
# Navigation tests only
npx playwright test test/e2e/customer/issued-invoices-navigation.spec.ts

# Filter tests only
npx playwright test test/e2e/customer/issued-invoices-filters.spec.ts

# Sorting tests only
npx playwright test test/e2e/customer/issued-invoices-sorting.spec.ts

# Pagination tests only
npx playwright test test/e2e/customer/issued-invoices-pagination.spec.ts

# Status badge tests only
npx playwright test test/e2e/customer/issued-invoices-status-badges.spec.ts

# Import modal tests only
npx playwright test test/e2e/customer/issued-invoices-import-modal.spec.ts
```

### Run Specific Tests by Name
```bash
# Run test matching pattern
npx playwright test --grep "Invoice ID filter"
npx playwright test --grep "Tab switching"
npx playwright test --grep "Pagination"
```

## Authentication

All tests use the `navigateToIssuedInvoices()` helper which:
1. Authenticates via service principal (backend + frontend session)
2. Navigates to the app root
3. Opens the Zákazník (Customer) menu
4. Clicks on Vydané faktury (Issued Invoices)

**CRITICAL:** Never use `createE2EAuthSession()` alone - always use `navigateToIssuedInvoices()` to ensure full authentication.

## Test Structure

### Common Patterns

**Standard Test Structure:**
```typescript
import { test, expect } from '@playwright/test';
import { navigateToIssuedInvoices } from '../helpers/e2e-auth-helper';
import { waitForLoadingComplete } from '../helpers/wait-helpers';

test.describe('IssuedInvoices - Feature Name', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);

    // Switch to Grid tab if needed
    const gridTab = page.locator('button:has-text("Seznam")');
    await gridTab.click();
    await waitForLoadingComplete(page);
  });

  test('should perform action', async ({ page }) => {
    // Test implementation
  });
});
```

**Waiting for Content:**
```typescript
// Wait for loading to complete
await waitForLoadingComplete(page);

// Wait for table rows
const tableRows = page.locator('tbody tr');
await expect(tableRows.first()).toBeVisible({ timeout: 10000 });

// Wait for specific content
const pageTitle = page.locator('h1:has-text("Vydané faktury")');
await expect(pageTitle).toBeVisible({ timeout: 10000 });
```

**Handling Empty Results:**
```typescript
const tableRows = page.locator('tbody tr');
const filteredCount = await tableRows.count();

if (filteredCount === 0) {
  const emptyMessage = page.locator('text="Žádné faktury nebyly nalezeny."');
  await expect(emptyMessage).toBeVisible();
} else {
  // Verify results
}
```

## Test Environment

**Target:** Staging environment (`https://heblo.stg.anela.cz`)
- Tests run against deployed staging environment
- Uses real Microsoft Entra ID authentication
- Tests with production-like data

**Prerequisites:**
- Valid E2E credentials in `frontend/.env.test`
- Staging environment must be healthy
- Internet connection required

## Common Selectors

### Page Elements
```typescript
// Page title
page.locator('h1:has-text("Vydané faktury")')

// Tab buttons
page.locator('button:has-text("Statistiky")')
page.locator('button:has-text("Seznam")')

// Table
page.locator('tbody tr')
page.locator('thead th')

// Filter inputs
page.locator('#invoiceId')
page.locator('#customerName')
page.locator('input[type="date"]')
page.locator('input[type="checkbox"]')

// Buttons
page.locator('button:has-text("Filtrovat")')
page.locator('button:has-text("Vymazat")')
page.locator('button:has-text("Import faktur")')

// Status badges
page.locator('span:has-text("Čeká")')
page.locator('span:has-text("Synchronizováno")')
page.locator('span:has-text("Chyba")')
```

## CI/CD Integration

These tests run:
- **Nightly:** Every night at 2:00 AM CET via GitHub Actions
- **Manual:** Via GitHub Actions workflow dispatch
- **Not in PR builds:** E2E tests don't block PR merges

**Workflow:** `.github/workflows/e2e-nightly-regression.yml`

## Debugging Tests

### Run in Headed Mode
```bash
npx playwright test test/e2e/customer/ --headed
```

### Debug Mode
```bash
npx playwright test test/e2e/customer/ --debug
```

### View Test Report
```bash
npx playwright show-report
```

### Screenshot on Failure
Tests automatically capture screenshots on failure (configured in `playwright.config.ts`)

## Troubleshooting

### Common Issues

**Issue: Microsoft login screen appears**
- **Fix:** Ensure using `navigateToIssuedInvoices()` helper, not manual navigation

**Issue: Tests timeout waiting for elements**
- **Fix:** Check if staging environment is healthy
- **Fix:** Increase timeout in test assertions

**Issue: Filter tests fail**
- **Fix:** Verify test data exists in staging environment
- **Fix:** Check if filter logic changed in implementation

**Issue: Import modal tests fail**
- **Fix:** Verify file upload endpoint is accessible
- **Fix:** Check network tab for upload errors

## Test Maintenance

### Adding New Tests

1. Create test in appropriate file (navigation, filters, sorting, etc.)
2. Use `navigateToIssuedInvoices()` helper for authentication
3. Use `waitForLoadingComplete()` for async operations
4. Follow existing naming conventions
5. Update this README with new test count

### Updating Tests

When implementation changes:
1. Update selectors if UI changes
2. Update expected text/values
3. Update wait conditions if loading behavior changes
4. Run full suite to verify no regressions

## Related Documentation

- **E2E Testing Guide:** `/docs/testing/playwright-e2e-testing.md`
- **Test Data Fixtures:** `/docs/testing/test-data-fixtures.md`
- **Architecture:** `/docs/📘 Architecture Documentation – MVP Work.md`
- **Setup Commands:** `/docs/development/setup.md`

## Test Coverage Summary

| Category | Tests | File |
|----------|-------|------|
| Navigation & Tabs | 2 | `issued-invoices-navigation.spec.ts` |
| Filter Functionality | 9 | `issued-invoices-filters.spec.ts` |
| Sorting | 7 | `issued-invoices-sorting.spec.ts` |
| Pagination | 7 | `issued-invoices-pagination.spec.ts` |
| Status Badges | 4 | `issued-invoices-status-badges.spec.ts` |
| Import Modal | 14 | `issued-invoices-import-modal.spec.ts` |
| **Total** | **43** | **6 files** |

---

**Last Updated:** 2026-01-28
**Test Suite Status:** ✅ Complete (43/43 tests implemented)
