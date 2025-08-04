import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis - Color Strips', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3001/purchase/stock-analysis');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
  });

  test('shows color strips when no status filter is applied', async ({ page }) => {
    // Wait for data to load
    await page.waitForTimeout(1500);
    
    // Check if table has rows
    const tableRows = page.locator('tbody tr');
    const rowCount = await tableRows.count();
    console.log('Table rows count:', rowCount);
    
    if (rowCount > 0) {
      // Look for color strips in the first few rows
      for (let i = 0; i < Math.min(3, rowCount); i++) {
        const row = tableRows.nth(i);
        const colorStrips = row.locator('div[class*="w-1"][class*="h-8"]');
        const stripCount = await colorStrips.count();
        
        console.log(`Row ${i} color strips:`, stripCount);
        
        if (stripCount > 0) {
          const stripClasses = await colorStrips.first().getAttribute('class');
          console.log(`Row ${i} strip classes:`, stripClasses);
          
          // Should have color classes like bg-red-500, bg-amber-500, etc.
          expect(stripClasses).toMatch(/bg-(red|amber|emerald|blue|gray)-(400|500)/);
        }
      }
    }
  });

  test('hides color strips when status filter is applied', async ({ page }) => {
    // Wait for initial data
    await page.waitForTimeout(1500);
    
    // First check that strips are visible with "All" filter
    const initialStrips = await page.locator('div[class*="w-1"][class*="h-8"]').count();
    console.log('Initial color strips count:', initialStrips);
    
    // Click on Critical filter
    const criticalButton = page.locator('button').filter({ hasText: 'Kritické:' });
    if (await criticalButton.count() > 0) {
      await criticalButton.first().click();
      await page.waitForTimeout(1000);
      
      // Check that color strips are now hidden
      const filteredStrips = await page.locator('div[class*="w-1"][class*="h-8"]').count();
      console.log('Filtered color strips count:', filteredStrips);
      
      // Should be 0 because we're filtering by specific status
      expect(filteredStrips).toBe(0);
    }
    
    // Reset to "All" filter
    const allButton = page.locator('button').filter({ hasText: 'Celkem:' });
    if (await allButton.count() > 0) {
      await allButton.first().click();
      await page.waitForTimeout(1000);
      
      // Color strips should be visible again
      const resetStrips = await page.locator('div[class*="w-1"][class*="h-8"]').count();
      console.log('Reset color strips count:', resetStrips);
      
      // Should be greater than 0 again
      expect(resetStrips).toBeGreaterThan(0);
    }
  });

  test('product name is displayed prominently above product code', async ({ page }) => {
    // Wait for data
    await page.waitForTimeout(1500);
    
    const tableRows = page.locator('tbody tr');
    const rowCount = await tableRows.count();
    
    if (rowCount > 0) {
      const firstRow = tableRows.first();
      const productCell = firstRow.locator('td').first();
      
      // Check if the structure is correct
      const productNameElements = productCell.locator('div.text-sm.font-bold.text-gray-900');
      const productCodeElements = productCell.locator('div.text-xs.text-gray-500');
      
      const nameCount = await productNameElements.count();
      const codeCount = await productCodeElements.count();
      
      console.log('Product name elements:', nameCount);
      console.log('Product code elements:', codeCount);
      
      // Should have at least one product name and one code
      expect(nameCount).toBeGreaterThan(0);
      expect(codeCount).toBeGreaterThan(0);
      
      if (nameCount > 0 && codeCount > 0) {
        const productName = await productNameElements.first().textContent();
        const productCode = await productCodeElements.first().textContent();
        
        console.log('Product name:', productName);
        console.log('Product code:', productCode);
        
        // Name should be longer/more descriptive than code
        expect(productName?.length || 0).toBeGreaterThan(3);
        expect(productCode?.length || 0).toBeGreaterThan(0);
      }
    }
  });

  test('different severity levels have different color strips', async ({ page }) => {
    // Wait for data
    await page.waitForTimeout(1500);
    
    const colorStrips = page.locator('div[class*="w-1"][class*="h-8"]');
    const stripCount = await colorStrips.count();
    console.log('Total color strips:', stripCount);
    
    if (stripCount > 0) {
      const seenColors = new Set();
      
      // Check first few strips for different colors
      const maxCheck = Math.min(6, stripCount);
      for (let i = 0; i < maxCheck; i++) {
        const strip = colorStrips.nth(i);
        const classes = await strip.getAttribute('class');
        console.log(`Strip ${i} classes:`, classes);
        
        if (classes) {
          // Extract color class (bg-color-shade)
          const colorMatch = classes.match(/bg-(red|amber|emerald|blue|gray)-(400|500)/);
          if (colorMatch) {
            const colorName = colorMatch[1];
            seenColors.add(colorName);
            console.log(`Strip ${i} color:`, colorName);
          }
        }
      }
      
      console.log('Unique colors seen:', Array.from(seenColors));
      
      // Should have at least 2 different colors (since we have different severities in mock data)
      expect(seenColors.size).toBeGreaterThanOrEqual(2);
    }
  });
});