import { test, expect } from '@playwright/test';
import { navigateToApp } from './helpers/e2e-auth-helper';

test.describe('Invoice Classification History', () => {
  test.beforeEach(async ({ page }) => {
    // Establish E2E authentication session with full frontend setup
    await navigateToApp(page);

    // Navigate to invoice classification page
    const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/purchase/invoice-classification`);
    await page.waitForLoadState('domcontentloaded');

    // Wait for React app to mount - check for the App container first
    await page.waitForSelector('[data-testid="app"], .App, #root > div', { timeout: 15000 });

    // Wait for either the page content OR an error message to appear
    try {
      // Try waiting for the page header
      await page.waitForSelector('h1:has-text("Klasifikace faktur")', { timeout: 10000 });

      // Wait for content to load - table or "no records" message or loading state
      await page.waitForSelector('table, :text("Nebyly nalezeny žádné záznamy"), :text("Načítání"), :text("Loading")', { timeout: 10000 });
    } catch (error) {
      // If page doesn't load, capture what's actually on screen
      const pageContent = await page.content();
      console.log('Page failed to load. Current URL:', page.url());
      console.log('Page content length:', pageContent.length);

      // Check if there's an error message visible
      const errorMessages = await page.locator(':text("Error"), :text("Chyba"), [role="alert"]').count();
      if (errorMessages > 0) {
        const errorText = await page.locator(':text("Error"), :text("Chyba"), [role="alert"]').first().textContent();
        console.log('Error message found:', errorText);
      }

      throw new Error(`Invoice classification page failed to load. URL: ${page.url()}`);
    }
  });

  test('pagination functionality', async ({ page }) => {
    // Check if there's a table with data
    const tableExists = await page.locator('table').count() > 0;

    if (!tableExists) {
      // If no table, check if there's a "no records" message
      const noRecordsMsg = await page.locator(':text("Nebyly nalezeny žádné záznamy")').count();
      if (noRecordsMsg > 0) {
        console.log('No classification history data available - test passed (no data to paginate)');
        return;
      }
      throw new Error('Expected either a table with data or a "no records" message');
    }

    // Table exists, now check for pagination controls
    // Pagination only shows when totalPages > 1 (line 359 in ClassificationHistoryPage.tsx)
    const paginationNav = page.locator('nav[aria-label="Pagination"]');
    const hasPagination = await paginationNav.count() > 0;

    if (!hasPagination) {
      // Single page of results - pagination not expected
      console.log('Single page of results - pagination controls not visible (expected behavior)');

      // Verify we can still see the page size selector
      const pageSizeSelector = page.locator('select').filter({ hasText: /10|20|50|100/ });
      const hasPageSizeSelector = await pageSizeSelector.count() > 0;

      if (hasPageSizeSelector) {
        // Test page size selector works
        await pageSizeSelector.selectOption('10');
        await page.waitForTimeout(500);

        const selectedValue = await pageSizeSelector.inputValue();
        expect(selectedValue).toBe('10');
        console.log('Page size selector works correctly');
      }

      return;
    }

    // Multiple pages - test full pagination
    console.log('Multiple pages detected - testing pagination controls');

    // Test page size selector
    const pageSizeSelector = page.locator('select').filter({ hasText: /10|20|50|100/ });
    expect(await pageSizeSelector.count()).toBeGreaterThan(0);

    // Change page size to 10
    await pageSizeSelector.selectOption('10');
    await page.waitForTimeout(1000);
    expect(await pageSizeSelector.inputValue()).toBe('10');

    // Change page size to 50
    await pageSizeSelector.selectOption('50');
    await page.waitForTimeout(1000);
    expect(await pageSizeSelector.inputValue()).toBe('50');

    // Test navigation buttons
    const nextButton = page.locator('nav[aria-label="Pagination"] button').last();
    const prevButton = page.locator('nav[aria-label="Pagination"] button').first();

    // Check if next button is enabled (meaning there's a next page)
    const nextButtonEnabled = !(await nextButton.isDisabled());

    if (nextButtonEnabled) {
      // Click next page
      await nextButton.click();
      await page.waitForTimeout(1000);

      // Verify we moved to page 2
      const currentPage = page.locator('nav[aria-label="Pagination"] button.z-10');
      const pageText = await currentPage.textContent();
      expect(pageText).toBe('2');

      // Go back to page 1
      await prevButton.click();
      await page.waitForTimeout(1000);

      // Verify we're back on page 1
      const firstPageText = await currentPage.textContent();
      expect(firstPageText).toBe('1');
    }

    console.log('Pagination functionality test completed successfully');
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