import { test, expect } from '@playwright/test';
import { navigateToCatalog } from './helpers/e2e-auth-helper';
import { TestCatalogItems } from './fixtures/test-data';

test.describe('Catalog Margins Chart Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to catalog with full authentication
    await navigateToCatalog(page);
  });

  test('margins chart should not display current month', async ({ page }) => {
    test.setTimeout(60000);

    // Wait for catalog to load
    await page.waitForSelector('table', { timeout: 15000 });

    // Filter catalog to show only Products (not Materials or Semi-products)
    const typeDropdown = page.locator('select').filter({ hasText: 'Všechny typy' }).first();
    await typeDropdown.selectOption({ label: 'Produkt' });
    await page.waitForTimeout(2000);

    // Use test data fixture for product with margins
    const testProduct = TestCatalogItems.darkovyBalicek;
    const productRow = page.locator('tbody tr').filter({ hasText: testProduct.code }).first();

    // Verify product exists in catalog
    await expect(productRow).toBeVisible({ timeout: 10000 });
    console.log(`✅ Found test product: ${testProduct.code} - ${testProduct.name}`);

    // Click to open product detail
    await productRow.click();
    await page.waitForTimeout(2000);

    // Find Marže tab - it's a button in the detail view tabs (not the sidebar navigation)
    // The sidebar link has class "px-3 py-2", while the tab button has "px-4 py-2"
    const marginsTabButton = page.locator('button').filter({ hasText: 'Marže' }).first();
    await expect(marginsTabButton).toBeVisible({ timeout: 5000 });
    console.log('✅ Margins tab button is visible');

    await marginsTabButton.click();
    console.log('✅ Clicked margins tab');
    await page.waitForTimeout(1500);

    // Get current month label in Czech format
    const now = new Date();
    const currentMonthLabel = now.toLocaleDateString('cs-CZ', { month: 'short', year: 'numeric' });
    console.log(`Current month label: ${currentMonthLabel}`);

    // Check if chart or empty state exists
    const chartCanvas = page.locator('canvas').first();
    const emptyState = page.locator('text=/Náklady a marže za posledních/i, text=/Žádná data/i').first();

    const chartVisible = await chartCanvas.isVisible({ timeout: 5000 }).catch(() => false);
    const emptyStateVisible = await emptyState.isVisible({ timeout: 5000 }).catch(() => false);

    // CRITICAL: At least one MUST be visible - test should fail if neither appears
    expect(chartVisible || emptyStateVisible).toBe(true);

    if (chartVisible) {
      console.log('✅ Chart canvas found - verifying chart renders');
      expect(chartCanvas).toBeVisible();

      // Verify current month is NOT displayed in the chart area
      const bodyText = await page.textContent('body');
      expect(bodyText).not.toContain(currentMonthLabel);
      console.log(`✅ Test passed: Chart is rendering without current month (${currentMonthLabel})`);
    } else {
      console.log('✅ Empty state found - verifying 12 months message');
      const emptyStateText = await emptyState.textContent();
      expect(emptyStateText).toContain('12 měsíc');
      console.log('✅ Test passed: Empty state shows correct message (12 months without current)');
    }
  });
});
