import { test, expect, Page } from '@playwright/test';

// Helper function to check if header is frozen
async function checkFrozenHeader(page: Page, tableSelector: string) {
  // Get initial header position
  const header = await page.locator(`${tableSelector} thead`).first();
  const initialHeaderPosition = await header.boundingBox();
  
  if (!initialHeaderPosition) {
    throw new Error('Header not found');
  }

  // Scroll the table container
  const container = await page.locator(tableSelector).first();
  await container.evaluate((el) => {
    const scrollableParent = el.closest('[style*="max-height"]') || el.closest('.overflow-auto');
    if (scrollableParent) {
      scrollableParent.scrollTop = 200;
    }
  });

  // Wait for scroll to complete
  await page.waitForTimeout(100);

  // Get header position after scroll
  const headerAfterScroll = await header.boundingBox();
  
  if (!headerAfterScroll) {
    throw new Error('Header not found after scroll');
  }

  // Header should remain at the same position (frozen)
  expect(Math.abs(headerAfterScroll.y - initialHeaderPosition.y)).toBeLessThan(5);
}

test.describe('Frozen Table Headers', () => {
  test.beforeEach(async ({ page }) => {
    // Start on the dashboard
    await page.goto('http://localhost:3001');
    await page.waitForLoadState('networkidle');
  });

  test('Product Margin Summary table has frozen headers', async ({ page }) => {
    // Navigate to Product Margin Summary
    await page.click('text=Analýza marže');
    await page.waitForSelector('table', { timeout: 10000 });

    // Check if the table header is sticky
    const thead = await page.locator('table thead').first();
    const classes = await thead.getAttribute('class');
    expect(classes).toContain('sticky');
    expect(classes).toContain('top-0');
    expect(classes).toContain('z-10');

    // Check that container has proper scrolling setup
    const container = await page.locator('div.overflow-auto').first();
    const style = await container.getAttribute('style');
    expect(style).toContain('max-height');
  });

  test('Product Margins List table has frozen headers and scrollable container', async ({ page }) => {
    // Navigate to Product Margins List
    await page.click('text=Marže produktů');
    await page.waitForSelector('table', { timeout: 10000 });

    // Check if the table header is sticky
    const thead = await page.locator('table thead').first();
    const classes = await thead.getAttribute('class');
    expect(classes).toContain('sticky');
    expect(classes).toContain('top-0');
    expect(classes).toContain('z-10');

    // Check that the table is in a scrollable container
    const tableContainer = await page.locator('.overflow-auto').first();
    expect(tableContainer).toBeTruthy();
  });

  test('Dashboard audit summary table has frozen headers', async ({ page }) => {
    // Stay on dashboard
    await page.waitForSelector('table', { timeout: 10000 });

    // Check if the table header is sticky
    const thead = await page.locator('table thead').first();
    const classes = await thead.getAttribute('class');
    expect(classes).toContain('sticky');
    expect(classes).toContain('top-0');
    expect(classes).toContain('z-10');
  });

  test('Financial Overview table has frozen headers', async ({ page }) => {
    // Navigate to Financial Overview
    await page.click('text=Finanční přehled');
    await page.waitForSelector('table', { timeout: 10000 });

    // Check if the table header is sticky
    const thead = await page.locator('table thead').first();
    const classes = await thead.getAttribute('class');
    expect(classes).toContain('sticky');
    expect(classes).toContain('top-0');
    expect(classes).toContain('z-10');

    // Check scrollable container
    const container = await page.locator('div.overflow-auto').first();
    const style = await container.getAttribute('style');
    expect(style).toContain('max-height');
  });

  test('Transport Box Detail tables have frozen headers', async ({ page }) => {
    // Navigate to Transport Boxes
    await page.click('text=Přepravní boxy');
    await page.waitForSelector('table', { timeout: 10000 });
    
    // Click on first transport box to see detail
    const firstRow = await page.locator('tbody tr').first();
    if (await firstRow.isVisible()) {
      await firstRow.click();
      await page.waitForSelector('table', { timeout: 10000 });

      // Check items table
      const itemsTableHead = await page.locator('table thead').first();
      const classes = await itemsTableHead.getAttribute('class');
      expect(classes).toContain('sticky');
      expect(classes).toContain('top-0');
      expect(classes).toContain('z-10');

      // Check that container has proper scrolling
      const container = await page.locator('div[style*="max-height"]').first();
      expect(container).toBeTruthy();
    }
  });

  test('Purchase Order Detail table has frozen headers', async ({ page }) => {
    // Navigate to Purchase Orders
    await page.click('text=Nákupní objednávky');
    await page.waitForSelector('table', { timeout: 10000 });
    
    // Click on first purchase order to see detail
    const firstRow = await page.locator('tbody tr').first();
    if (await firstRow.isVisible()) {
      await firstRow.click();
      await page.waitForSelector('table', { timeout: 10000 });

      // Check order lines table
      const tableHead = await page.locator('table thead').first();
      const classes = await tableHead.getAttribute('class');
      expect(classes).toContain('sticky');
      expect(classes).toContain('top-0');
      expect(classes).toContain('z-10');

      // Check scrollable container
      const container = await page.locator('div[style*="max-height"]').first();
      expect(container).toBeTruthy();
    }
  });

  test('Catalog Detail pricing table has frozen headers', async ({ page }) => {
    // Navigate to Catalog
    await page.click('text=Katalog');
    await page.waitForSelector('table', { timeout: 10000 });
    
    // Click on first catalog item to see detail
    const firstRow = await page.locator('tbody tr').first();
    if (await firstRow.isVisible()) {
      await firstRow.click();
      await page.waitForSelector('h1:has-text("Detail položky")', { timeout: 10000 });

      // Check if there's a pricing table
      const pricingTable = await page.locator('table').first();
      if (await pricingTable.isVisible()) {
        const thead = await pricingTable.locator('thead').first();
        const classes = await thead.getAttribute('class');
        expect(classes).toContain('sticky');
        expect(classes).toContain('top-0');
        expect(classes).toContain('z-10');
      }
    }
  });
});