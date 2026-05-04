import { test, expect } from '@playwright/test';
import { navigateToInvoiceClassification } from '../helpers/e2e-auth-helper';

test.describe('Invoice Classification History', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to invoice classification page with full authentication
    console.log('🧭 Navigating to invoice classification page...');
    await navigateToInvoiceClassification(page);

    // Verify we're on the right page
    expect(page.url()).toContain('/purchase/invoice-classification');
    console.log('✅ On invoice classification page:', page.url());

    // Wait for page header to appear
    await page.waitForSelector('h1:has-text("Klasifikace faktur")', { timeout: 15000 });

    // Wait for content to load - give enough time for table to load or "no records" message to appear
    // The page shows "Loading..." initially, then renders table or empty state
    await page.waitForTimeout(2000);

    console.log('✅ Invoice classification page loaded');
  });

  test('pagination functionality', async ({ page }) => {
    // Arrange: page must show either a data table or an explicit empty-state message.
    const tableLocator = page.locator('table');
    const emptyStateLocator = page.locator(':text("Nebyly nalezeny žádné záznamy")');

    await expect(tableLocator.or(emptyStateLocator).first()).toBeVisible({ timeout: 15000 });

    // If the page has no data, there is nothing to paginate — pass early.
    const hasTable = await tableLocator.count() > 0;
    if (!hasTable) {
      return;
    }

    // The page-size select is always rendered when the table is present.
    const pageSizeSelect = page.locator('select').filter({ hasText: /10|20|50|100/ }).first();
    await expect(pageSizeSelect).toBeVisible();

    // Act: change page size and verify the select reflects the new value.
    await pageSizeSelect.selectOption('10');
    await expect(pageSizeSelect).toHaveValue('10');

    // Pagination nav only renders when totalPages > 1.
    const paginationNav = page.locator('nav[aria-label="Pagination"]');
    const hasPagination = await paginationNav.count() > 0;
    if (!hasPagination) {
      // Single-page result set — no navigation controls expected.
      return;
    }

    // Expand to 50 per page to maximise chance of multiple pages being present.
    await pageSizeSelect.selectOption('50');
    await page.waitForTimeout(1000);

    const nextButton = paginationNav.locator('button').last();
    const prevButton = paginationNav.locator('button').first();
    const activePageButton = paginationNav.locator('button.z-10');

    if (await nextButton.isDisabled()) {
      // Still only one page — nothing more to assert.
      return;
    }

    // Act: navigate to page 2.
    await nextButton.click();
    await expect(activePageButton).toHaveText('2');

    // Act: navigate back to page 1.
    await prevButton.click();
    await expect(activePageButton).toHaveText('1');
  });

  test('filters functionality', async ({ page }) => {
    // Verify filter section exists
    const filterSection = page.locator('div:has-text("Filtry:")').first();
    expect(await filterSection.count()).toBeGreaterThan(0);

    // Test company name filter
    const companyInput = page.locator('input[placeholder*="Název firmy"]');
    expect(await companyInput.count()).toBeGreaterThan(0);

    await companyInput.fill('test');
    await page.waitForTimeout(500);

    // Click filter button
    const filterButton = page.locator('button:has-text("Filtrovat")');
    expect(await filterButton.count()).toBeGreaterThan(0);
    await filterButton.click();
    await page.waitForTimeout(1000);

    // Verify the filter was applied (input should still contain the value)
    expect(await companyInput.inputValue()).toBe('test');

    // Test invoice number filter
    const invoiceInput = page.locator('input[placeholder*="Číslo faktury"]');
    expect(await invoiceInput.count()).toBeGreaterThan(0);

    await invoiceInput.fill('INV-123');
    await page.waitForTimeout(500);
    await filterButton.click();
    await page.waitForTimeout(1000);

    // Verify both filters are still set
    expect(await companyInput.inputValue()).toBe('test');
    expect(await invoiceInput.inputValue()).toBe('INV-123');

    // Test clear filters button
    const clearButton = page.locator('button:has-text("Vymazat")');
    expect(await clearButton.count()).toBeGreaterThan(0);
    await clearButton.click();
    await page.waitForTimeout(1000);

    // Verify filters were cleared
    expect(await companyInput.inputValue()).toBe('');
    expect(await invoiceInput.inputValue()).toBe('');

    // Test date filters
    const fromDateInput = page.locator('input#fromDate');
    const toDateInput = page.locator('input#toDate');

    expect(await fromDateInput.count()).toBeGreaterThan(0);
    expect(await toDateInput.count()).toBeGreaterThan(0);

    // Set date range
    await fromDateInput.fill('2026-01-01');
    await toDateInput.fill('2026-01-31');
    await page.waitForTimeout(500);
    await filterButton.click();
    await page.waitForTimeout(1000);

    // Verify dates are set
    expect(await fromDateInput.inputValue()).toBe('2026-01-01');
    expect(await toDateInput.inputValue()).toBe('2026-01-31');

    // Clear all filters again
    await clearButton.click();
    await page.waitForTimeout(1000);

    // Verify all filters cleared including dates
    expect(await fromDateInput.inputValue()).toBe('');
    expect(await toDateInput.inputValue()).toBe('');

    console.log('Filters functionality test completed successfully');
  });
});