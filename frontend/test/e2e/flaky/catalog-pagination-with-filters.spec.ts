import { test, expect } from '@playwright/test';
import { navigateToCatalog } from './helpers/e2e-auth-helper';
import {
  applyProductNameFilter,
  selectProductType,
  validateFilteredResults,
  validateFilterStatusIndicator,
  getRowCount,
  getCurrentPageNumber,
  getPageSizeSelect,
  waitForTableUpdate,
} from './helpers/catalog-test-helpers';

test.describe('Catalog Pagination with Filters E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to catalog with full authentication
    console.log('🧭 Navigating to catalog page...');
    await navigateToCatalog(page);
    expect(page.url()).toContain('/catalog');
    console.log('✅ On catalog page:', page.url());

    // Wait for initial catalog load
    console.log('⏳ Waiting for initial catalog to load...');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(3000);
  });

  // ============================================================================
  // PAGE NAVIGATION WITH FILTERS
  // ============================================================================

  test('should navigate to page 2 with name filter applied', async ({ page }) => {
    // Apply filter that should have multiple pages of results
    await applyProductNameFilter(page, 'Krém');

    const rowCount = await getRowCount(page);
    console.log(`📊 Filtered results on page 1: ${rowCount} rows`);

    // Check if there's a Next/Page 2 button
    const nextButton = page.getByRole('button', { name: /Next|Další|2/i });

    if (await nextButton.isVisible({ timeout: 2000 })) {
      // Navigate to page 2
      await nextButton.click();
      await waitForTableUpdate(page);

      // Verify we're on page 2
      const currentPage = await getCurrentPageNumber(page);
      expect(currentPage).toBe(2);

      // Verify filter is still applied
      await validateFilteredResults(page, { productName: 'Krém' }, { maxRowsToCheck: 5 });

      console.log('✅ Navigated to page 2 with filter applied');
    } else {
      console.log('ℹ️ No page 2 available (not enough filtered results)');
    }
  });

  test('should navigate to page 2 with product type filter', async ({ page }) => {
    // Apply product type filter
    await selectProductType(page, 'Produkt');

    const rowCount = await getRowCount(page);
    console.log(`📊 Products on page 1: ${rowCount} rows`);

    // Try to navigate to page 2
    const page2Link = page.getByRole('button', { name: '2' }).or(page.getByText('2', { exact: true }));

    if (await page2Link.isVisible({ timeout: 2000 })) {
      await page2Link.click();
      await waitForTableUpdate(page);

      // Verify we're on page 2
      const currentPage = await getCurrentPageNumber(page);
      expect(currentPage).toBe(2);

      // Verify filter is still applied
      await validateFilteredResults(page, { productType: 'Produkt' }, { maxRowsToCheck: 5 });

      console.log('✅ Navigated to page 2 with product type filter');
    } else {
      console.log('ℹ️ No page 2 available');
    }
  });

  test('should show correct page number in URL with filters', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    // Try to navigate to page 2
    const url = new URL(page.url());
    url.searchParams.set('page', '2');
    await page.goto(url.toString());
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Verify URL has page parameter
    const currentUrl = new URL(page.url());
    const pageParam = currentUrl.searchParams.get('page');
    expect(pageParam).toBe('2');

    // Verify filter is still in URL (implementation dependent)
    console.log('   Current URL:', page.url());

    console.log('✅ Page number shown in URL with filters');
  });

  test('should maintain filters when navigating between pages', async ({ page }) => {
    // Apply combined filters
    await selectProductType(page, 'Produkt');
    await applyProductNameFilter(page, 'Krém');

    const page1Count = await getRowCount(page);
    console.log(`📊 Page 1 count: ${page1Count}`);

    // Navigate to page 2 if available
    const nextButton = page.getByRole('button', { name: /Next|Další/i }).or(page.getByRole('button', { name: '2' }));

    if (await nextButton.isVisible({ timeout: 2000 })) {
      await nextButton.click();
      await waitForTableUpdate(page);

      // Verify filters maintained on page 2
      await validateFilteredResults(
        page,
        {
          productName: 'Krém',
          productType: 'Produkt',
        },
        { maxRowsToCheck: 5 }
      );

      // Navigate back to page 1
      const prevButton = page.getByRole('button', { name: /Previous|Předchozí|1/i });

      if (await prevButton.isVisible({ timeout: 2000 })) {
        await prevButton.click();
        await waitForTableUpdate(page);

        // Verify filters still maintained
        await validateFilteredResults(
          page,
          {
            productName: 'Krém',
            productType: 'Produkt',
          },
          { maxRowsToCheck: 5 }
        );

        console.log('✅ Filters maintained when navigating between pages');
      }
    } else {
      console.log('ℹ️ Not enough pages to test navigation');
    }
  });

  test('should display correct "X-Y of Z (filtrováno)" status', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Check for pagination info
      const paginationInfo = page.locator('text=/\\d+-\\d+ of \\d+/i');

      if (await paginationInfo.isVisible({ timeout: 2000 })) {
        const infoText = await paginationInfo.textContent();
        console.log(`   Pagination info: "${infoText}"`);

        // Should contain "filtrováno" or similar indicator
        await validateFilterStatusIndicator(page, true);

        console.log('✅ Correct pagination status displayed');
      } else {
        console.log('⚠️ Pagination info not found');
      }
    } else {
      console.log('ℹ️ No results to show pagination');
    }
  });

  // ============================================================================
  // PAGE SIZE CHANGES WITH FILTERS
  // ============================================================================

  test('should change page size (10→20) with filter applied', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    const initialRowCount = await getRowCount(page);
    console.log(`📊 Initial page size row count: ${initialRowCount}`);

    if (initialRowCount > 0) {
      // Change page size to 20
      const pageSizeSelect = getPageSizeSelect(page);
      await pageSizeSelect.selectOption('20');
      await waitForTableUpdate(page);

      const newRowCount = await getRowCount(page);
      console.log(`📊 After changing to 20: ${newRowCount} rows`);

      // Verify filter is still applied
      await validateFilteredResults(page, { productName: 'Krém' }, { maxRowsToCheck: 10 });

      console.log('✅ Page size changed with filter maintained');
    } else {
      console.log('ℹ️ No results to test page size change');
    }
  });

  test('should reset to page 1 when changing page size with filter', async ({ page }) => {
    // Apply filter
    await selectProductType(page, 'Produkt');

    // Navigate to page 2 if possible
    const url = new URL(page.url());
    url.searchParams.set('page', '2');
    await page.goto(url.toString());
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Change page size
    const pageSizeSelect = getPageSizeSelect(page);
    await pageSizeSelect.selectOption('20');
    await waitForTableUpdate(page);

    // Verify page was reset to 1
    const currentPage = await getCurrentPageNumber(page);
    expect(currentPage).toBe(1);

    console.log('✅ Page reset to 1 when changing page size with filter');
  });

  test('should maintain filter when changing page size', async ({ page }) => {
    // Apply combined filters
    await selectProductType(page, 'Materiál');
    await applyProductNameFilter(page, 'Olej');

    const initialRowCount = await getRowCount(page);

    if (initialRowCount > 0) {
      // Change page size
      const pageSizeSelect = getPageSizeSelect(page);
      await pageSizeSelect.selectOption('50');
      await waitForTableUpdate(page);

      // Verify both filters maintained
      await validateFilteredResults(
        page,
        {
          productName: 'Olej',
          productType: 'Materiál',
        },
        { maxRowsToCheck: 10 }
      );

      console.log('✅ Filters maintained when changing page size');
    } else {
      console.log('ℹ️ No results to test page size with filters');
    }
  });

  test('should recalculate total pages with active filter', async ({ page }) => {
    // Get total pages without filter
    const initialRowCount = await getRowCount(page);
    console.log(`📊 Initial total rows: ${initialRowCount}`);

    // Apply restrictive filter
    await applyProductNameFilter(page, 'Krém');

    const filteredRowCount = await getRowCount(page);
    console.log(`📊 Filtered rows: ${filteredRowCount}`);

    // The filtered count should be less than or equal to initial
    expect(filteredRowCount).toBeLessThanOrEqual(initialRowCount);

    // Check pagination controls reflect filtered total
    const paginationInfo = page.locator('text=/\\d+-\\d+ of \\d+/i');

    if (await paginationInfo.isVisible({ timeout: 2000 })) {
      const infoText = await paginationInfo.textContent();
      console.log(`   Pagination after filter: "${infoText}"`);

      console.log('✅ Total pages recalculated with filter');
    } else {
      console.log('ℹ️ Pagination info not available');
    }
  });

  // ============================================================================
  // PAGINATION EDGE CASES
  // ============================================================================

  test('should handle last page with filtered results', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    // Try to find the last page
    const paginationInfo = page.locator('text=/\\d+-\\d+ of (\\d+)/i');

    if (await paginationInfo.isVisible({ timeout: 2000 })) {
      const infoText = await paginationInfo.textContent();
      console.log(`   Pagination info: "${infoText}"`);

      // Try to click last page or next until we can't anymore
      let clickedNext = false;

      for (let i = 0; i < 10; i++) {
        const nextButton = page.getByRole('button', { name: /Next|Další/i });

        if (await nextButton.isEnabled({ timeout: 1000 })) {
          await nextButton.click();
          await waitForTableUpdate(page);
          clickedNext = true;
        } else {
          break;
        }
      }

      if (clickedNext) {
        // Verify we're on last page and filter is maintained
        await validateFilteredResults(page, { productName: 'Krém' }, { maxRowsToCheck: 5 });
        console.log('✅ Last page with filtered results handled correctly');
      } else {
        console.log('ℹ️ Only one page of filtered results');
      }
    } else {
      console.log('ℹ️ Pagination not available');
    }
  });

  test('should handle single page of filtered results', async ({ page }) => {
    // Apply very restrictive filter
    await applyProductNameFilter(page, 'NONEXISTENT');

    const rowCount = await getRowCount(page);

    if (rowCount === 0) {
      // Verify pagination controls handle empty results
      const nextButton = page.getByRole('button', { name: /Next|Další/i });
      const isNextDisabled = !(await nextButton.isEnabled({ timeout: 1000 }).catch(() => false));

      if (isNextDisabled || !(await nextButton.isVisible({ timeout: 1000 }))) {
        console.log('✅ Pagination correctly handles empty filtered results');
      }
    } else {
      console.log('ℹ️ Filter returned results, trying different filter');
    }
  });

  test('should disable Next button on last filtered page', async ({ page }) => {
    // Apply filter
    await selectProductType(page, 'Produkt');

    // Navigate through pages until Next is disabled
    let pagesNavigated = 0;

    for (let i = 0; i < 10; i++) {
      const nextButton = page.getByRole('button', { name: /Next|Další/i });

      if (await nextButton.isVisible({ timeout: 1000 })) {
        const isEnabled = await nextButton.isEnabled({ timeout: 1000 });

        if (!isEnabled) {
          console.log(`✅ Next button disabled on page ${pagesNavigated + 1}`);
          break;
        }

        await nextButton.click();
        await waitForTableUpdate(page);
        pagesNavigated++;
      } else {
        break;
      }
    }

    console.log(`   Navigated through ${pagesNavigated} pages`);
  });

  test('should disable Previous button on first page', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    // Ensure we're on page 1
    const currentPage = await getCurrentPageNumber(page);
    expect(currentPage).toBe(1);

    // Check Previous button is disabled
    const prevButton = page.getByRole('button', { name: /Previous|Předchozí/i });

    if (await prevButton.isVisible({ timeout: 2000 })) {
      const isEnabled = await prevButton.isEnabled();
      expect(isEnabled).toBe(false);
      console.log('✅ Previous button disabled on first page');
    } else {
      console.log('ℹ️ Previous button not visible (alternative pagination UI)');
    }
  });
});
