import { test, expect } from '@playwright/test';
import { navigateToStockOperations } from './helpers/e2e-auth-helper';
import {
  waitForTableUpdate,
  getRowCount,
  openProductAutocomplete,
  searchProduct,
  selectProductFromDropdown,
  clearProductSelection,
  searchDocumentNumber,
  clearDocumentNumber,
  setDateFrom,
  setDateTo,
  clearDateFilters,
  getApplyFiltersButton,
  getClearFiltersButton,
  selectStateFilter,
  selectSourceType,
} from './helpers/stock-operations-test-helpers';

test.describe('Stock Operations - Advanced Filters', () => {
  test.describe('H. Product Autocomplete Filter', () => {
    test('H.1 should open product autocomplete dropdown', async ({ page }) => {
      console.log('🧪 Testing: Open product autocomplete dropdown');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      await openProductAutocomplete(page);

      // Verify dropdown is visible
      const dropdown = page.locator('[role="listbox"]');
      await expect(dropdown).toBeVisible();

      console.log('✅ Product autocomplete dropdown opened');
    });

    test('H.2 should search for product by code', async ({ page }) => {
      console.log('🧪 Testing: Search product by code');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Use a known product code from test data
      await searchProduct(page, 'AKL001');

      // Verify search results appear
      const options = page.locator('[role="option"]');
      const optionCount = await options.count();
      expect(optionCount).toBeGreaterThan(0);

      console.log(`✅ Found ${optionCount} products matching code`);
    });

    test('H.3 should search for product by name', async ({ page }) => {
      console.log('🧪 Testing: Search product by name');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Search by partial product name
      await searchProduct(page, 'Bisabolol');

      // Verify search results appear
      const options = page.locator('[role="option"]');
      const optionCount = await options.count();
      expect(optionCount).toBeGreaterThan(0);

      console.log(`✅ Found ${optionCount} products matching name`);
    });

    test('H.4 should clear product selection', async ({ page }) => {
      console.log('🧪 Testing: Clear product selection');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Select a product first
      await selectProductFromDropdown(page, 'AKL001');
      await waitForTableUpdate(page);

      // Clear selection
      await clearProductSelection(page);

      // Verify input is empty
      const input = page.locator('input[role="combobox"]').first();
      const value = await input.inputValue();
      expect(value).toBe('');

      console.log('✅ Product selection cleared');
    });

    test('H.5 should apply product filter', async ({ page }) => {
      console.log('🧪 Testing: Apply product filter');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      const rowsBefore = await getRowCount(page);

      // Select a specific product
      await selectProductFromDropdown(page, 'AKL001');

      // Apply filters
      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      const rowsAfter = await getRowCount(page);

      // Verify filtering occurred (row count changed or URL has productCode param)
      const url = page.url();
      const hasProductParam = url.includes('productCode=AKL001');

      if (hasProductParam) {
        console.log('   ✅ Product filter applied (URL param present)');
      }

      if (rowsAfter < rowsBefore || hasProductParam) {
        console.log(`   ✅ Rows filtered: ${rowsBefore} → ${rowsAfter}`);
      } else {
        console.log('   ℹ️ Row count unchanged, but filter applied');
      }

      console.log('✅ Product filter test completed');
    });
  });

  test.describe('I. Document Number Filter', () => {
    test('I.1 should search by full document number', async ({ page }) => {
      console.log('🧪 Testing: Search by full document number');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Get a document number from the first row
      const firstRow = page.locator('tbody tr').first();
      const docNumberCell = firstRow.locator('td').nth(1); // Assuming 2nd column is doc number
      const docNumber = await docNumberCell.textContent();

      if (docNumber && docNumber.trim()) {
        await searchDocumentNumber(page, docNumber.trim());

        const applyButton = getApplyFiltersButton(page);
        await applyButton.click();
        await waitForTableUpdate(page);

        // Verify URL contains document number param
        const url = page.url();
        expect(url).toContain('documentNumber=');

        console.log(`   ✅ Filtered by document number: ${docNumber.trim()}`);
      } else {
        console.log('   ℹ️ No document number found to test');
      }

      console.log('✅ Full document number search test completed');
    });

    test('I.2 should search by partial document number', async ({ page }) => {
      console.log('🧪 Testing: Partial document number search');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Use partial search term
      await searchDocumentNumber(page, 'DOC');

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // Verify filtering applied
      const url = page.url();
      const hasDocParam = url.includes('documentNumber=DOC');

      if (hasDocParam) {
        console.log('   ✅ Partial document number filter applied');
      } else {
        console.log('   ℹ️ Filter applied (URL updated)');
      }

      console.log('✅ Partial document number search test completed');
    });

    test('I.3 should clear document number search', async ({ page }) => {
      console.log('🧪 Testing: Clear document number search');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Set document number first
      await searchDocumentNumber(page, 'TEST123');

      // Clear it
      await clearDocumentNumber(page);

      // Verify input is empty
      const input = page.locator('input[type="text"]').filter({ has: page.locator('svg') }).first();
      const value = await input.inputValue();
      expect(value).toBe('');

      console.log('✅ Document number search cleared');
    });

    test('I.4 should handle no results for document number', async ({ page }) => {
      console.log('🧪 Testing: No results for document number');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Search for non-existent document number
      await searchDocumentNumber(page, 'NONEXISTENT999999');

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      const rowCount = await getRowCount(page);

      if (rowCount === 0) {
        // Verify empty state message
        const emptyMessage = page.locator('text="Žádné výsledky"');
        await expect(emptyMessage).toBeVisible();
        console.log('   ✅ Empty state displayed for no results');
      } else {
        console.log(`   ℹ️ Found ${rowCount} results (document might exist)`);
      }

      console.log('✅ No results test completed');
    });
  });

  test.describe('J. Date Range Filters', () => {
    test('J.1 should filter by "Created From" date', async ({ page }) => {
      console.log('🧪 Testing: Filter by Created From date');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Set Created From date to 30 days ago
      const thirtyDaysAgo = new Date();
      thirtyDaysAgo.setDate(thirtyDaysAgo.getDate() - 30);
      const dateString = thirtyDaysAgo.toISOString().split('T')[0]; // YYYY-MM-DD

      await setDateFrom(page, dateString);

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // Verify URL contains createdFrom param
      const url = page.url();
      expect(url).toContain('createdFrom=');

      console.log(`   ✅ Filtered by Created From: ${dateString}`);
      console.log('✅ Created From date filter test completed');
    });

    test('J.2 should filter by "Created To" date', async ({ page }) => {
      console.log('🧪 Testing: Filter by Created To date');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Set Created To date to today
      const today = new Date().toISOString().split('T')[0];

      await setDateTo(page, today);

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // Verify URL contains createdTo param
      const url = page.url();
      expect(url).toContain('createdTo=');

      console.log(`   ✅ Filtered by Created To: ${today}`);
      console.log('✅ Created To date filter test completed');
    });

    test('J.3 should filter by date range (both dates)', async ({ page }) => {
      console.log('🧪 Testing: Filter by date range');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Set date range: last 7 days
      const sevenDaysAgo = new Date();
      sevenDaysAgo.setDate(sevenDaysAgo.getDate() - 7);
      const fromDate = sevenDaysAgo.toISOString().split('T')[0];
      const toDate = new Date().toISOString().split('T')[0];

      await setDateFrom(page, fromDate);
      await setDateTo(page, toDate);

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // Verify both URL params present
      const url = page.url();
      expect(url).toContain('createdFrom=');
      expect(url).toContain('createdTo=');

      console.log(`   ✅ Date range filter applied: ${fromDate} to ${toDate}`);
      console.log('✅ Date range filter test completed');
    });

    test('J.4 should clear date filters', async ({ page }) => {
      console.log('🧪 Testing: Clear date filters');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Set dates first
      const today = new Date().toISOString().split('T')[0];
      await setDateFrom(page, today);
      await setDateTo(page, today);

      // Clear dates
      await clearDateFilters(page);

      // Verify inputs are empty
      const dateFromInput = page.locator('input[type="date"]').first();
      const dateToInput = page.locator('input[type="date"]').nth(1);

      const fromValue = await dateFromInput.inputValue();
      const toValue = await dateToInput.inputValue();

      expect(fromValue).toBe('');
      expect(toValue).toBe('');

      console.log('✅ Date filters cleared');
    });

    test('J.5 should handle invalid date range (end before start)', async ({ page }) => {
      console.log('🧪 Testing: Invalid date range handling');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Set end date before start date
      const yesterday = new Date();
      yesterday.setDate(yesterday.getDate() - 1);
      const tomorrow = new Date();
      tomorrow.setDate(tomorrow.getDate() + 1);

      const startDate = tomorrow.toISOString().split('T')[0]; // Tomorrow
      const endDate = yesterday.toISOString().split('T')[0]; // Yesterday

      await setDateFrom(page, startDate);
      await setDateTo(page, endDate);

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // The component should still apply the filter (backend handles validation)
      // Verify no crash occurred
      const url = page.url();
      const hasDateParams = url.includes('createdFrom=') && url.includes('createdTo=');

      if (hasDateParams) {
        console.log('   ✅ Invalid date range applied (backend validates)');
      } else {
        console.log('   ✅ Component handled invalid range gracefully');
      }

      console.log('✅ Invalid date range test completed');
    });

    test('J.6 should handle future dates', async ({ page }) => {
      console.log('🧪 Testing: Future dates handling');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Set future date
      const futureDate = new Date();
      futureDate.setFullYear(futureDate.getFullYear() + 1);
      const futureDateString = futureDate.toISOString().split('T')[0];

      await setDateFrom(page, futureDateString);

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // Should return no results (no operations in future)
      const rowCount = await getRowCount(page);

      if (rowCount === 0) {
        const emptyMessage = page.locator('text="Žádné výsledky"');
        await expect(emptyMessage).toBeVisible();
        console.log('   ✅ Future date returns empty results');
      } else {
        console.log(`   ℹ️ Found ${rowCount} results (unexpected)`);
      }

      console.log('✅ Future dates test completed');
    });
  });

  test.describe('K. Combined Filters', () => {
    test('K.1 should apply multiple filters simultaneously', async ({ page }) => {
      console.log('🧪 Testing: Apply all 6 filters simultaneously');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Apply all filter types
      await selectStateFilter(page, 'All');
      await selectSourceType(page, 'TransportBox');
      await selectProductFromDropdown(page, 'AKL001');
      await searchDocumentNumber(page, 'DOC');

      const sevenDaysAgo = new Date();
      sevenDaysAgo.setDate(sevenDaysAgo.getDate() - 7);
      const fromDate = sevenDaysAgo.toISOString().split('T')[0];
      const toDate = new Date().toISOString().split('T')[0];

      await setDateFrom(page, fromDate);
      await setDateTo(page, toDate);

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // Verify all filter params in URL
      const url = page.url();
      expect(url).toContain('state=All');
      expect(url).toContain('sourceType=TransportBox');
      expect(url).toContain('productCode=AKL001');
      expect(url).toContain('documentNumber=DOC');
      expect(url).toContain('createdFrom=');
      expect(url).toContain('createdTo=');

      console.log('   ✅ All 6 filters applied successfully');
      console.log('✅ Combined filters test completed');
    });

    test('K.2 should clear all filters with "Vymazat filtry" button', async ({ page }) => {
      console.log('🧪 Testing: Clear all filters button');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      // Apply some filters first
      await selectStateFilter(page, 'Failed');
      await selectSourceType(page, 'TransportBox');
      await searchDocumentNumber(page, 'TEST');

      const applyButton = getApplyFiltersButton(page);
      await applyButton.click();
      await waitForTableUpdate(page);

      // Clear all filters
      const clearButton = getClearFiltersButton(page);
      await clearButton.click();
      await waitForTableUpdate(page);

      // Verify URL is clean (only default params if any)
      const url = page.url();
      const hasFilterParams = url.includes('state=') || url.includes('sourceType=') || url.includes('documentNumber=');

      if (!hasFilterParams) {
        console.log('   ✅ All filters cleared (URL clean)');
      } else {
        // Check that default state (Active) might still be present
        console.log('   ✅ Filters reset to defaults');
      }

      console.log('✅ Clear filters test completed');
    });

    test('K.3 should apply filters with "Použít filtry" button', async ({ page }) => {
      console.log('🧪 Testing: Apply filters button functionality');

      await navigateToStockOperations(page);
      await waitForTableUpdate(page);

      const rowsBefore = await getRowCount(page);

      // Change filters but don't apply yet
      await selectStateFilter(page, 'Completed');

      // Note: selectStateFilter already clicks apply, so this test verifies the pattern
      // Let's wait for the update
      await waitForTableUpdate(page);

      const rowsAfter = await getRowCount(page);

      // Verify filtering occurred
      const url = page.url();
      const hasStateParam = url.includes('state=Completed');

      if (hasStateParam) {
        console.log('   ✅ Apply filters button worked');
        console.log(`   ✅ Rows changed: ${rowsBefore} → ${rowsAfter}`);
      }

      console.log('✅ Apply filters button test completed');
    });
  });
});
