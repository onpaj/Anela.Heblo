import { test, expect } from '@playwright/test';

test.describe('CatalogDetail - Product Type specific display logic', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/catalog');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
  });

  test('should hide purchase history tab for SemiProduct and Product types', async ({ page }) => {
    // Wait for the catalog table to be visible
    await expect(page.locator('table')).toBeVisible();
    
    // Find and click on a product (assuming first row is a product)
    const firstRow = page.locator('tbody tr').first();
    await firstRow.click();
    
    // Wait for detail modal to open
    await expect(page.getByText('Základní informace')).toBeVisible();
    
    // Check the product type badge to determine expected behavior
    const productTypeBadge = page.locator('.bg-blue-100, .bg-purple-100, .bg-green-100, .bg-orange-100').first();
    const productTypeText = await productTypeBadge.textContent();
    
    if (productTypeText?.includes('Produkt') || productTypeText?.includes('Polotovar')) {
      // For Product and SemiProduct types, purchase history tab should NOT be visible
      await expect(page.getByRole('button', { name: 'Historie nákupů' })).not.toBeVisible();
    } else {
      // For other types (Material, Goods), purchase history tab should be visible
      await expect(page.getByRole('button', { name: 'Historie nákupů' })).toBeVisible();
    }
  });

  test('should show manufacture/consumption charts for SemiProduct', async ({ page }) => {
    // Try to find a SemiProduct in the catalog
    await page.waitForSelector('table tbody tr', { timeout: 10000 });
    
    // Look for a SemiProduct type in the table
    const rows = page.locator('tbody tr');
    const rowCount = await rows.count();
    
    let semiProductFound = false;
    
    for (let i = 0; i < Math.min(rowCount, 10); i++) {
      const row = rows.nth(i);
      const typeCell = row.locator('td').nth(1); // Assuming type is in second column
      const typeText = await typeCell.textContent();
      
      if (typeText?.includes('Polotovar')) {
        semiProductFound = true;
        await row.click();
        break;
      }
    }
    
    if (semiProductFound) {
      // Wait for detail modal to open
      await expect(page.getByText('Základní informace')).toBeVisible();
      
      // Check that chart tabs are visible (they should be for SemiProduct)
      await expect(page.getByRole('button', { name: 'Výroba' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Spotřeba' })).toBeVisible();
      
      // Check that purchase history tab is NOT visible
      await expect(page.getByRole('button', { name: 'Historie nákupů' })).not.toBeVisible();
      
      // Test switching between chart tabs
      await page.getByRole('button', { name: 'Výroba' }).click();
      await expect(page.getByText('Celkové shrnutí - výroba')).toBeVisible();
      
      await page.getByRole('button', { name: 'Spotřeba' }).click();
      await expect(page.getByText('Celkové shrnutí - spotřeba')).toBeVisible();
    } else {
      console.log('No SemiProduct found in catalog for testing');
    }
  });

  test('should show appropriate chart tabs for different product types', async ({ page }) => {
    // Test with multiple product types
    await page.waitForSelector('table tbody tr', { timeout: 10000 });
    
    const rows = page.locator('tbody tr');
    const rowCount = await rows.count();
    
    const testCases = [
      { type: 'Material', expectedInput: 'Nákup', expectedOutput: 'Spotřeba' },
      { type: 'Product', expectedInput: 'Výroba', expectedOutput: 'Prodeje' },
      { type: 'SemiProduct', expectedInput: 'Výroba', expectedOutput: 'Spotřeba' },
      { type: 'Goods', expectedInput: 'Nákup', expectedOutput: 'Prodeje' }
    ];
    
    for (const testCase of testCases) {
      // Look for products of this type
      for (let i = 0; i < Math.min(rowCount, 15); i++) {
        const row = rows.nth(i);
        const typeCell = row.locator('td').nth(1);
        const typeText = await typeCell.textContent();
        
        let matchesType = false;
        switch (testCase.type) {
          case 'Material':
            matchesType = typeText?.includes('Materiál') || false;
            break;
          case 'Product':
            matchesType = typeText?.includes('Produkt') || false;
            break;
          case 'SemiProduct':
            matchesType = typeText?.includes('Polotovar') || false;
            break;
          case 'Goods':
            matchesType = typeText?.includes('Zboží') || false;
            break;
        }
        
        if (matchesType) {
          await row.click();
          
          // Wait for detail modal to open
          await expect(page.getByText('Základní informace')).toBeVisible();
          
          // Check chart tabs if they should exist (all types except UNDEFINED)
          if (testCase.type !== 'UNDEFINED') {
            await expect(page.getByRole('button', { name: testCase.expectedInput })).toBeVisible();
            await expect(page.getByRole('button', { name: testCase.expectedOutput })).toBeVisible();
            
            // Test clicking the tabs
            await page.getByRole('button', { name: testCase.expectedInput }).click();
            await expect(page.getByText(`Celkové shrnutí - ${testCase.expectedInput.toLowerCase()}`)).toBeVisible();
            
            await page.getByRole('button', { name: testCase.expectedOutput }).click();
            await expect(page.getByText(`Celkové shrnutí - ${testCase.expectedOutput.toLowerCase()}`)).toBeVisible();
          }
          
          // Close modal and continue to next test case
          await page.getByRole('button', { name: 'Zavřít' }).click();
          break;
        }
      }
    }
  });

  test('should hide purchase history tab for Product type specifically', async ({ page }) => {
    // Look specifically for Product type
    await page.waitForSelector('table tbody tr', { timeout: 10000 });
    
    const rows = page.locator('tbody tr');
    const rowCount = await rows.count();
    
    let productFound = false;
    
    for (let i = 0; i < Math.min(rowCount, 10); i++) {
      const row = rows.nth(i);
      const typeCell = row.locator('td').nth(1);
      const typeText = await typeCell.textContent();
      
      if (typeText?.includes('Produkt')) {
        productFound = true;
        await row.click();
        
        // Wait for detail modal to open
        await expect(page.getByText('Základní informace')).toBeVisible();
        
        // Check that purchase history tab is NOT visible for Product type
        await expect(page.getByRole('button', { name: 'Historie nákupů' })).not.toBeVisible();
        
        // But chart tabs should be visible
        await expect(page.getByRole('button', { name: 'Výroba' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Prodeje' })).toBeVisible();
        
        break;
      }
    }
    
    expect(productFound).toBeTruthy();
  });
});