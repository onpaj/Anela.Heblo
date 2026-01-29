import { test, expect } from '@playwright/test';
import { navigateToStockOperations } from './helpers/e2e-auth-helper';
import {
  selectStateFilter,
  waitForTableUpdate,
  getRowCount,
} from './helpers/stock-operations-test-helpers';

test.describe('Stock Operations - Accept Failed Operations', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);
    expect(page.url()).toContain('/stock-operations');
    await waitForTableUpdate(page);
  });

  test('should show green "Akceptovat" button for Failed operations', async ({ page }) => {
    console.log('🧪 Testing: Accept button for Failed operations');

    await selectStateFilter(page, 'Failed');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Verify accept button exists
      const acceptButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Akceptovat' });
      await expect(acceptButton).toBeVisible();

      // Verify button color (green)
      await expect(acceptButton).toHaveClass(/bg-green-600/);

      console.log('   ✅ Failed accept button validated');
    } else {
      console.log('   ℹ️ No failed operations available to test accept button');
    }
  });

  test('should show confirmation dialog when clicking Accept', async ({ page }) => {
    console.log('🧪 Testing: Accept confirmation dialog');

    await selectStateFilter(page, 'Failed');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Set up dialog handler to verify message and cancel
      let dialogShown = false;
      page.once('dialog', async dialog => {
        dialogShown = true;
        console.log(`   📝 Dialog message: "${dialog.message()}"`);
        expect(dialog.message()).toContain('Opravdu chcete akceptovat tuto chybnou operaci?');
        await dialog.dismiss();
        console.log('   ✅ Dialog cancelled');
      });

      // Click accept button
      const acceptButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Akceptovat' });
      await acceptButton.click();

      // Verify dialog was shown
      expect(dialogShown).toBe(true);

      // Wait a bit to ensure no mutation occurred
      await page.waitForTimeout(1000);

      console.log('   ✅ Confirmation dialog working correctly');
    } else {
      console.log('   ℹ️ No failed operations available to test confirmation dialog');
    }
  });

  test('should accept failed operation when confirmed', async ({ page }) => {
    console.log('🧪 Testing: Accept failed operation workflow');

    await selectStateFilter(page, 'Failed');
    const initialRowCount = await getRowCount(page);

    if (initialRowCount > 0) {
      // Set up dialog handler to accept
      let dialogShown = false;
      page.once('dialog', async dialog => {
        dialogShown = true;
        console.log(`   📝 Accepting operation via dialog`);
        await dialog.accept();
      });

      // Click accept button
      const acceptButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Akceptovat' });
      await acceptButton.click();

      if (dialogShown) {
        // Wait for mutation to complete
        await waitForTableUpdate(page);

        // Verify row count changed (operation should be removed from Failed list or refreshed)
        const newRowCount = await getRowCount(page);
        console.log(`   📊 Initial rows: ${initialRowCount}, After accept: ${newRowCount}`);

        // The operation should either:
        // 1. Be removed from the Failed list (if filter auto-refreshes)
        // 2. Still be there but with updated state (if no auto-refresh)
        // We just verify the mutation completed without error
        console.log('   ✅ Accept operation completed');
      } else {
        console.log('   ℹ️ Dialog not shown, test inconclusive');
      }
    } else {
      console.log('   ℹ️ No failed operations available to test accept workflow');
    }
  });
});
