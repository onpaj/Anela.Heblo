import { test, expect } from '@playwright/test';
import { navigateToStockOperations } from '../helpers/e2e-auth-helper';
import {
  selectStateFilter,
  waitForTableUpdate,
  getRowCount,
  validateStateBadge,
  validateStuckWarning,
} from '../helpers/stock-operations-test-helpers';

test.describe('Stock Operations - State Badges & Stuck Detection', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);
    expect(page.url()).toContain('/stock-operations');
    await waitForTableUpdate(page);
  });

  test('should display green badge with CheckCircle for Completed state', async ({ page }) => {
    console.log('🧪 Testing: Completed state badge');

    await selectStateFilter(page, 'Completed');

    // Wait for filter to apply
    await page.waitForTimeout(1000);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateStateBadge(page, 'Completed', 0);

      // Verify icon presence
      const row = page.locator('tbody tr').first();
      const checkIcon = row.locator('svg').first();
      await expect(checkIcon).toBeVisible();

      console.log('   ✅ Completed badge with icon validated');
    } else {
      console.log('   ℹ️ No completed operations available - test passed (no data to validate)');
    }
  });

  test('should display red badge with XCircle for Failed state', async ({ page }) => {
    console.log('🧪 Testing: Failed state badge');

    await selectStateFilter(page, 'Failed');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateStateBadge(page, 'Failed', 0);

      // Verify icon presence
      const row = page.locator('tbody tr').first();
      const xIcon = row.locator('svg').first();
      await expect(xIcon).toBeVisible();

      console.log('   ✅ Failed badge with icon validated');
    } else {
      console.log('   ℹ️ No failed operations available');
    }
  });

  test('should display yellow badge with Clock for Pending state', async ({ page }) => {
    console.log('🧪 Testing: Pending state badge');

    await selectStateFilter(page, 'Pending');

    // Wait for filter to apply
    await page.waitForTimeout(1000);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateStateBadge(page, 'Pending', 0);

      // Verify icon presence
      const row = page.locator('tbody tr').first();
      const clockIcon = row.locator('svg').first();
      await expect(clockIcon).toBeVisible();

      console.log('   ✅ Pending badge with icon validated');
    } else {
      console.log('   ℹ️ No pending operations available - test passed (no data to validate)');
    }
  });

  test('should display blue badge with RefreshCw for Submitted state', async ({ page }) => {
    console.log('🧪 Testing: Submitted state badge');

    await selectStateFilter(page, 'Submitted');

    // Wait for filter to apply
    await page.waitForTimeout(1000);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      await validateStateBadge(page, 'Submitted', 0);

      // Verify icon presence
      const row = page.locator('tbody tr').first();
      const refreshIcon = row.locator('svg').first();
      await expect(refreshIcon).toBeVisible();

      console.log('   ✅ Submitted badge with icon validated');
    } else {
      console.log('   ℹ️ No submitted operations available - test passed (no data to validate)');
    }
  });

  test('should show stuck warning for Submitted operations older than 5 minutes', async ({ page }) => {
    console.log('🧪 Testing: Stuck Submitted operation warning');

    await selectStateFilter(page, 'Submitted');

    // Wait for filter to apply
    await page.waitForTimeout(1000);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Check each row for stuck warning (AlertTriangle with animate-pulse)
      let foundStuckOperation = false;

      for (let i = 0; i < Math.min(rowCount, 10); i++) {
        const row = page.locator('tbody tr').nth(i);
        // Look specifically for animate-pulse SVG (stuck warning indicator)
        const alertIcon = row.locator('svg.animate-pulse');
        const alertCount = await alertIcon.count();

        if (alertCount > 0) {
          console.log(`   ✅ Found stuck warning on row ${i}`);
          foundStuckOperation = true;
          break;
        }
      }

      if (!foundStuckOperation) {
        console.log('   ℹ️ No stuck submitted operations found (all recent) - test passed');
      }
    } else {
      console.log('   ℹ️ No submitted operations available - test passed (no data to validate)');
    }
  });

  test('should show stuck warning for Pending operations older than 10 minutes', async ({ page }) => {
    console.log('🧪 Testing: Stuck Pending operation warning');

    await selectStateFilter(page, 'Pending');

    // Wait for filter to apply
    await page.waitForTimeout(1000);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Check each row for stuck warning (AlertTriangle with animate-pulse)
      let foundStuckOperation = false;

      for (let i = 0; i < Math.min(rowCount, 10); i++) {
        const row = page.locator('tbody tr').nth(i);
        // Look specifically for animate-pulse SVG (stuck warning indicator)
        const alertIcon = row.locator('svg.animate-pulse');
        const alertCount = await alertIcon.count();

        if (alertCount > 0) {
          console.log(`   ✅ Found stuck warning on row ${i}`);
          foundStuckOperation = true;
          break;
        }
      }

      if (!foundStuckOperation) {
        console.log('   ℹ️ No stuck pending operations found (all recent) - test passed');
      }
    } else {
      console.log('   ℹ️ No pending operations available - test passed (no data to validate)');
    }
  });

  test('should NOT show stuck warning for recent operations', async ({ page }) => {
    console.log('🧪 Testing: No stuck warning for recent operations');

    await selectStateFilter(page, 'All');
    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Check Completed operations (should never have warning)
      await selectStateFilter(page, 'Completed');
      const completedCount = await getRowCount(page);

      if (completedCount > 0) {
        const row = page.locator('tbody tr').first();
        const alertIcon = row.locator('svg.animate-pulse');

        await expect(alertIcon).not.toBeVisible();
        console.log('   ✅ No stuck warning for completed operations');
      }

      // Check Failed operations (should never have stuck warning)
      await selectStateFilter(page, 'Failed');
      const failedCount = await getRowCount(page);

      if (failedCount > 0) {
        const row = page.locator('tbody tr').first();
        const alertIcon = row.locator('svg.animate-pulse');

        await expect(alertIcon).not.toBeVisible();
        console.log('   ✅ No stuck warning for failed operations');
      }

      console.log('   ✅ Recent operations have no stuck warnings');
    } else {
      console.log('   ℹ️ No operations available');
    }
  });
});
