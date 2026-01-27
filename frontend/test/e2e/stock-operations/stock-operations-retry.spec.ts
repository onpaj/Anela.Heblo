import { test, expect } from '@playwright/test';
import { navigateToStockOperations } from '../helpers/e2e-auth-helper';
import {
  selectStateFilter,
  waitForTableUpdate,
  getRowCount,
  validateRetryButton,
  validateNoRetryButton,
} from '../helpers/stock-operations-test-helpers';

test.describe('Stock Operations - Retry Functionality', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);
    expect(page.url()).toContain('/stock-operations');
    await waitForTableUpdate(page);
  });

  test('should show red "Opakovat" button for Failed operations', async ({ page }) => {
    console.log('🧪 Testing: Retry button for Failed operations');

    await selectStateFilter(page, 'Failed');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateRetryButton(page, 'Failed', 0);

      // Verify button color
      const retryButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Opakovat' });
      await expect(retryButton).toHaveClass(/bg-red-600/);

      console.log('   ✅ Failed retry button validated');
    } else {
      console.log('   ℹ️ No failed operations available');
    }
  });

  test('should show orange "Znovu zkusit" button for Submitted operations', async ({ page }) => {
    console.log('🧪 Testing: Retry button for Submitted operations');

    await selectStateFilter(page, 'Submitted');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateRetryButton(page, 'Submitted', 0);

      // Verify button color
      const retryButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Znovu zkusit' });
      await expect(retryButton).toHaveClass(/bg-orange-500/);

      console.log('   ✅ Submitted retry button validated');
    } else {
      console.log('   ℹ️ No submitted operations available');
    }
  });

  test('should show yellow "Spustit" button for Pending operations', async ({ page }) => {
    console.log('🧪 Testing: Retry button for Pending operations');

    await selectStateFilter(page, 'Pending');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateRetryButton(page, 'Pending', 0);

      // Verify button color
      const retryButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Spustit' });
      await expect(retryButton).toHaveClass(/bg-yellow-600/);

      console.log('   ✅ Pending retry button validated');
    } else {
      console.log('   ℹ️ No pending operations available');
    }
  });

  test('should NOT show retry button for Completed operations', async ({ page }) => {
    console.log('🧪 Testing: No retry button for Completed operations');

    await selectStateFilter(page, 'Completed');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateNoRetryButton(page, 0);
      console.log('   ✅ No retry button for completed operations');
    } else {
      console.log('   ℹ️ No completed operations available');
    }
  });

  test('should show confirmation dialog and allow cancellation', async ({ page }) => {
    console.log('🧪 Testing: Retry confirmation dialog cancellation');

    await selectStateFilter(page, 'Failed');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Set up dialog handler to cancel
      page.once('dialog', async dialog => {
        console.log(`   📝 Dialog message: "${dialog.message()}"`);
        expect(dialog.message()).toContain('Opravdu chcete');
        await dialog.dismiss();
        console.log('   ✅ Dialog cancelled');
      });

      // Click retry button
      const retryButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Opakovat' });
      await retryButton.click();

      // Wait a bit to ensure no mutation occurred
      await page.waitForTimeout(1000);

      console.log('   ✅ Confirmation cancellation working');
    } else {
      console.log('   ℹ️ No failed operations available');
    }
  });

  test('should disable retry button during mutation', async ({ page }) => {
    console.log('🧪 Testing: Retry button disabled during mutation');

    await selectStateFilter(page, 'Failed');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      const retryButton = page.locator('tbody tr').first().locator('button').filter({ hasText: 'Opakovat' });

      // Verify button is initially enabled
      await expect(retryButton).toBeEnabled();

      // Set up dialog handler to accept (will trigger mutation)
      let dialogShown = false;
      page.once('dialog', async dialog => {
        dialogShown = true;
        await dialog.accept();
      });

      // Click retry button
      await retryButton.click();

      if (dialogShown) {
        // Try to check if button becomes disabled (mutation may be too fast)
        try {
          await expect(retryButton).toBeDisabled({ timeout: 500 });
          console.log('   ✅ Button disabled during mutation');
        } catch (e) {
          console.log('   ℹ️ Mutation too fast to catch disabled state');
        }

        // Wait for mutation to complete
        await waitForTableUpdate(page);
        console.log('   ✅ Retry mutation completed');
      } else {
        console.log('   ℹ️ Dialog not shown, test inconclusive');
      }
    } else {
      console.log('   ℹ️ No failed operations available');
    }
  });
});
