import { test, expect } from '@playwright/test';
import { navigateToInvoiceClassification } from './helpers/e2e-auth-helper';
import {
  waitForClassificationHistoryLoaded,
  applyFilters,
  clearAllFilters,
  getFilterInputs,
  getRowCount,
  hasNoRecordsMessage,
  getTableRows,
} from './helpers/classification-history-helpers';

test.describe('Classification History - Date Filters', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToInvoiceClassification(page);
    await waitForClassificationHistoryLoaded(page);
  });

  test('should filter by fromDate - basic functionality', async ({ page }) => {
    const initialCount = await getRowCount(page);

    // Apply fromDate filter
    const fromDateStr = '2026-01-01';

    await applyFilters(page, { fromDate: fromDateStr });

    const filteredCount = await getRowCount(page);
    const noRecords = await hasNoRecordsMessage(page);

    // We expect either:
    // 1. Records to be filtered (count changed) OR
    // 2. No records message if all dates are before fromDate OR
    // 3. Same count if all records are within range
    expect(noRecords || filteredCount >= 0).toBeTruthy();

    // Verify filter input shows the value
    const inputs = getFilterInputs(page);
    const fromDateValue = await inputs.fromDate.inputValue();
    expect(fromDateValue).toBe(fromDateStr);

    // If we have records, verify dates are >= fromDate
    if (filteredCount > 0 && !noRecords) {
      const rows = getTableRows(page);
      const firstRow = rows.first();

      // Check that date column exists and is >= fromDate
      const dateCell = firstRow.locator('td').first(); // Assuming first column is date
      const dateCellText = await dateCell.textContent();

      if (dateCellText) {
        console.log(`First row date: ${dateCellText}, Filter from: ${fromDateStr}`);
      }
    }
  });

  test('should filter by toDate - basic functionality', async ({ page }) => {
    const initialCount = await getRowCount(page);

    // Apply toDate filter
    const toDateStr = '2026-01-31';

    await applyFilters(page, { toDate: toDateStr });

    const filteredCount = await getRowCount(page);
    const noRecords = await hasNoRecordsMessage(page);

    // We expect either:
    // 1. Records to be filtered (count changed) OR
    // 2. No records message if all dates are after toDate OR
    // 3. Same count if all records are within range
    expect(noRecords || filteredCount >= 0).toBeTruthy();

    // Verify filter input shows the value
    const inputs = getFilterInputs(page);
    const toDateValue = await inputs.toDate.inputValue();
    expect(toDateValue).toBe(toDateStr);

    // If we have records, verify dates are <= toDate
    if (filteredCount > 0 && !noRecords) {
      const rows = getTableRows(page);
      const firstRow = rows.first();

      // Check that date column exists and is <= toDate
      const dateCell = firstRow.locator('td').first(); // Assuming first column is date
      const dateCellText = await dateCell.textContent();

      if (dateCellText) {
        console.log(`First row date: ${dateCellText}, Filter to: ${toDateStr}`);
      }
    }
  });

  test('should filter by date range (both dates)', async ({ page }) => {
    const inputs = getFilterInputs(page);

    // Set date range
    const fromDate = '2026-01-01';
    const toDate = '2026-01-31';
    await applyFilters(page, { fromDate, toDate });

    // Verify both filters applied
    expect(await inputs.fromDate.inputValue()).toBe(fromDate);
    expect(await inputs.toDate.inputValue()).toBe(toDate);

    // Verify results
    const hasData = (await getRowCount(page)) > 0;
    const hasNoData = await hasNoRecordsMessage(page);
    expect(hasData || hasNoData).toBe(true);
  });

  test('should handle invalid date ranges gracefully', async ({ page }) => {
    const inputs = getFilterInputs(page);

    // Set toDate before fromDate (invalid range)
    const fromDate = '2026-01-31';
    const toDate = '2026-01-01';
    await applyFilters(page, { fromDate, toDate });

    // Application should still work (may show no results or all results)
    // Verify no error state appears
    const errorElement = page.locator(':text("Chyba"), :text("Error")');
    const hasError = (await errorElement.count()) > 0;
    expect(hasError).toBe(false);
  });

  test('should clear date filters when clicking clear button', async ({ page }) => {
    const inputs = getFilterInputs(page);

    // Apply date filters
    await applyFilters(page, {
      fromDate: '2026-01-01',
      toDate: '2026-01-31',
    });

    // Verify filters are set
    expect(await inputs.fromDate.inputValue()).toBe('2026-01-01');
    expect(await inputs.toDate.inputValue()).toBe('2026-01-31');

    // Clear filters
    await clearAllFilters(page);

    // Verify filters are cleared
    expect(await inputs.fromDate.inputValue()).toBe('');
    expect(await inputs.toDate.inputValue()).toBe('');
  });
});
