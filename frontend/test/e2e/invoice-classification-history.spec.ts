import { test, expect } from '@playwright/test';
import { navigateToApp } from './helpers/e2e-auth-helper';

test.describe('Invoice Classification History', () => {
  test.beforeEach(async ({ page }) => {
    // Establish E2E authentication session with full frontend setup
    await navigateToApp(page);

    await page.goto('/purchase/invoice-classification');

    // Wait for page to load
    await page.waitForSelector('body', { timeout: 10000 });

    // Navigate to History tab
    const historyTab = page.locator('text=Historie');
    if (await historyTab.count() > 0) {
      await historyTab.click();
      await page.waitForTimeout(1000);
    }
  });

  test('pagination functionality', async ({ page }) => {
    // Wait for the table to load
    await page.waitForSelector('table', { timeout: 15000 });
    
    // Check if pagination exists
    const pagination = page.locator('[aria-label="Pagination"]');
    
    if (await pagination.count() > 0) {
      // Test page size selector
      const pageSizeSelector = page.locator('select').filter({ hasText: /Zobrazit/ }).first();
      
      if (await pageSizeSelector.count() > 0) {
        // Change page size to 10
        await pageSizeSelector.selectOption('10');
        await page.waitForTimeout(1000); // Wait for data to load
        
        // Change page size to 50
        await pageSizeSelector.selectOption('50');
        await page.waitForTimeout(1000);
        
        // Verify the selector is working
        const selectedValue = await pageSizeSelector.inputValue();
        expect(selectedValue).toBe('50');
      }
      
      // Test navigation buttons if multiple pages exist
      const nextButton = page.locator('button').filter({ hasText: /Next|Další/ }).first();
      const prevButton = page.locator('button').filter({ hasText: /Previous|Předchozí/ }).first();
      
      if (await nextButton.count() > 0 && !(await nextButton.isDisabled())) {
        await nextButton.click();
        await page.waitForTimeout(1000);
        
        // Go back
        if (await prevButton.count() > 0 && !(await prevButton.isDisabled())) {
          await prevButton.click();
          await page.waitForTimeout(1000);
        }
      }
    }
    
    // Basic test that the page loaded successfully
    expect(await page.locator('table').count()).toBeGreaterThan(0);
  });

  test('filters functionality', async ({ page }) => {
    // Test search filters
    const searchInput = page.locator('input[placeholder*="company"]').first();
    
    if (await searchInput.count() > 0) {
      await searchInput.fill('test');
      
      const searchButton = page.locator('button').filter({ hasText: /Search|Hledat/ }).first();
      if (await searchButton.count() > 0) {
        await searchButton.click();
        await page.waitForTimeout(1000);
      }
      
      // Clear filters
      const clearButton = page.locator('button').filter({ hasText: /Clear|Vymazat/ }).first();
      if (await clearButton.count() > 0) {
        await clearButton.click();
        await page.waitForTimeout(1000);
      }
    }
  });
});