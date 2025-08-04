import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis - Celkem Filter Color Bar Debug', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3001/nakup/analyza-skladu');
    await page.waitForLoadState('networkidle');
  });

  test('should display color bars when Celkem filter is selected', async ({ page }) => {
    // Wait for data to load
    await expect(page.locator('[data-testid="stock-analysis-table"], table')).toBeVisible({ timeout: 10000 });
    
    // Click on "Celkem" filter to ensure it's selected
    const celkemButton = page.locator('button').filter({ hasText: /Celkem:/ });
    await expect(celkemButton).toBeVisible();
    await celkemButton.click();
    
    // Wait a moment for any state updates
    await page.waitForTimeout(1000);
    
    // Check if table rows exist
    const tableRows = page.locator('tbody tr');
    const rowCount = await tableRows.count();
    console.log(`Found ${rowCount} table rows`);
    
    if (rowCount > 0) {
      // Look for color strips in the first few rows
      for (let i = 0; i < Math.min(rowCount, 5); i++) {
        const row = tableRows.nth(i);
        
        // Check if row is visible
        await expect(row).toBeVisible();
        
        // Look for color strip element - check different possible selectors
        const colorStripSelectors = [
          '.w-1.h-8', // The specific classes from the code
          '[class*="bg-red-500"]',
          '[class*="bg-amber-500"]', 
          '[class*="bg-emerald-500"]',
          '[class*="bg-blue-500"]',
          '[class*="bg-gray-400"]',
          'div[class*="w-1"][class*="h-8"]' // Generic color strip
        ];
        
        let colorStripFound = false;
        for (const selector of colorStripSelectors) {
          const colorStrip = row.locator(selector);
          if (await colorStrip.count() > 0) {
            console.log(`Row ${i}: Found color strip with selector: ${selector}`);
            const classes = await colorStrip.getAttribute('class');
            console.log(`Row ${i}: Color strip classes: ${classes}`);
            colorStripFound = true;
            break;
          }
        }
        
        if (!colorStripFound) {
          console.log(`Row ${i}: No color strip found`);
          
          // Debug: Print the HTML structure of the first cell
          const firstCell = row.locator('td').first();
          const innerHTML = await firstCell.innerHTML();
          console.log(`Row ${i}: First cell HTML:`, innerHTML);
        }
      }
      
      // Specific test: look for ANY color strip in the table
      const anyColorStrip = page.locator('tbody tr td div.w-1.h-8');
      const colorStripCount = await anyColorStrip.count();
      console.log(`Total color strips found: ${colorStripCount}`);
      
      if (colorStripCount === 0) {
        // Take a screenshot for debugging
        await page.screenshot({ 
          path: 'debug-celkem-no-color-strips.png',
          fullPage: true 
        });
        
        // Also check the current filter state
        const celkemButtonClass = await celkemButton.getAttribute('class');
        console.log('Celkem button classes:', celkemButtonClass);
        
        // Check if it has the selected state classes
        const isSelected = celkemButtonClass?.includes('ring-2') || celkemButtonClass?.includes('bg-gray-100');
        console.log('Celkem button appears selected:', isSelected);
      }
      
      // The test should pass if we find at least one color strip when Celkem is selected
      expect(colorStripCount).toBeGreaterThan(0);
      
    } else {
      console.log('No table rows found - might be loading issue or no data');
      await page.screenshot({ path: 'debug-no-table-rows.png', fullPage: true });
    }
  });

  test('should hide color bars when specific status filter is selected', async ({ page }) => {
    // Wait for data to load
    await expect(page.locator('[data-testid="stock-analysis-table"], table')).toBeVisible({ timeout: 10000 });
    
    // First ensure we're on "Celkem" and have color bars
    const celkemButton = page.locator('button').filter({ hasText: /Celkem:/ });
    await celkemButton.click();
    await page.waitForTimeout(500);
    
    // Then click on "Kritické" filter
    const kritickeButton = page.locator('button').filter({ hasText: /Kritické:/ });
    await expect(kritickeButton).toBeVisible();
    await kritickeButton.click();
    await page.waitForTimeout(1000);
    
    // Now color strips should be hidden
    const colorStrips = page.locator('tbody tr td div.w-1.h-8');
    const colorStripCount = await colorStrips.count();
    console.log(`Color strips after selecting Kritické: ${colorStripCount}`);
    
    // Should be 0 when specific filter is selected
    expect(colorStripCount).toBe(0);
  });

  test('debug filter state and component behavior', async ({ page }) => {
    // Wait for data to load
    await expect(page.locator('table')).toBeVisible({ timeout: 10000 });
    
    // Get all filter buttons
    const filterButtons = page.locator('button').filter({ hasText: /Celkem:|Kritické:|Nízké:|Optimální:|Přeskladněno:|Nezkonfigurováno:/ });
    const buttonCount = await filterButtons.count();
    console.log(`Found ${buttonCount} filter buttons`);
    
    for (let i = 0; i < buttonCount; i++) {
      const button = filterButtons.nth(i);
      const buttonText = await button.textContent();
      const buttonClass = await button.getAttribute('class');
      const isSelected = buttonClass?.includes('ring-2');
      console.log(`Button ${i}: "${buttonText}" - Selected: ${isSelected}`);
    }
    
    // Test clicking each filter and checking color strip behavior
    const celkemButton = page.locator('button').filter({ hasText: /Celkem:/ });
    await celkemButton.click();
    await page.waitForTimeout(500);
    
    let colorStripCount = await page.locator('tbody tr td div.w-1.h-8').count();
    console.log(`After Celkem click: ${colorStripCount} color strips`);
    
    // Debug: Add console output to component by evaluating JavaScript
    await page.evaluate(() => {
      console.log('=== DEBUG INFO FROM BROWSER ===');
      // Try to access React component state if possible
      const rows = document.querySelectorAll('tbody tr');
      console.log(`Found ${rows.length} table rows in DOM`);
      
      rows.forEach((row, i) => {
        const firstCell = row.querySelector('td');
        if (firstCell) {
          console.log(`Row ${i} first cell HTML:`, firstCell.innerHTML);
        }
      });
    });
    
    // Take screenshot of current state
    await page.screenshot({ 
      path: 'debug-celkem-selected-state.png',
      fullPage: true 
    });
  });
});