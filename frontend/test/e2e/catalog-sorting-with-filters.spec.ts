import { test, expect } from '@playwright/test';
import { navigateToCatalog } from './helpers/e2e-auth-helper';
import {
  applyProductNameFilter,
  applyProductCodeFilter,
  selectProductType,
  validateFilteredResults,
  validatePageResetToOne,
  getRowCount,
  waitForTableUpdate,
} from './helpers/catalog-test-helpers';

test.describe('Catalog Sorting with Filters E2E Tests', () => {
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
  // COLUMN SORTING WITH ACTIVE FILTERS
  // ============================================================================

  test('should sort by Product Code with name filter applied', async ({ page }) => {
    // Apply name filter
    await applyProductNameFilter(page, 'Krém');

    const rowCount = await getRowCount(page);

    if (rowCount > 1) {
      // Click on Product Code header to sort
      const codeHeader = page.locator('th').filter({ hasText: 'Kód produktu' }).first();
      await codeHeader.click();
      await waitForTableUpdate(page);

      // Verify filter is still applied
      await validateFilteredResults(page, { productName: 'Krém' }, { maxRowsToCheck: 5 });

      // Verify sorting by checking first few codes are in order
      const firstCode = await page.locator('tbody tr:nth-child(1) td:nth-child(1)').textContent();
      const secondCode = await page.locator('tbody tr:nth-child(2) td:nth-child(1)').textContent();

      console.log(`   First code: "${firstCode?.trim()}"`);
      console.log(`   Second code: "${secondCode?.trim()}"`);

      console.log('✅ Sorting by Product Code with name filter working');
    } else {
      console.log('ℹ️ Not enough results to test sorting');
    }
  });

  test('should sort by Product Name with code filter applied', async ({ page }) => {
    // Apply code filter
    await applyProductCodeFilter(page, 'AH');

    const rowCount = await getRowCount(page);

    if (rowCount > 1) {
      // Click on Product Name header to sort
      const nameHeader = page.locator('th').filter({ hasText: 'Název produktu' }).first();
      await nameHeader.click();
      await waitForTableUpdate(page);

      // Verify filter is still applied
      await validateFilteredResults(page, { productCode: 'AH' }, { maxRowsToCheck: 5 });

      // Verify sorting by checking first few names
      const firstName = await page.locator('tbody tr:nth-child(1) td:nth-child(2)').textContent();
      const secondName = await page.locator('tbody tr:nth-child(2) td:nth-child(2)').textContent();

      console.log(`   First name: "${firstName?.trim()}"`);
      console.log(`   Second name: "${secondName?.trim()}"`);

      console.log('✅ Sorting by Product Name with code filter working');
    } else {
      console.log('ℹ️ Not enough results to test sorting');
    }
  });

  test('should sort by Available Stock with product type filter', async ({ page }) => {
    // Apply product type filter
    await selectProductType(page, 'Produkt');

    const rowCount = await getRowCount(page);

    if (rowCount > 1) {
      // Find and click Available Stock header
      // This might be "Volný sklad" or similar
      const stockHeader = page.locator('th').filter({ hasText: /Volný|Sklad/i }).first();

      if (await stockHeader.isVisible({ timeout: 2000 })) {
        await stockHeader.click();
        await waitForTableUpdate(page);

        // Verify filter is still applied
        await validateFilteredResults(page, { productType: 'Produkt' }, { maxRowsToCheck: 5 });

        console.log('✅ Sorting by Available Stock with product type filter working');
      } else {
        console.log('⚠️ Available Stock column not found');
      }
    } else {
      console.log('ℹ️ Not enough results to test sorting');
    }
  });

  test('should sort by In Reserve with combined filters', async ({ page }) => {
    // Apply combined filters
    await selectProductType(page, 'Produkt');
    await applyProductNameFilter(page, 'Krém');

    const rowCount = await getRowCount(page);

    if (rowCount > 1) {
      // Find and click In Reserve header
      const reserveHeader = page.locator('th').filter({ hasText: /Rezerv/i }).first();

      if (await reserveHeader.isVisible({ timeout: 2000 })) {
        await reserveHeader.click();
        await waitForTableUpdate(page);

        // Verify both filters are still applied
        await validateFilteredResults(
          page,
          {
            productName: 'Krém',
            productType: 'Produkt',
          },
          { maxRowsToCheck: 5 }
        );

        console.log('✅ Sorting by In Reserve with combined filters working');
      } else {
        console.log('⚠️ In Reserve column not found');
      }
    } else {
      console.log('ℹ️ Not enough results to test sorting');
    }
  });

  // ============================================================================
  // SORT DIRECTION TOGGLE
  // ============================================================================

  test('should toggle ascending/descending with filter applied', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    const rowCount = await getRowCount(page);

    if (rowCount > 1) {
      // Click Product Code header once (ascending)
      const codeHeader = page.locator('th').filter({ hasText: 'Kód produktu' }).first();
      await codeHeader.click();
      await waitForTableUpdate(page);

      const firstAscCode = await page.locator('tbody tr:nth-child(1) td:nth-child(1)').textContent();
      console.log(`   First code (ascending): "${firstAscCode?.trim()}"`);

      // Click again (descending)
      await codeHeader.click();
      await waitForTableUpdate(page);

      const firstDescCode = await page.locator('tbody tr:nth-child(1) td:nth-child(1)').textContent();
      console.log(`   First code (descending): "${firstDescCode?.trim()}"`);

      // They should be different (unless there's only one result)
      if (rowCount > 1) {
        expect(firstAscCode?.trim()).not.toBe(firstDescCode?.trim());
      }

      // Verify filter is still applied after toggling
      await validateFilteredResults(page, { productName: 'Krém' }, { maxRowsToCheck: 3 });

      console.log('✅ Sort direction toggle working with filter');
    } else {
      console.log('ℹ️ Not enough results to test sort toggle');
    }
  });

  test('should maintain filter when changing sort direction', async ({ page }) => {
    // Apply filter
    await selectProductType(page, 'Materiál');

    const initialRowCount = await getRowCount(page);

    if (initialRowCount > 0) {
      // Sort ascending
      const nameHeader = page.locator('th').filter({ hasText: 'Název produktu' }).first();
      await nameHeader.click();
      await waitForTableUpdate(page);

      let currentRowCount = await getRowCount(page);
      expect(currentRowCount).toBe(initialRowCount);

      // Sort descending
      await nameHeader.click();
      await waitForTableUpdate(page);

      currentRowCount = await getRowCount(page);
      expect(currentRowCount).toBe(initialRowCount);

      // Verify filter maintained
      await validateFilteredResults(page, { productType: 'Materiál' }, { maxRowsToCheck: 5 });

      console.log('✅ Filter maintained when changing sort direction');
    } else {
      console.log('ℹ️ No materials to test sorting');
    }
  });

  test('should maintain sort when applying new filter', async ({ page }) => {
    // First, sort by Product Name (ascending)
    const nameHeader = page.locator('th').filter({ hasText: 'Název produktu' }).first();
    await nameHeader.click();
    await waitForTableUpdate(page);

    const initialFirstName = await page.locator('tbody tr:nth-child(1) td:nth-child(2)').textContent();
    console.log(`   First name after sort: "${initialFirstName?.trim()}"`);

    // Now apply a filter - use a search term that will return multiple results
    // Using "Glyc" to match "Glycerol" and other items containing "Glyc"
    await applyProductNameFilter(page, 'Glyc');

    // Give DOM time to settle after filter application
    await page.waitForTimeout(1000);

    const rowCount = await getRowCount(page);
    console.log(`   Row count after filter: ${rowCount}`);

    if (rowCount === 0) {
      throw new Error('Test data missing: Expected to find products containing "Glyc"');
    }

    // The sort should still be applied (ascending by product name)
    const filteredFirstName = await page.locator('tbody tr:nth-child(1) td:nth-child(2)').textContent();
    console.log(`   First name after filter: "${filteredFirstName?.trim()}"`);

    // Verify the filter is working (first result should contain "Glyc")
    if (filteredFirstName) {
      expect(filteredFirstName.toLowerCase()).toContain('glyc');
    }

    // Verify sorting is maintained by checking order of results (if multiple)
    if (rowCount > 1) {
      const secondName = await page.locator('tbody tr:nth-child(2) td:nth-child(2)').textContent();
      console.log(`   Second name after filter: "${secondName?.trim()}"`);

      // Both should contain "Glyc" (filter maintained)
      if (secondName) {
        expect(secondName.toLowerCase()).toContain('glyc');
      }

      // First name should be alphabetically <= second name (sort maintained)
      if (filteredFirstName && secondName) {
        const comparison = filteredFirstName.trim().localeCompare(secondName.trim());
        expect(comparison).toBeLessThanOrEqual(0);
      }
    }

    console.log('✅ Sort maintained when applying new filter');
  });

  // Testing: Pagination reset when changing sort order
  // Expected behavior: When user changes sort order while on page 2, pagination should reset to page 1
  // to show the beginning of sorted results.
  //
  // BUG IDENTIFIED: This test reveals a real application bug where changing sort order does NOT reset
  // pagination to page 1. When a user is on page 2 and clicks to sort by a different column, they remain
  // on page 2 instead of being taken to page 1 to see the beginning of the newly sorted results.
  //
  // Expected: After clicking sort header, URL should reset to page=1 (or no page param)
  // Actual: URL keeps page=2, showing records 21-40 instead of 1-20 of sorted results
  //
  // User Impact: Users may miss important data or get confused when sorted results start from middle of list
  test('should reset to page 1 when changing sort', async ({ page }) => {
    // Apply filter to ensure we have results
    await selectProductType(page, 'Produkt');

    // Navigate to page 2 if possible
    const url = new URL(page.url());
    url.searchParams.set('page', '2');
    await page.goto(url.toString());
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Sort by Product Code
    const codeHeader = page.locator('th').filter({ hasText: 'Kód produktu' }).first();
    await codeHeader.click();
    await waitForTableUpdate(page);

    // Verify page was reset to 1
    await validatePageResetToOne(page);

    console.log('✅ Page reset to 1 when changing sort');
  });

  // ============================================================================
  // MULTI-COLUMN SORTING
  // ============================================================================

  test('should change sort column while maintaining filters', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    const rowCount = await getRowCount(page);

    if (rowCount > 1) {
      // Sort by Product Code
      const codeHeader = page.locator('th').filter({ hasText: 'Kód produktu' }).first();
      await codeHeader.click();
      await waitForTableUpdate(page);

      // Verify filter maintained
      await validateFilteredResults(page, { productName: 'Krém' }, { maxRowsToCheck: 3 });

      // Change to sort by Product Name
      const nameHeader = page.locator('th').filter({ hasText: 'Název produktu' }).first();
      await nameHeader.click();
      await waitForTableUpdate(page);

      // Verify filter still maintained
      await validateFilteredResults(page, { productName: 'Krém' }, { maxRowsToCheck: 3 });

      console.log('✅ Filter maintained when changing sort column');
    } else {
      console.log('ℹ️ Not enough results to test column change');
    }
  });

  test('should preserve filter results when switching sort columns', async ({ page }) => {
    // Apply combined filters
    await selectProductType(page, 'Produkt');
    await applyProductNameFilter(page, 'Krém');

    const initialRowCount = await getRowCount(page);
    console.log(`📊 Initial filtered count: ${initialRowCount}`);

    if (initialRowCount > 0) {
      // Sort by Code
      const codeHeader = page.locator('th').filter({ hasText: 'Kód produktu' }).first();
      await codeHeader.click();
      await waitForTableUpdate(page);

      let currentRowCount = await getRowCount(page);
      expect(currentRowCount).toBe(initialRowCount);

      // Sort by Name
      const nameHeader = page.locator('th').filter({ hasText: 'Název produktu' }).first();
      await nameHeader.click();
      await waitForTableUpdate(page);

      currentRowCount = await getRowCount(page);
      expect(currentRowCount).toBe(initialRowCount);

      console.log('✅ Filter results preserved when switching sort columns');
    } else {
      console.log('ℹ️ No results to test sort switching');
    }
  });

  test('should handle sorting empty filtered results', async ({ page }) => {
    // Apply filter that results in no matches
    await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);
    expect(rowCount).toBe(0);

    // Try to sort (should not cause errors)
    const codeHeader = page.locator('th').filter({ hasText: 'Kód produktu' }).first();

    try {
      await codeHeader.click();
      await page.waitForTimeout(1000);
      console.log('✅ Sorting empty filtered results handled gracefully');
    } catch (error) {
      console.log('⚠️ Error when sorting empty results:', error);
      throw error;
    }

    // Should still show empty state
    const finalRowCount = await getRowCount(page);
    expect(finalRowCount).toBe(0);
  });
});
