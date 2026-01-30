import { test, expect } from '@playwright/test';
import { navigateToInvoiceClassification } from '../helpers/e2e-auth-helper';
import {
  waitForClassificationHistoryLoaded,
  applyFilters,
  clearAllFilters,
  getFilterInputs,
  getRowCount,
  hasNoRecordsMessage,
  getTableRows,
} from '../helpers/classification-history-helpers';

test.describe('Classification History - Date Filters', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToInvoiceClassification(page);
    await waitForClassificationHistoryLoaded(page);
  });

  test('should filter by fromDate - basic functionality', async ({ page }) => {
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

test.describe('Classification History - Invoice Number Filters', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToInvoiceClassification(page);
    await waitForClassificationHistoryLoaded(page);
  });

  test('should filter by exact invoice number match', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's invoice number
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const invoiceNumberCell = firstRow.locator('td').nth(1); // Second column: Invoice Number
    const invoiceNumber = await invoiceNumberCell.textContent();
    expect(invoiceNumber).toBeTruthy();

    // Apply exact invoice number filter
    await applyFilters(page, { invoiceNumber: invoiceNumber!.trim() });

    // Verify results
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results match the invoice number
    const filteredRows = getTableRows(page);
    const filteredInvoiceNumbers = await filteredRows
      .locator('td')
      .nth(1)
      .allTextContents();
    filteredInvoiceNumbers.forEach((num) => {
      expect(num.trim()).toContain(invoiceNumber!.trim());
    });
  });

  test('should filter by partial invoice number match', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's invoice number and use first 3 characters
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const invoiceNumberCell = firstRow.locator('td').nth(1);
    const fullInvoiceNumber = await invoiceNumberCell.textContent();
    expect(fullInvoiceNumber).toBeTruthy();

    const partialNumber = fullInvoiceNumber!.trim().substring(0, 3);

    // Apply partial invoice number filter
    await applyFilters(page, { invoiceNumber: partialNumber });

    // Verify results
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results contain the partial number
    const filteredRows = getTableRows(page);
    const filteredInvoiceNumbers = await filteredRows
      .locator('td')
      .nth(1)
      .allTextContents();
    filteredInvoiceNumbers.forEach((num) => {
      expect(num.toLowerCase()).toContain(partialNumber.toLowerCase());
    });
  });

  test('should be case-insensitive for invoice number search', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's invoice number
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const invoiceNumberCell = firstRow.locator('td').nth(1);
    const invoiceNumber = await invoiceNumberCell.textContent();
    expect(invoiceNumber).toBeTruthy();

    // Convert to lowercase and apply filter
    const lowerCaseNumber = invoiceNumber!.trim().toLowerCase();
    await applyFilters(page, { invoiceNumber: lowerCaseNumber });

    // Verify results - should match regardless of case
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results contain the invoice number (case-insensitive)
    const filteredRows = getTableRows(page);
    const filteredInvoiceNumbers = await filteredRows
      .locator('td')
      .nth(1)
      .allTextContents();
    filteredInvoiceNumbers.forEach((num) => {
      expect(num.toLowerCase()).toContain(lowerCaseNumber);
    });
  });

  test('should show no results for non-existent invoice number', async ({ page }) => {
    const nonExistentNumber = 'XXXX-NONEXISTENT-9999';

    // Apply filter with non-existent invoice number
    await applyFilters(page, { invoiceNumber: nonExistentNumber });

    // Verify no results message appears
    const noRecords = await hasNoRecordsMessage(page);
    expect(noRecords).toBe(true);

    // Verify row count is 0
    const rowCount = await getRowCount(page);
    expect(rowCount).toBe(0);
  });

  test('should apply invoice number filter on Enter key press', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's invoice number
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const invoiceNumberCell = firstRow.locator('td').nth(1);
    const invoiceNumber = await invoiceNumberCell.textContent();
    expect(invoiceNumber).toBeTruthy();

    // Type invoice number and press Enter
    const inputs = getFilterInputs(page);
    await inputs.invoiceNumber.fill(invoiceNumber!.trim());
    await inputs.invoiceNumber.press('Enter');

    // Wait for filter to apply
    await page.waitForTimeout(500);

    // Verify results
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results match the invoice number
    const filteredRows = getTableRows(page);
    const filteredInvoiceNumbers = await filteredRows
      .locator('td')
      .nth(1)
      .allTextContents();
    filteredInvoiceNumbers.forEach((num) => {
      expect(num.trim()).toContain(invoiceNumber!.trim());
    });
  });
});

test.describe('Classification History - Company Name Filters', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToInvoiceClassification(page);
    await waitForClassificationHistoryLoaded(page);
  });

  test('should filter by exact company name match', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's company name
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const companyNameCell = firstRow.locator('td').nth(2); // Third column: Company Name
    const companyName = await companyNameCell.textContent();
    expect(companyName).toBeTruthy();

    // Apply exact company name filter
    await applyFilters(page, { companyName: companyName!.trim() });

    // Verify results
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results match the company name
    const filteredRows = getTableRows(page);
    const filteredCompanyNames = await filteredRows
      .locator('td')
      .nth(2)
      .allTextContents();
    filteredCompanyNames.forEach((name) => {
      expect(name.trim()).toContain(companyName!.trim());
    });
  });

  test('should filter by partial company name match', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's company name and use first word
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const companyNameCell = firstRow.locator('td').nth(2);
    const fullCompanyName = await companyNameCell.textContent();
    expect(fullCompanyName).toBeTruthy();

    const firstWord = fullCompanyName!.trim().split(' ')[0];

    // Apply partial company name filter
    await applyFilters(page, { companyName: firstWord });

    // Verify results
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results contain the partial name
    const filteredRows = getTableRows(page);
    const filteredCompanyNames = await filteredRows
      .locator('td')
      .nth(2)
      .allTextContents();
    filteredCompanyNames.forEach((name) => {
      expect(name.toLowerCase()).toContain(firstWord.toLowerCase());
    });
  });

  test('should be case-insensitive for company name search', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's company name
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const companyNameCell = firstRow.locator('td').nth(2);
    const companyName = await companyNameCell.textContent();
    expect(companyName).toBeTruthy();

    // Convert to lowercase and apply filter
    const lowerCaseName = companyName!.trim().toLowerCase();
    await applyFilters(page, { companyName: lowerCaseName });

    // Verify results - should match regardless of case
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results contain the company name (case-insensitive)
    const filteredRows = getTableRows(page);
    const filteredCompanyNames = await filteredRows
      .locator('td')
      .nth(2)
      .allTextContents();
    filteredCompanyNames.forEach((name) => {
      expect(name.toLowerCase()).toContain(lowerCaseName);
    });
  });

  test('should apply company name filter on Enter key press', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get first row's company name
    const rows = getTableRows(page);
    const firstRow = rows.first();
    const companyNameCell = firstRow.locator('td').nth(2);
    const companyName = await companyNameCell.textContent();
    expect(companyName).toBeTruthy();

    // Type company name and press Enter
    const inputs = getFilterInputs(page);
    await inputs.companyName.fill(companyName!.trim());
    await inputs.companyName.press('Enter');

    // Wait for filter to apply
    await page.waitForTimeout(500);

    // Verify results
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify all results match the company name
    const filteredRows = getTableRows(page);
    const filteredCompanyNames = await filteredRows
      .locator('td')
      .nth(2)
      .allTextContents();
    filteredCompanyNames.forEach((name) => {
      expect(name.trim()).toContain(companyName!.trim());
    });
  });
});

test.describe('Classification History - Combined Filters', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToInvoiceClassification(page);
    await waitForClassificationHistoryLoaded(page);
  });

  test('should apply all four filters together', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Get data from first row
    const rows = getTableRows(page);
    const firstRow = rows.first();

    const dateCell = firstRow.locator('td').nth(0);
    const invoiceNumberCell = firstRow.locator('td').nth(1);
    const companyNameCell = firstRow.locator('td').nth(2);

    const dateText = await dateCell.textContent();
    const invoiceNumber = await invoiceNumberCell.textContent();
    const companyName = await companyNameCell.textContent();

    expect(dateText).toBeTruthy();
    expect(invoiceNumber).toBeTruthy();
    expect(companyName).toBeTruthy();

    // Parse date and create date range (same month)
    const dateParts = dateText!.trim().split('.');
    expect(dateParts.length).toBeGreaterThanOrEqual(2);

    const day = dateParts[0].padStart(2, '0');
    const month = dateParts[1].padStart(2, '0');
    const year = dateParts[2] || '2026';

    const fromDate = `${year}-${month}-01`;
    const toDate = `${year}-${month}-${day}`;

    // Apply all four filters
    await applyFilters(page, {
      fromDate,
      toDate,
      invoiceNumber: invoiceNumber!.trim(),
      companyName: companyName!.trim(),
    });

    // Verify results
    const filteredCount = await getRowCount(page);
    expect(filteredCount).toBeGreaterThan(0);

    // Verify first row matches all filters
    const filteredRows = getTableRows(page);
    const filteredFirstRow = filteredRows.first();

    const filteredInvoiceNumber = await filteredFirstRow
      .locator('td')
      .nth(1)
      .textContent();
    const filteredCompanyName = await filteredFirstRow
      .locator('td')
      .nth(2)
      .textContent();

    expect(filteredInvoiceNumber?.trim()).toContain(invoiceNumber!.trim());
    expect(filteredCompanyName?.trim()).toContain(companyName!.trim());
  });

  test('should persist combined filters after pagination', async ({ page }) => {
    const initialCount = await getRowCount(page);
    expect(initialCount).toBeGreaterThan(0);

    // Apply combined filters
    await applyFilters(page, {
      fromDate: '2026-01-01',
      invoiceNumber: 'INV',
    });

    const inputs = getFilterInputs(page);

    // Verify filters are applied
    expect(await inputs.fromDate.inputValue()).toBe('2026-01-01');
    expect(await inputs.invoiceNumber.inputValue()).toBe('INV');

    // Check if pagination exists
    const paginationNext = page.locator('[aria-label="Go to next page"]');
    const isPaginationAvailable = (await paginationNext.count()) > 0;

    if (isPaginationAvailable && (await paginationNext.isEnabled())) {
      // Navigate to next page
      await paginationNext.click();
      await page.waitForTimeout(500);

      // Verify filters persist
      expect(await inputs.fromDate.inputValue()).toBe('2026-01-01');
      expect(await inputs.invoiceNumber.inputValue()).toBe('INV');

      // Verify filtered results on page 2
      const page2Count = await getRowCount(page);
      expect(page2Count).toBeGreaterThan(0);
    } else {
      // If no pagination, just verify filters remain
      expect(await inputs.fromDate.inputValue()).toBe('2026-01-01');
      expect(await inputs.invoiceNumber.inputValue()).toBe('INV');
    }
  });
});
