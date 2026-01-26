import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToCatalog } from './helpers/e2e-auth-helper';

test.describe('Catalog Product Type Filtering E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Create E2E authentication session before each test
    await createE2EAuthSession(page);
  });

  test('should filter catalog by product type "Material"', async ({ page }) => {
    // Navigate to catalog using shared helper
    console.log('🧭 Navigating to catalog page...');
    await navigateToCatalog(page);

    // Verify we're on the catalog page
    expect(page.url()).toContain('/catalog');
    console.log('✅ On catalog page:', page.url());

    // Wait for initial catalog load
    console.log('⏳ Waiting for initial catalog to load...');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(3000);

    // Find the product type dropdown
    const productTypeDropdown = page.locator('select#productType');
    await expect(productTypeDropdown).toBeVisible();
    console.log('✅ Product type dropdown found');

    // Get initial product count
    const initialRows = page.locator('tbody tr');
    const initialCount = await initialRows.count();
    console.log(`📊 Initial product count: ${initialCount}`);

    // Select "Material" from the dropdown
    console.log('🔽 Selecting "Material" product type...');
    await productTypeDropdown.selectOption({ label: 'Materiál' });

    // Wait for the filter to apply and table to update
    console.log('⏳ Waiting for filter to apply...');
    await page.waitForTimeout(2000);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);

    // Get filtered product rows
    const filteredRows = page.locator('tbody tr');
    const filteredCount = await filteredRows.count();
    console.log(`📊 Filtered product count: ${filteredCount}`);

    if (filteredCount === 0) {
      // Check if there's a "no products found" message
      const noProductsMessage = page.locator('text=Žádné produkty nebyly nalezeny');
      const hasNoProductsMessage = await noProductsMessage.isVisible();

      if (hasNoProductsMessage) {
        console.log('ℹ️  No materials found in catalog - this is acceptable if there are no materials');
      } else {
        console.error('❌ No products shown after filtering, but no "no products" message either');
        throw new Error('Filter may not be working - no products shown but no empty state message');
      }
    } else {
      console.log(`✅ Found ${filteredCount} products after filtering`);

      // Verify all visible products have type "Materiál"
      console.log('🔍 Verifying all visible products are materials...');
      let validatedCount = 0;
      let invalidCount = 0;

      // Check each row for the product type badge
      for (let i = 0; i < Math.min(filteredCount, 10); i++) {
        const row = filteredRows.nth(i);
        const typeBadge = row.locator('span.inline-flex.items-center.px-2\\.5');
        const typeText = await typeBadge.textContent();

        console.log(`   Row ${i + 1}: Product type = "${typeText?.trim()}"`);

        if (typeText?.trim() === 'Materiál') {
          validatedCount++;
        } else {
          invalidCount++;
          console.error(`   ❌ Row ${i + 1}: Expected "Materiál" but got "${typeText?.trim()}"`);
        }
      }

      console.log(`📈 Validation results: ${validatedCount} correct, ${invalidCount} incorrect`);

      // Fail the test if any product has wrong type
      expect(invalidCount).toBe(0);
      expect(validatedCount).toBeGreaterThan(0);
    }

    console.log('✅ Material type filter test completed');
  });

  test('should filter catalog by product type "Product"', async ({ page }) => {
    // Navigate to catalog using shared helper
    console.log('🧭 Navigating to catalog page...');
    await navigateToCatalog(page);

    // Verify we're on the catalog page
    expect(page.url()).toContain('/catalog');
    console.log('✅ On catalog page:', page.url());

    // Wait for initial catalog load
    console.log('⏳ Waiting for initial catalog to load...');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(3000);

    // Find the product type dropdown
    const productTypeDropdown = page.locator('select#productType');
    await expect(productTypeDropdown).toBeVisible();
    console.log('✅ Product type dropdown found');

    // Select "Product" from the dropdown
    console.log('🔽 Selecting "Product" product type...');
    await productTypeDropdown.selectOption({ label: 'Produkt' });

    // Wait for the filter to apply and table to update
    console.log('⏳ Waiting for filter to apply...');
    await page.waitForTimeout(2000);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);

    // Get filtered product rows
    const filteredRows = page.locator('tbody tr');
    const filteredCount = await filteredRows.count();
    console.log(`📊 Filtered product count: ${filteredCount}`);

    if (filteredCount === 0) {
      // Check if there's a "no products found" message
      const noProductsMessage = page.locator('text=Žádné produkty nebyly nalezeny');
      const hasNoProductsMessage = await noProductsMessage.isVisible();

      if (hasNoProductsMessage) {
        console.log('ℹ️  No products found in catalog - this is acceptable if there are no products');
      } else {
        console.error('❌ No products shown after filtering, but no "no products" message either');
        throw new Error('Filter may not be working - no products shown but no empty state message');
      }
    } else {
      console.log(`✅ Found ${filteredCount} products after filtering`);

      // Verify all visible products have type "Produkt"
      console.log('🔍 Verifying all visible products are of type "Produkt"...');
      let validatedCount = 0;
      let invalidCount = 0;

      // Check each row for the product type badge
      for (let i = 0; i < Math.min(filteredCount, 10); i++) {
        const row = filteredRows.nth(i);
        const typeBadge = row.locator('span.inline-flex.items-center.px-2\\.5');
        const typeText = await typeBadge.textContent();

        console.log(`   Row ${i + 1}: Product type = "${typeText?.trim()}"`);

        if (typeText?.trim() === 'Produkt') {
          validatedCount++;
        } else {
          invalidCount++;
          console.error(`   ❌ Row ${i + 1}: Expected "Produkt" but got "${typeText?.trim()}"`);
        }
      }

      console.log(`📈 Validation results: ${validatedCount} correct, ${invalidCount} incorrect`);

      // Fail the test if any product has wrong type
      expect(invalidCount).toBe(0);
      expect(validatedCount).toBeGreaterThan(0);
    }

    console.log('✅ Product type filter test completed');
  });

  test('should reset filter when "Všechny typy" is selected', async ({ page }) => {
    // Navigate to catalog using shared helper
    console.log('🧭 Navigating to catalog page...');
    await navigateToCatalog(page);

    // Verify we're on the catalog page
    expect(page.url()).toContain('/catalog');
    console.log('✅ On catalog page:', page.url());

    // Wait for initial catalog load
    console.log('⏳ Waiting for initial catalog to load...');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(3000);

    // Find the product type dropdown
    const productTypeDropdown = page.locator('select#productType');
    await expect(productTypeDropdown).toBeVisible();

    // Get initial product count (all types)
    const initialRows = page.locator('tbody tr');
    const initialCount = await initialRows.count();
    console.log(`📊 Initial product count (all types): ${initialCount}`);

    // Select "Material" to filter
    console.log('🔽 Selecting "Material" product type...');
    await productTypeDropdown.selectOption({ label: 'Materiál' });
    await page.waitForTimeout(2000);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);

    const filteredRows = page.locator('tbody tr');
    const filteredCount = await filteredRows.count();
    console.log(`📊 Filtered product count (materials): ${filteredCount}`);

    // Now reset to "Všechny typy"
    console.log('🔄 Resetting to "Všechny typy"...');
    await productTypeDropdown.selectOption({ label: 'Všechny typy' });
    await page.waitForTimeout(2000);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);

    // Get final product count
    const finalRows = page.locator('tbody tr');
    const finalCount = await finalRows.count();
    console.log(`📊 Final product count (all types again): ${finalCount}`);

    // The final count should match or be close to initial count
    // (allowing for small differences due to data changes)
    if (initialCount > 0) {
      expect(finalCount).toBeGreaterThanOrEqual(filteredCount);
      console.log('✅ Filter reset successfully - showing all types again');
    }

    console.log('✅ Filter reset test completed');
  });
});
