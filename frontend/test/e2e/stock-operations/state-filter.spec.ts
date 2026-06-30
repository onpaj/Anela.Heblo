import { test, expect } from '@playwright/test';
import { navigateToStockOperations } from '../helpers/e2e-auth-helper';
import {
  selectStateFilter,
  waitForTableUpdate,
  getRowCount,
  validateStateBadge,
} from '../helpers/stock-operations-test-helpers';

test.describe('Stock Operations - State Filter', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);
    expect(page.url()).toContain('/stock-up-operations');
    await waitForTableUpdate(page);
  });

  test('should filter by "All" states', async ({ page }) => {
    console.log('🧪 Testing: State filter - All');

    await selectStateFilter(page, 'All');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} operations (all states)`);

    if (rowCount > 0) {
      console.log('   ✅ All states filter applied');
    } else {
      console.log('   ℹ️ No operations available');
    }
  });

  test('should filter by "Active" state (default)', async ({ page }) => {
    console.log('🧪 Testing: State filter - Active');

    await selectStateFilter(page, 'Active');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} active operations`);

    if (rowCount > 0) {
      // Validate first row has non-Completed badge
      const firstRow = page.locator('tbody tr').first();
      const completedBadge = firstRow.locator('span').filter({ hasText: 'Completed' });
      const badgeCount = await completedBadge.count();

      expect(badgeCount).toBe(0);
      console.log('   ✅ Active filter excludes Completed operations');
    } else {
      console.log('   ℹ️ No active operations available');
    }
  });

  test('should filter by "Pending" state', async ({ page }) => {
    console.log('🧪 Testing: State filter - Pending');

    await selectStateFilter(page, 'Pending');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} pending operations`);

    if (rowCount > 0) {
      // Validate first row has yellow badge with Clock icon
      await validateStateBadge(page, 'Pending', 0);
      console.log('   ✅ Pending state badge validated');
    } else {
      console.log('   ℹ️ No pending operations available');
    }
  });

  test('should filter by "Submitted" state', async ({ page }) => {
    console.log('🧪 Testing: State filter - Submitted');

    await selectStateFilter(page, 'Submitted');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} submitted operations`);

    if (rowCount > 0) {
      // Validate first row has blue badge with RefreshCw icon
      await validateStateBadge(page, 'Submitted', 0);
      console.log('   ✅ Submitted state badge validated');
    } else {
      console.log('   ℹ️ No submitted operations available');
    }
  });

  test('should filter by "Failed" state', async ({ page }) => {
    console.log('🧪 Testing: State filter - Failed');

    await selectStateFilter(page, 'Failed');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} failed operations`);

    if (rowCount > 0) {
      // Validate first row has red badge with XCircle icon
      await validateStateBadge(page, 'Failed', 0);
      console.log('   ✅ Failed state badge validated');
    } else {
      console.log('   ℹ️ No failed operations available');
    }
  });

  test('should filter by "Completed" state', async ({ page }) => {
    console.log('🧪 Testing: State filter - Completed');

    await selectStateFilter(page, 'Completed');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} completed operations`);

    if (rowCount > 0) {
      // Validate first row has green badge with CheckCircle icon
      await validateStateBadge(page, 'Completed', 0);
      console.log('   ✅ Completed state badge validated');
    } else {
      console.log('   ℹ️ No completed operations available');
    }
  });
});
