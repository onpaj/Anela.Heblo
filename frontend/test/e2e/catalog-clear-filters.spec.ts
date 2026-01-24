import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToCatalog } from './helpers/e2e-auth-helper';
import {
  applyProductNameFilter,
  applyProductCodeFilter,
  selectProductType,
  clearAllFilters,
  validateFiltersCleared,
  validateFilterStatusIndicator,
  validatePageResetToOne,
  validateEmptyState,
  getRowCount,
  getProductNameInput,
  getProductCodeInput,
  getProductTypeSelect,
  getClearButton,
  waitForTableUpdate,
} from './helpers/catalog-test-helpers';

test.describe('Catalog Clear Filters E2E Tests', () => {
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
  // CLEAR BUTTON FUNCTIONALITY
  // ============================================================================

  test('should clear product name filter', async ({ page }) => {
    // Apply name filter
    await applyProductNameFilter(page, 'Krém');

    const filteredCount = await getRowCount(page);
    console.log(`📊 Filtered row count: ${filteredCount}`);

    // Clear filters
    await clearAllFilters(page);

    // Validate input is cleared
    const nameInput = getProductNameInput(page);
    await expect(nameInput).toHaveValue('');

    // Validate more rows are shown (or at least the filter is removed)
    const clearedCount = await getRowCount(page);
    console.log(`📊 After clear: ${clearedCount} rows`);

    expect(clearedCount).toBeGreaterThanOrEqual(filteredCount);

    console.log('✅ Product name filter cleared successfully');
  });

  test('should clear product code filter', async ({ page }) => {
    // Apply code filter
    await applyProductCodeFilter(page, 'AH');

    const filteredCount = await getRowCount(page);
    console.log(`📊 Filtered row count: ${filteredCount}`);

    // Clear filters
    await clearAllFilters(page);

    // Validate input is cleared
    const codeInput = getProductCodeInput(page);
    await expect(codeInput).toHaveValue('');

    // Validate more rows shown
    const clearedCount = await getRowCount(page);
    console.log(`📊 After clear: ${clearedCount} rows`);

    expect(clearedCount).toBeGreaterThanOrEqual(filteredCount);

    console.log('✅ Product code filter cleared successfully');
  });

  test('should reset product type to "Všechny typy"', async ({ page }) => {
    // Select a specific product type
    await selectProductType(page, 'Produkt');

    const filteredCount = await getRowCount(page);
    console.log(`📊 Filtered row count: ${filteredCount}`);

    // Clear filters
    await clearAllFilters(page);

    // Validate product type is reset
    const typeSelect = getProductTypeSelect(page);
    const selectedValue = await typeSelect.inputValue();
    expect(selectedValue).toBe(''); // Empty value represents "Všechny typy"

    console.log('✅ Product type reset to "Všechny typy"');
  });

  test('should clear all filters simultaneously', async ({ page }) => {
    // Apply all three filter types
    const nameInput = getProductNameInput(page);
    const codeInput = getProductCodeInput(page);

    await nameInput.fill('Krém');
    await codeInput.fill('AH');
    await selectProductType(page, 'Produkt');

    // Apply text filters
    const filterButton = page.getByRole('button', { name: 'Filtrovat' });
    await filterButton.click();
    await waitForTableUpdate(page);

    const filteredCount = await getRowCount(page);
    console.log(`📊 Filtered row count (all filters): ${filteredCount}`);

    // Clear all filters
    await clearAllFilters(page);

    // Validate all filters are cleared
    await validateFiltersCleared(page);

    // Validate more rows shown
    const clearedCount = await getRowCount(page);
    console.log(`📊 After clear: ${clearedCount} rows`);

    expect(clearedCount).toBeGreaterThanOrEqual(filteredCount);

    console.log('✅ All filters cleared simultaneously');
  });

  test('should reset page to 1 after clearing', async ({ page }) => {
    // Apply filter and navigate to page 2
    await applyProductNameFilter(page, 'Krém');

    // Try to navigate to page 2
    const url = new URL(page.url());
    url.searchParams.set('page', '2');
    await page.goto(url.toString());
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Clear filters
    await clearAllFilters(page);

    // Validate page was reset to 1
    await validatePageResetToOne(page);

    console.log('✅ Page reset to 1 after clearing filters');
  });

  test('should reload full dataset after clearing', async ({ page }) => {
    // Get initial full dataset count
    const initialCount = await getRowCount(page);
    console.log(`📊 Initial full dataset: ${initialCount} rows`);

    // Apply filter to reduce results
    await applyProductNameFilter(page, 'Krém');

    const filteredCount = await getRowCount(page);
    console.log(`📊 Filtered count: ${filteredCount} rows`);

    // Clear filters
    await clearAllFilters(page);

    // Validate full dataset is reloaded
    const reloadedCount = await getRowCount(page);
    console.log(`📊 Reloaded count: ${reloadedCount} rows`);

    // Should be approximately the same as initial
    // (allowing small variance for data changes)
    expect(Math.abs(reloadedCount - initialCount)).toBeLessThanOrEqual(5);

    console.log('✅ Full dataset reloaded after clearing filters');
  });

  // ============================================================================
  // CLEAR BUTTON STATE
  // ============================================================================

  test('should be enabled when any filter is active', async ({ page }) => {
    // Apply a filter
    await applyProductNameFilter(page, 'Krém');

    // Check clear button is enabled
    const clearButton = getClearButton(page);
    await expect(clearButton).toBeEnabled();
    await expect(clearButton).toBeVisible();

    console.log('✅ Clear button is enabled when filter is active');
  });

  test('should handle clearing filters from empty result state', async ({ page }) => {
    // Apply filter that results in empty state
    await applyProductNameFilter(page, 'NONEXISTENTPRODUCT12345');

    await waitForTableUpdate(page);

    // Verify empty state
    await validateEmptyState(page);

    // Clear filters
    const clearButton = getClearButton(page);
    await expect(clearButton).toBeVisible();
    await clearButton.click();

    await waitForTableUpdate(page);

    // Verify products are shown again
    const rowCount = await getRowCount(page);
    expect(rowCount).toBeGreaterThan(0);

    // Verify filters are cleared
    await validateFiltersCleared(page);

    console.log('✅ Filters cleared successfully from empty result state');
  });

  // ============================================================================
  // INPUT FIELD RESET
  // ============================================================================

  test('should clear text input values visually', async ({ page }) => {
    // Fill text inputs
    const nameInput = getProductNameInput(page);
    const codeInput = getProductCodeInput(page);

    await nameInput.fill('TestName');
    await codeInput.fill('TestCode');

    // Apply filters
    const filterButton = page.getByRole('button', { name: 'Filtrovat' });
    await filterButton.click();
    await waitForTableUpdate(page);

    // Verify inputs have values
    await expect(nameInput).toHaveValue('TestName');
    await expect(codeInput).toHaveValue('TestCode');

    // Clear filters
    await clearAllFilters(page);

    // Verify inputs are visually cleared
    await expect(nameInput).toHaveValue('');
    await expect(codeInput).toHaveValue('');

    console.log('✅ Text input values cleared visually');
  });

  test('should reset dropdown to default option', async ({ page }) => {
    // Select a specific product type
    const typeSelect = getProductTypeSelect(page);
    await typeSelect.selectOption({ label: 'Materiál' });
    await waitForTableUpdate(page);

    // Verify selection
    let selectedValue = await typeSelect.inputValue();
    expect(selectedValue).not.toBe('');

    // Clear filters
    await clearAllFilters(page);

    // Verify dropdown is reset to default (empty value)
    selectedValue = await typeSelect.inputValue();
    expect(selectedValue).toBe('');

    console.log('✅ Dropdown reset to default option');
  });

  test('should clear filter status indicator', async ({ page }) => {
    // Apply filter
    await applyProductNameFilter(page, 'Krém');

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Verify filter status indicator is shown
      await validateFilterStatusIndicator(page, true);

      // Clear filters
      await clearAllFilters(page);

      // Verify filter status indicator is hidden
      await validateFilterStatusIndicator(page, false);

      console.log('✅ Filter status indicator cleared');
    } else {
      console.log('ℹ️ No results to test filter indicator');
    }
  });
});
