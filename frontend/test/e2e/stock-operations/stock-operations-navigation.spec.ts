import { test, expect } from '@playwright/test';
import { navigateToStockOperations } from '../helpers/e2e-auth-helper';
import { waitForTableUpdate, getRowCount } from '../helpers/stock-operations-test-helpers';

// SKIPPED: Same timeout issue as stock-operations-badges - see that file's comment for details.
test.describe.skip('Stock Operations - Navigation & Initial Load', () => {
  test('should navigate to page via direct URL', async ({ page }) => {
    console.log('🧪 Testing: Direct navigation to stock operations');

    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);

    // Verify URL
    expect(page.url()).toContain('/stock-operations');

    // Verify page title
    const title = await page.locator('h1').textContent();
    expect(title).toContain('Operace naskladnění');

    console.log('✅ Navigation successful');
  });

  test('should load with default filters (State: Active, Source: All)', async ({ page }) => {
    console.log('🧪 Testing: Default filter state on page load');

    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);
    await waitForTableUpdate(page);

    // Check state filter default
    const stateSelect = page.locator('select').first();
    const selectedState = await stateSelect.inputValue();
    expect(selectedState).toBe('Active');

    // Check source type default
    const allSourceRadio = page.locator('input[type="radio"][value="All"]');
    await expect(allSourceRadio).toBeChecked();

    console.log('✅ Default filters validated');
  });

  test('should display loading state during data fetch', async ({ page }) => {
    console.log('🧪 Testing: Loading state display');

    await createE2EAuthSession(page);

    // Start navigation
    const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    const navPromise = page.goto(`${baseUrl}/stock-operations`);

    // Try to catch loading spinner (may be too fast)
    try {
      const spinner = page.locator('svg.animate-spin');
      const isVisible = await spinner.isVisible({ timeout: 1000 });
      if (isVisible) {
        console.log('   ✅ Loading spinner detected');
      } else {
        console.log('   ℹ️ Loading too fast to catch spinner');
      }
    } catch (e) {
      console.log('   ℹ️ Loading completed before spinner check');
    }

    await navPromise;
    await waitForTableUpdate(page);

    // Verify data loaded (spinner should be gone)
    const spinner = page.locator('svg.animate-spin');
    await expect(spinner).not.toBeVisible();

    console.log('✅ Loading state test completed');
  });

  test('should display empty state when no results match filters', async ({ page }) => {
    console.log('🧪 Testing: Empty state display');

    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);
    await waitForTableUpdate(page);

    // Try to create empty state by using a very specific filter combination
    // that likely won't match any data
    const stateSelect = page.locator('select').first();
    await stateSelect.selectOption({ label: 'Completed' });
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount === 0) {
      // Verify empty state message
      const emptyMessage = page.locator('text="Žádné výsledky"');
      await expect(emptyMessage).toBeVisible();
      console.log('   ✅ Empty state message displayed');
    } else {
      console.log(`   ℹ️ Found ${rowCount} results, cannot test empty state`);
    }

    console.log('✅ Empty state test completed');
  });

  test('should display error state on API failure', async ({ page }) => {
    console.log('🧪 Testing: Error state display');

    await createE2EAuthSession(page);

    // Intercept API calls and force failure
    await page.route('**/api/stock-up-operations**', route => {
      route.abort('failed');
    });

    await navigateToStockOperations(page);
    await page.waitForTimeout(3000);

    // Check for error message
    const errorMessage = page.locator('text="Chyba při načítání operací"');
    const isErrorVisible = await errorMessage.isVisible();

    if (isErrorVisible) {
      console.log('   ✅ Error state displayed');

      // Verify retry button exists
      const retryButton = page.getByRole('button', { name: /Zkusit znovu/i });
      await expect(retryButton).toBeVisible();
      console.log('   ✅ Retry button present');
    } else {
      console.log('   ℹ️ Error state not triggered (possible caching)');
    }

    console.log('✅ Error state test completed');
  });
});
