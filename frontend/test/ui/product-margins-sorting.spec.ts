import { test, expect } from '@playwright/test';

test.describe('Product Margins Sorting', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate directly to product margins page by URL
    await page.goto('http://localhost:3001/produkty/marze');
    await page.waitForLoadState('networkidle');
    
    // Wait for data to load
    await page.waitForSelector('table tbody tr', { timeout: 15000 });
    await page.waitForTimeout(1000);
  });

  test('should sort by margin percentage (default)', async ({ page }) => {
    // Check that margin percentage column header shows active sort
    const marginHeader = page.locator('th').filter({ hasText: 'Marže %' });
    
    // Should have descending sort indicator (ChevronDown should be active)
    const downArrow = marginHeader.locator('svg').nth(1);
    await expect(downArrow).toHaveClass(/text-indigo-600/);
    
    // Get first few margin values and verify they are in descending order
    const marginCells = page.locator('tbody tr td:nth-child(7)'); // 7th column is margin % (now we have 7 columns)
    const marginValues: number[] = [];
    
    const cellCount = await marginCells.count();
    const maxCellsToCheck = Math.min(cellCount, 5); // Check first 5 rows
    
    for (let i = 0; i < maxCellsToCheck; i++) {
      const cellText = await marginCells.nth(i).textContent();
      if (cellText && cellText !== '-') {
        const marginValue = parseFloat(cellText.replace('%', '').replace(',', '.'));
        if (!isNaN(marginValue)) {
          marginValues.push(marginValue);
        }
      }
    }
    
    // Verify descending order (if we have values)
    if (marginValues.length >= 2) {
      for (let i = 1; i < marginValues.length; i++) {
        expect(marginValues[i]).toBeLessThanOrEqual(marginValues[i - 1]);
      }
    }
  });

  test('should sort by product code ascending/descending', async ({ page }) => {
    // Click on product code header
    await page.getByText('Kód produktu').click();
    await page.waitForTimeout(500);
    
    // Check ascending sort
    const productCodeCells = page.locator('tbody tr td:nth-child(1)');
    const firstProductCode = await productCodeCells.first().textContent();
    const secondProductCode = await productCodeCells.nth(1).textContent();
    
    if (firstProductCode && secondProductCode) {
      expect(firstProductCode.localeCompare(secondProductCode)).toBeLessThanOrEqual(0);
    }
    
    // Click again for descending sort
    await page.getByText('Kód produktu').click();
    await page.waitForTimeout(500);
    
    const firstProductCodeDesc = await productCodeCells.first().textContent();
    const secondProductCodeDesc = await productCodeCells.nth(1).textContent();
    
    if (firstProductCodeDesc && secondProductCodeDesc) {
      expect(firstProductCodeDesc.localeCompare(secondProductCodeDesc)).toBeGreaterThanOrEqual(0);
    }
  });

  test('should sort by product name ascending/descending', async ({ page }) => {
    // Click on product name header
    await page.getByText('Název produktu').click();
    await page.waitForTimeout(500);
    
    // Check ascending sort
    const productNameCells = page.locator('tbody tr td:nth-child(2)');
    const firstProductName = await productNameCells.first().textContent();
    const secondProductName = await productNameCells.nth(1).textContent();
    
    if (firstProductName && secondProductName) {
      expect(firstProductName.localeCompare(secondProductName)).toBeLessThanOrEqual(0);
    }
    
    // Click again for descending sort
    await page.getByText('Název produktu').click();
    await page.waitForTimeout(500);
    
    const firstProductNameDesc = await productNameCells.first().textContent();
    const secondProductNameDesc = await productNameCells.nth(1).textContent();
    
    if (firstProductNameDesc && secondProductNameDesc) {
      expect(firstProductNameDesc.localeCompare(secondProductNameDesc)).toBeGreaterThanOrEqual(0);
    }
  });

  test('should sort by price without VAT ascending/descending', async ({ page }) => {
    // Click on price without VAT header
    await page.getByText('Cena bez DPH').click();
    await page.waitForTimeout(500);
    
    // Get price values and verify ascending order
    const priceCells = page.locator('tbody tr td:nth-child(3)');
    const priceValues: number[] = [];
    
    const cellCount = await priceCells.count();
    const maxCellsToCheck = Math.min(cellCount, 3);
    
    for (let i = 0; i < maxCellsToCheck; i++) {
      const cellText = await priceCells.nth(i).textContent();
      if (cellText && cellText !== '-') {
        const priceValue = parseFloat(cellText.replace('Kč', '').replace(/\s/g, '').replace(',', '.'));
        if (!isNaN(priceValue)) {
          priceValues.push(priceValue);
        }
      }
    }
    
    // Verify ascending order
    if (priceValues.length >= 2) {
      for (let i = 1; i < priceValues.length; i++) {
        expect(priceValues[i]).toBeGreaterThanOrEqual(priceValues[i - 1]);
      }
    }
    
    // Click again for descending sort
    await page.getByText('Cena bez DPH').click();
    await page.waitForTimeout(500);
    
    // Get price values again and verify descending order
    const priceValuesDesc: number[] = [];
    for (let i = 0; i < maxCellsToCheck; i++) {
      const cellText = await priceCells.nth(i).textContent();
      if (cellText && cellText !== '-') {
        const priceValue = parseFloat(cellText.replace('Kč', '').replace(/\s/g, '').replace(',', '.'));
        if (!isNaN(priceValue)) {
          priceValuesDesc.push(priceValue);
        }
      }
    }
    
    // Verify descending order
    if (priceValuesDesc.length >= 2) {
      for (let i = 1; i < priceValuesDesc.length; i++) {
        expect(priceValuesDesc[i]).toBeLessThanOrEqual(priceValuesDesc[i - 1]);
      }
    }
  });

  test('should sort by material cost ascending/descending', async ({ page }) => {
    // Click on material cost header
    await page.getByText('Materiál').click();
    await page.waitForTimeout(500);
    
    // Get material cost values and verify ascending order
    const materialCostCells = page.locator('tbody tr td:nth-child(5)'); // 5th column is material cost
    const materialCostValues: number[] = [];
    
    const cellCount = await materialCostCells.count();
    const maxCellsToCheck = Math.min(cellCount, 3);
    
    for (let i = 0; i < maxCellsToCheck; i++) {
      const cellText = await materialCostCells.nth(i).textContent();
      if (cellText && cellText !== '-') {
        const costValue = parseFloat(cellText.replace('Kč', '').replace(/\s/g, '').replace(',', '.'));
        if (!isNaN(costValue)) {
          materialCostValues.push(costValue);
        }
      }
    }
    
    // Verify ascending order
    if (materialCostValues.length >= 2) {
      for (let i = 1; i < materialCostValues.length; i++) {
        expect(materialCostValues[i]).toBeGreaterThanOrEqual(materialCostValues[i - 1]);
      }
    }
    
    // Click again for descending sort
    await page.getByText('Materiál').click();
    await page.waitForTimeout(500);
    
    // Verify descending order by checking the sort indicator
    const materialCostHeader = page.locator('th').filter({ hasText: 'Materiál' });
    const downArrow = materialCostHeader.locator('svg').nth(1);
    await expect(downArrow).toHaveClass(/text-indigo-600/);
  });

  test('should sort by handling cost ascending/descending', async ({ page }) => {
    // Click on handling cost header
    await page.getByText('Výroba průměr').click();
    await page.waitForTimeout(500);
    
    // Get handling cost values and verify ascending order
    const handlingCostCells = page.locator('tbody tr td:nth-child(6)'); // 6th column is handling cost
    const handlingCostValues: number[] = [];
    
    const cellCount = await handlingCostCells.count();
    const maxCellsToCheck = Math.min(cellCount, 3);
    
    for (let i = 0; i < maxCellsToCheck; i++) {
      const cellText = await handlingCostCells.nth(i).textContent();
      if (cellText && cellText !== '-') {
        const costValue = parseFloat(cellText.replace('Kč', '').replace(/\s/g, '').replace(',', '.'));
        if (!isNaN(costValue)) {
          handlingCostValues.push(costValue);
        }
      }
    }
    
    // Verify ascending order
    if (handlingCostValues.length >= 2) {
      for (let i = 1; i < handlingCostValues.length; i++) {
        expect(handlingCostValues[i]).toBeGreaterThanOrEqual(handlingCostValues[i - 1]);
      }
    }
    
    // Click again for descending sort
    await page.getByText('Výroba průměr').click();
    await page.waitForTimeout(500);
    
    // Verify descending order by checking the sort indicator
    const handlingCostHeader = page.locator('th').filter({ hasText: 'Výroba průměr' });
    const downArrow = handlingCostHeader.locator('svg').nth(1);
    await expect(downArrow).toHaveClass(/text-indigo-600/);
  });

  test('should sort by margin percentage ascending/descending when clicked', async ({ page }) => {
    // Click on margin percentage header to change from default descending to ascending
    await page.getByText('Marže %').click();
    await page.waitForTimeout(500);
    
    // Check ascending sort indicator
    const marginHeader = page.locator('th').filter({ hasText: 'Marže %' });
    const upArrow = marginHeader.locator('svg').first();
    await expect(upArrow).toHaveClass(/text-indigo-600/);
    
    // Get margin values and verify ascending order
    const marginCells = page.locator('tbody tr td:nth-child(7)');
    const marginValues: number[] = [];
    
    const cellCount = await marginCells.count();
    const maxCellsToCheck = Math.min(cellCount, 3);
    
    for (let i = 0; i < maxCellsToCheck; i++) {
      const cellText = await marginCells.nth(i).textContent();
      if (cellText && cellText !== '-') {
        const marginValue = parseFloat(cellText.replace('%', '').replace(',', '.'));
        if (!isNaN(marginValue)) {
          marginValues.push(marginValue);
        }
      }
    }
    
    // Verify ascending order
    if (marginValues.length >= 2) {
      for (let i = 1; i < marginValues.length; i++) {
        expect(marginValues[i]).toBeGreaterThanOrEqual(marginValues[i - 1]);
      }
    }
    
    // Click again for descending sort
    await page.getByText('Marže %').click();
    await page.waitForTimeout(500);
    
    // Check descending sort indicator
    const downArrow = marginHeader.locator('svg').nth(1);
    await expect(downArrow).toHaveClass(/text-indigo-600/);
  });

  test('should maintain sorting state across pagination', async ({ page }) => {
    // Click on product code to sort ascending
    await page.getByText('Kód produktu').click();
    await page.waitForTimeout(500);
    
    // Get first product code on page 1
    const firstRowProductCode = await page.locator('tbody tr:first-child td:first-child').textContent();
    
    // Check if pagination buttons exist
    const nextPageButton = page.getByRole('button', { name: 'Další' }).first();
    const nextPageButtonExists = await nextPageButton.isVisible().catch(() => false);
    
    if (nextPageButtonExists) {
      const hasNextPage = await nextPageButton.isEnabled();
      
      if (hasNextPage) {
        await nextPageButton.click();
        await page.waitForTimeout(500);
        
        // Check that sort indicator is still active
        const productCodeHeader = page.locator('th').filter({ hasText: 'Kód produktu' });
        const upArrow = productCodeHeader.locator('svg').first();
        await expect(upArrow).toHaveClass(/text-indigo-600/);
        
        // Go back to first page
        const prevPageButton = page.getByRole('button', { name: 'Předchozí' }).first();
        await prevPageButton.click();
        await page.waitForTimeout(500);
        
        // Verify the first product code is the same as before
        const firstRowProductCodeAfter = await page.locator('tbody tr:first-child td:first-child').textContent();
        expect(firstRowProductCodeAfter).toBe(firstRowProductCode);
      }
    } else {
      // If no pagination buttons, just verify sorting is maintained
      const productCodeHeader = page.locator('th').filter({ hasText: 'Kód produktu' });
      const upArrow = productCodeHeader.locator('svg').first();
      await expect(upArrow).toHaveClass(/text-indigo-600/);
    }
  });

  test('should show visual sorting indicators', async ({ page }) => {
    // Test all sortable headers have sorting indicators
    const sortableHeaders = [
      'Kód produktu',
      'Název produktu', 
      'Cena bez DPH',
      'Nákupní cena',
      'Materiál',
      'Výroba průměr',
      'Marže %'
    ];
    
    for (const headerText of sortableHeaders) {
      const header = page.locator('th').filter({ hasText: headerText });
      
      // Check that header has cursor pointer
      await expect(header).toHaveClass(/cursor-pointer/);
      
      // Check that header has both up and down arrow icons
      const upArrow = header.locator('svg').first();
      const downArrow = header.locator('svg').nth(1);
      
      await expect(upArrow).toBeVisible();
      await expect(downArrow).toBeVisible();
      
      // Click the header and verify one arrow becomes active
      await header.click();
      await page.waitForTimeout(300);
      
      const activeArrows = await header.locator('svg.text-indigo-600').count();
      expect(activeArrows).toBe(1);
    }
  });
});