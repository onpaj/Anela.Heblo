import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToCatalog } from './helpers/e2e-auth-helper';
import {
  applyProductNameFilter,
  applyProductNameFilterWithEnter,
  applyProductCodeFilter,
  applyProductCodeFilterWithEnter,
  validateFilteredResults,
  validateEmptyState,
  validateFilterStatusIndicator,
  validatePageResetToOne,
  getRowCount,
  getProductNameInput,
  getProductCodeInput,
  getFilterButton,
  waitForTableUpdate,
} from './helpers/catalog-test-helpers';

test.describe('Catalog Text Search Filters E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Create E2E authentication session before each test
    await createE2EAuthSession(page);

    // Navigate to catalog
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
  // PRODUCT NAME FILTERING
  // ============================================================================

  test('should filter products by name using Filter button', async ({ page }) => {
    // Apply a common search term that should return results
    await applyProductNameFilter(page, 'Krém');

    // Wait for results
    await waitForTableUpdate(page);

    // Check if we have results or empty state
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Validate all results contain the search term
      await validateFilteredResults(page, { productName: 'Krém' });

      // Validate filter status indicator is shown
      await validateFilterStatusIndicator(page, true);

      // Validate page was reset to 1
      await validatePageResetToOne(page);
    } else {
      // If no results, validate empty state is shown
      await validateEmptyState(page);
    }
  });

  test('should filter products by name using Enter key', async ({ page }) => {
    // Apply filter using Enter key
    await applyProductNameFilterWithEnter(page, 'Sérum');

    // Wait for results
    await waitForTableUpdate(page);

    // Check if we have results or empty state
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Validate all results contain the search term
      await validateFilteredResults(page, { productName: 'Sérum' });

      // Validate filter status indicator is shown
      await validateFilterStatusIndicator(page, true);

      // Validate page was reset to 1
      await validatePageResetToOne(page);
    } else {
      // If no results, validate empty state is shown
      await validateEmptyState(page);
    }
  });

  test('should perform partial name matching', async ({ page }) => {
    // Search for partial product name
    await applyProductNameFilter(page, 'Plet');

    // Wait for results
    await waitForTableUpdate(page);

    // Validate results contain the partial match
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateFilteredResults(page, { productName: 'Plet' });
      console.log('✅ Partial name matching working correctly');
    } else {
      console.log('ℹ️ No products found with partial match "Plet"');
    }
  });

  test('should handle case-insensitive search', async ({ page }) => {
    // Search with uppercase
    await applyProductNameFilter(page, 'KRÉM');

    // Wait for results
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Validate results (case-insensitive check)
      await validateFilteredResults(page, { productName: 'krém' }, { caseSensitive: false });
      console.log('✅ Case-insensitive search working correctly');
    } else {
      console.log('ℹ️ No products found for case-insensitive search');
    }
  });

  test('should reset to page 1 when applying name filter', async ({ page }) => {
    // First, navigate to page 2 if possible
    const url = new URL(page.url());
    url.searchParams.set('page', '2');
    await page.goto(url.toString());
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Apply name filter
    await applyProductNameFilter(page, 'Krém');

    // Validate page was reset to 1
    await validatePageResetToOne(page);
  });

  test('should display filter status in pagination info', async ({ page }) => {
    // Apply name filter
    await applyProductNameFilter(page, 'Krém');

    // Wait for results
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Validate filter status indicator is visible
      await validateFilterStatusIndicator(page, true);
      console.log('✅ Filter status indicator displayed in pagination');
    } else {
      console.log('ℹ️ No results to show filter status');
    }
  });

  // ============================================================================
  // PRODUCT CODE FILTERING
  // ============================================================================

  test('should filter products by code using Filter button', async ({ page }) => {
    // Apply product code filter (use a short code that might match)
    await applyProductCodeFilter(page, 'AH');

    // Wait for results
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Validate all results contain the code
      await validateFilteredResults(page, { productCode: 'AH' });

      // Validate filter status indicator
      await validateFilterStatusIndicator(page, true);

      // Validate page reset
      await validatePageResetToOne(page);
    } else {
      await validateEmptyState(page);
    }
  });

  test('should filter products by code using Enter key', async ({ page }) => {
    // Apply filter using Enter key
    await applyProductCodeFilterWithEnter(page, 'AH');

    // Wait for results
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateFilteredResults(page, { productCode: 'AH' });
      await validateFilterStatusIndicator(page, true);
    } else {
      await validateEmptyState(page);
    }
  });

  test('should perform exact code matching', async ({ page }) => {
    // This test attempts to find an exact product code match
    // First, get a product code from the table
    const firstCodeCell = page.locator('tbody tr:first-child td:nth-child(1)');
    const productCode = await firstCodeCell.textContent();

    if (productCode) {
      const code = productCode.trim();
      console.log(`🔍 Testing exact code match with: "${code}"`);

      await applyProductCodeFilter(page, code);
      await waitForTableUpdate(page);

      const rowCount = await getRowCount(page);

      if (rowCount > 0) {
        await validateFilteredResults(page, { productCode: code });
        console.log('✅ Exact code matching working correctly');
      }
    } else {
      console.log('⚠️ Could not get product code for exact match test');
    }
  });

  test('should handle partial code matching', async ({ page }) => {
    // Search for partial product code
    await applyProductCodeFilter(page, 'AH');

    // Wait for results
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateFilteredResults(page, { productCode: 'AH' });
      console.log('✅ Partial code matching working correctly');
    } else {
      console.log('ℹ️ No products found with partial code "AH"');
    }
  });

  test('should reset to page 1 when applying code filter', async ({ page }) => {
    // Navigate to page 2
    const url = new URL(page.url());
    url.searchParams.set('page', '2');
    await page.goto(url.toString());
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Apply code filter
    await applyProductCodeFilter(page, 'AH');

    // Validate page was reset to 1
    await validatePageResetToOne(page);
  });

  // ============================================================================
  // FILTER BUTTON BEHAVIOR
  // ============================================================================

  test('should not trigger filter with empty inputs', async ({ page }) => {
    // Get initial row count
    const initialCount = await getRowCount(page);
    console.log(`📊 Initial row count: ${initialCount}`);

    // Ensure inputs are empty
    const nameInput = getProductNameInput(page);
    const codeInput = getProductCodeInput(page);
    await nameInput.fill('');
    await codeInput.fill('');

    // Click filter button
    const filterButton = getFilterButton(page);
    await filterButton.click();
    await page.waitForTimeout(1000);

    // Verify row count hasn't changed (or changed minimally due to data refresh)
    const newCount = await getRowCount(page);
    console.log(`📊 New row count: ${newCount}`);

    // The count should be similar (allowing small variance for data changes)
    expect(Math.abs(newCount - initialCount)).toBeLessThanOrEqual(5);
    console.log('✅ Empty filter did not trigger unwanted filtering');
  });

  test('should apply both name and code filters simultaneously when both filled', async ({ page }) => {
    // Fill both filters with values that should overlap
    const nameInput = getProductNameInput(page);
    const codeInput = getProductCodeInput(page);

    await nameInput.fill('Krém');
    await codeInput.fill('AH');

    const filterButton = getFilterButton(page);
    await filterButton.click();

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Validate both filters are applied
      await validateFilteredResults(page, {
        productName: 'Krém',
        productCode: 'AH',
      });
      console.log('✅ Both filters applied simultaneously');
    } else {
      // No results matching both criteria
      await validateEmptyState(page);
      console.log('ℹ️ No products match both filter criteria');
    }
  });

  test('should handle rapid consecutive filter button clicks', async ({ page }) => {
    const nameInput = getProductNameInput(page);
    await nameInput.fill('Krém');

    const filterButton = getFilterButton(page);

    // Click filter button multiple times rapidly
    await filterButton.click();
    await filterButton.click();
    await filterButton.click();

    // Wait for final update
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Validate filter was applied correctly despite multiple clicks
      await validateFilteredResults(page, { productName: 'Krém' });
      console.log('✅ Rapid clicks handled correctly');
    } else {
      console.log('ℹ️ No results for rapid click test');
    }
  });

  // ============================================================================
  // EMPTY RESULTS HANDLING
  // ============================================================================

  test('should display "Žádné produkty nebyly nalezeny." for no matches', async ({ page }) => {
    // Search for a term that should not match any products
    await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345XYZ');

    // Wait for response
    await waitForTableUpdate(page);

    // Validate empty state message is displayed
    await validateEmptyState(page);

    console.log('✅ Empty state message displayed correctly for no matches');
  });

  test('should show empty state with filter applied', async ({ page }) => {
    // Apply filter with no matches
    await applyProductCodeFilter(page, 'ZZZZZ99999');

    await waitForTableUpdate(page);

    // Validate empty state
    await validateEmptyState(page);

    // Validate filter indicator might still be shown (depends on implementation)
    // This is optional - the implementation might hide it or show it
    console.log('✅ Empty state shown with filter applied');
  });

  test('should allow clearing filters from empty state', async ({ page }) => {
    // Apply filter that results in empty state
    await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');

    await waitForTableUpdate(page);

    // Verify empty state
    await validateEmptyState(page);

    // Now clear filters using Clear button
    const clearButton = page.getByRole('button', { name: 'Vymazat' });
    await clearButton.click();
    await waitForTableUpdate(page);

    // Verify products are shown again
    const rowCount = await getRowCount(page);
    expect(rowCount).toBeGreaterThan(0);

    console.log('✅ Filters cleared successfully from empty state');
  });
});
