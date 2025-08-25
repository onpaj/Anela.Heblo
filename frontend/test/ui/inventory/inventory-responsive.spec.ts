import { test, expect } from '@playwright/test';

test.describe('Manufacturing Stock Analysis - Responsive Table', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3001');
  });

  test('should display all columns with proper widths on desktop', async ({ page }) => {
    // Set desktop viewport
    await page.setViewportSize({ width: 1920, height: 1080 });
    
    // Navigate to Manufacturing Stock Analysis
    await page.click('text=Řízení zásob');
    
    // Wait for table to load
    await page.waitForSelector('table');
    
    // Check that all columns are visible
    const headers = [
      'Produkt',
      'Skladem', 
      'Prodeje období',
      'Prodeje/den',
      'Nadsklad',
      'Zásoba dni',
      'Min zásoba',
      'Nadsklad %',
      'ks/šarže'
    ];
    
    for (const header of headers) {
      await expect(page.locator(`th:has-text("${header}")`)).toBeVisible();
    }
    
    // Verify table has proper min-width for horizontal scroll
    const table = page.locator('table');
    const tableStyle = await table.getAttribute('style');
    expect(tableStyle).toContain('minWidth: 1200px');
  });

  test('should enable horizontal scroll on tablet viewport', async ({ page }) => {
    // Set tablet viewport (narrower than table min-width)
    await page.setViewportSize({ width: 768, height: 1024 });
    
    // Navigate to Manufacturing Stock Analysis
    await page.click('text=Řízení zásob');
    
    // Wait for table container
    await page.waitForSelector('.overflow-x-auto');
    
    // Verify horizontal scroll is enabled
    const tableContainer = page.locator('.overflow-x-auto');
    await expect(tableContainer).toHaveCSS('overflow-x', 'auto');
    
    // Check that table maintains minimum width
    const table = page.locator('table');
    const tableStyle = await table.getAttribute('style');
    expect(tableStyle).toContain('minWidth: 1200px');
  });

  test('should enable horizontal scroll on mobile viewport', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 812 });
    
    // Navigate to Manufacturing Stock Analysis
    await page.click('text=Řízení zásob');
    
    // Wait for table container
    await page.waitForSelector('.overflow-x-auto');
    
    // Verify horizontal scroll is enabled
    const tableContainer = page.locator('.overflow-x-auto');
    await expect(tableContainer).toHaveCSS('overflow-x', 'auto');
    
    // Check that all essential columns maintain minimum widths
    const productColumn = page.locator('th:has-text("Produkt")');
    const productColumnStyle = await productColumn.getAttribute('style');
    expect(productColumnStyle).toContain('minWidth: 200px');
    
    const stockColumn = page.locator('th:has-text("Skladem")');
    const stockColumnStyle = await stockColumn.getAttribute('style');
    expect(stockColumnStyle).toContain('minWidth: 90px');
  });

  test('should scroll horizontally to show hidden columns', async ({ page }) => {
    // Set narrow viewport
    await page.setViewportSize({ width: 600, height: 800 });
    
    // Navigate to Manufacturing Stock Analysis
    await page.click('text=Řízení zásob');
    
    // Wait for table to load
    await page.waitForSelector('table');
    
    // Initially some columns should be off-screen
    const lastColumn = page.locator('th:has-text("ks/šarže")');
    
    // Scroll table container horizontally
    const tableContainer = page.locator('.overflow-x-auto');
    await tableContainer.evaluate(el => el.scrollLeft = el.scrollWidth - el.clientWidth);
    
    // Now the last column should be visible
    await expect(lastColumn).toBeVisible();
  });

  test('should maintain column proportions with percentage widths', async ({ page }) => {
    // Set wide viewport
    await page.setViewportSize({ width: 1600, height: 1200 });
    
    // Navigate to Manufacturing Stock Analysis
    await page.click('text=Řízení zásob');
    
    // Wait for table to load
    await page.waitForSelector('table');
    
    // Check that Product column takes up 25% width as specified
    const productColumn = page.locator('th:has-text("Produkt")');
    const productStyle = await productColumn.getAttribute('style');
    expect(productStyle).toContain('width: 25%');
    
    // Check other column widths
    const stockColumn = page.locator('th:has-text("Skladem")');
    const stockStyle = await stockColumn.getAttribute('style');
    expect(stockStyle).toContain('width: 10%');
    
    const salesPeriodColumn = page.locator('th:has-text("Prodeje období")');
    const salesPeriodStyle = await salesPeriodColumn.getAttribute('style');
    expect(salesPeriodStyle).toContain('width: 12%');
  });

  test('should handle subgrid rows with consistent column widths', async ({ page }) => {
    // Set desktop viewport
    await page.setViewportSize({ width: 1920, height: 1080 });
    
    // Navigate to Manufacturing Stock Analysis
    await page.click('text=Řízení zásob');
    
    // Wait for table to load
    await page.waitForSelector('table');
    
    // Look for expandable row (with chevron button)
    const expandButton = page.locator('.expand-button').first();
    if (await expandButton.isVisible()) {
      // Click to expand
      await expandButton.click();
      
      // Wait for subgrid to load
      await page.waitForTimeout(1000);
      
      // Check that subgrid cells have same styling as main rows
      const subgridCell = page.locator('tr.border-l-8 td').first();
      const subgridStyle = await subgridCell.getAttribute('style');
      expect(subgridStyle).toContain('minWidth: 200px');
      expect(subgridStyle).toContain('width: 25%');
    }
  });

  test('should maintain readability with smaller padding on narrow screens', async ({ page }) => {
    // Test that reduced padding still maintains readability
    await page.setViewportSize({ width: 768, height: 1024 });
    
    // Navigate to Manufacturing Stock Analysis
    await page.click('text=Řízení zásob');
    
    // Wait for table to load
    await page.waitForSelector('table');
    
    // Check that cells use px-3 (reduced padding) instead of px-6
    const tableCell = page.locator('tbody td').first();
    const cellClasses = await tableCell.getAttribute('class');
    expect(cellClasses).toContain('px-4'); // Product column has px-4
    
    const otherCell = page.locator('tbody td').nth(1);
    const otherCellClasses = await otherCell.getAttribute('class');
    expect(otherCellClasses).toContain('px-3'); // Other columns have px-3
  });
});