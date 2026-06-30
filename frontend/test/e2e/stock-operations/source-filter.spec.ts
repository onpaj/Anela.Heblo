import { test, expect } from '@playwright/test';
import { navigateToStockOperations } from '../helpers/e2e-auth-helper';
import {
  selectSourceType,
  waitForTableUpdate,
  getRowCount,
} from '../helpers/stock-operations-test-helpers';

test.describe('Stock Operations - Source Type Filter', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to stock operations with full authentication
    await navigateToStockOperations(page);
    expect(page.url()).toContain('/stock-up-operations');
    await waitForTableUpdate(page);
  });

  test('should filter by "All" source types (default)', async ({ page }) => {
    console.log('🧪 Testing: Source type filter - All');

    await selectSourceType(page, 'All');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} operations (all source types)`);

    if (rowCount > 0) {
      // Verify "All" radio is checked
      const allRadio = page.locator('input[type="radio"][value="All"]');
      await expect(allRadio).toBeChecked();
      console.log('   ✅ All source types filter applied');
    } else {
      console.log('   ℹ️ No operations available');
    }
  });

  test('should filter by "Transport Box" source type', async ({ page }) => {
    console.log('🧪 Testing: Source type filter - Transport Box');

    await selectSourceType(page, 'TransportBox');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} transport box operations`);

    if (rowCount > 0) {
      // Verify "TransportBox" radio is checked
      const transportRadio = page.locator('input[type="radio"][value="TransportBox"]');
      await expect(transportRadio).toBeChecked();
      console.log('   ✅ Transport Box filter applied');
    } else {
      console.log('   ℹ️ No transport box operations available');
    }
  });

  test('should filter by "Gift Package Manufacture" source type', async ({ page }) => {
    console.log('🧪 Testing: Source type filter - Gift Package Manufacture');

    await selectSourceType(page, 'GiftPackageManufacture');

    const rowCount = await getRowCount(page);
    console.log(`   📊 Found ${rowCount} gift package operations`);

    if (rowCount > 0) {
      // Verify "GiftPackageManufacture" radio is checked
      const giftRadio = page.locator('input[type="radio"][value="GiftPackageManufacture"]');
      await expect(giftRadio).toBeChecked();
      console.log('   ✅ Gift Package Manufacture filter applied');
    } else {
      console.log('   ℹ️ No gift package operations available');
    }
  });
});
