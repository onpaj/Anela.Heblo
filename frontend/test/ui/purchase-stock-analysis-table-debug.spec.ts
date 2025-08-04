import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis Table Debug', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the Purchase Stock Analysis page
    await page.goto('/nakup/analyza-skladu');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
  });

  test('should debug table overflow issues with data', async ({ page }) => {
    // Wait for potential data load
    await page.waitForTimeout(3000);
    
    console.log('\n=== TABLE OVERFLOW DEBUG ===');
    
    // Check if there's data in the table
    const tableBody = page.locator('tbody');
    const rows = await tableBody.locator('tr').count();
    console.log(`Table rows found: ${rows}`);
    
    if (rows > 0) {
      console.log('✅ Table has data - checking overflow');
      
      // Get table container
      const tableContainer = page.locator('.overflow-auto').first();
      const containerBounds = await tableContainer.boundingBox();
      console.log('Table container bounds:', containerBounds);
      
      // Get table element
      const table = page.locator('table').first();
      const tableBounds = await table.boundingBox();
      console.log('Table bounds:', tableBounds);
      
      if (containerBounds && tableBounds) {
        const hasHorizontalScroll = tableBounds.width > containerBounds.width;
        console.log(`Table width: ${tableBounds.width}px`);
        console.log(`Container width: ${containerBounds.width}px`);
        console.log(`Has horizontal scroll: ${hasHorizontalScroll}`);
        
        // Check at different viewport sizes
        const screenSizes = [
          { width: 1200, height: 800, name: 'Desktop' },
          { width: 768, height: 1024, name: 'Tablet' },
          { width: 375, height: 667, name: 'Mobile' }
        ];

        for (const size of screenSizes) {
          await page.setViewportSize({ width: size.width, height: size.height });
          await page.waitForTimeout(500);
          
          console.log(`\n--- ${size.name} (${size.width}x${size.height}) ---`);
          
          // Check body scroll
          const bodyScrollWidth = await page.evaluate(() => document.body.scrollWidth);
          const bodyClientWidth = await page.evaluate(() => document.body.clientWidth);
          console.log(`Body scroll width: ${bodyScrollWidth}px`);
          console.log(`Body client width: ${bodyClientWidth}px`);
          console.log(`Body has horizontal overflow: ${bodyScrollWidth > bodyClientWidth}`);
          
          // Check table container
          const updatedContainerBounds = await tableContainer.boundingBox();
          const updatedTableBounds = await table.boundingBox();
          
          if (updatedContainerBounds && updatedTableBounds) {
            const tableOverflow = updatedTableBounds.width > updatedContainerBounds.width;
            console.log(`Table container width: ${updatedContainerBounds.width}px`);
            console.log(`Table width: ${updatedTableBounds.width}px`);
            console.log(`Table overflows container: ${tableOverflow}`);
            
            // Count visible columns
            const visibleHeaders = await page.locator('thead th:not(.hidden)').count();
            console.log(`Visible columns: ${visibleHeaders}`);
          }
          
          // Take screenshot for this size
          await page.screenshot({ path: `debug-table-${size.name.toLowerCase()}.png` });
        }
      }
    } else {
      console.log('❌ No table data found - cannot test overflow');
      
      // Check if we're in loading or error state
      const loadingElement = page.getByText('Načítání dat...');
      const errorElement = page.getByText('Chyba při načítání dat');
      const emptyElement = page.getByText('Žádné výsledky');
      
      const isLoading = await loadingElement.isVisible();
      const hasError = await errorElement.isVisible();
      const isEmpty = await emptyElement.isVisible();
      
      console.log(`Loading state: ${isLoading}`);
      console.log(`Error state: ${hasError}`);
      console.log(`Empty state: ${isEmpty}`);
    }
  });

  test('should verify responsive column visibility', async ({ page }) => {
    // Wait for potential data load
    await page.waitForTimeout(2000);
    
    const screenSizes = [
      { width: 1200, height: 800, name: 'Desktop', expectedCols: 9 },
      { width: 768, height: 1024, name: 'Tablet', expectedCols: 7 }, // Hidden: lg:table-cell columns
      { width: 375, height: 667, name: 'Mobile', expectedCols: 4 }   // Hidden: md:table-cell columns
    ];

    for (const size of screenSizes) {
      await page.setViewportSize({ width: size.width, height: size.height });
      await page.waitForTimeout(500);
      
      console.log(`\n--- Column Visibility Test: ${size.name} ---`);
      
      // Count visible header columns
      const visibleHeaders = await page.locator('thead th:not(.hidden)').count();
      console.log(`Expected columns: ${size.expectedCols}`);
      console.log(`Visible columns: ${visibleHeaders}`);
      
      // Verify specific responsive behavior
      if (size.width < 768) { // Mobile
        // These should be hidden on mobile (md:table-cell)
        const minOptHeader = page.locator('th').filter({ hasText: 'Min/Opt' });
        const recommendedHeader = page.locator('th').filter({ hasText: 'Doporučeno' });
        
        const minOptVisible = await minOptHeader.isVisible();
        const recommendedVisible = await recommendedHeader.isVisible();
        
        console.log(`Min/Opt column visible (should be false): ${minOptVisible}`);
        console.log(`Recommended column visible (should be false): ${recommendedVisible}`);
      }
      
      if (size.width < 1024) { // Tablet and mobile
        // These should be hidden on tablet and below (lg:table-cell)
        const consumptionHeader = page.locator('th').filter({ hasText: 'Spotřeba' });
        const lastPurchaseHeader = page.locator('th').filter({ hasText: 'Poslední nákup' });
        
        const consumptionVisible = await consumptionHeader.isVisible();
        const lastPurchaseVisible = await lastPurchaseHeader.isVisible();
        
        console.log(`Consumption column visible (should be false): ${consumptionVisible}`);
        console.log(`Last Purchase column visible (should be false): ${lastPurchaseVisible}`);
      }
    }
  });
});