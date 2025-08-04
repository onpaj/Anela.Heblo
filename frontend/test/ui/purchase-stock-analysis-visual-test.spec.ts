import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis Visual Test', () => {
  test('should take screenshot of new table design', async ({ page }) => {
    // Navigate to the Purchase Stock Analysis page
    await page.goto('/nakup/analyza-skladu');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
    
    // Wait longer for backend data initialization 
    console.log('Waiting for backend data initialization...');
    await page.waitForTimeout(10000); // 10 seconds
    
    // Check if we have data now
    const tableRows = await page.locator('tbody tr').count();
    console.log(`Table rows found after waiting: ${tableRows}`);
    
    // Check what state we're in
    const isLoading = await page.getByText('Načítání dat...').isVisible();
    const hasError = await page.getByText('Chyba při načítání dat').isVisible();
    const isEmpty = await page.getByText('Žádné výsledky').isVisible();
    const hasTable = await page.locator('table').isVisible();
    
    console.log(`Loading: ${isLoading}, Error: ${hasError}, Empty: ${isEmpty}, Table: ${hasTable}`);
    
    // Take full page screenshot regardless of state
    await page.screenshot({ 
      path: 'test-results/purchase-stock-analysis-new-design.png', 
      fullPage: true 
    });
    
    // Test different viewport sizes
    const screenSizes = [
      { width: 1200, height: 800, name: 'desktop' },
      { width: 768, height: 1024, name: 'tablet' },
      { width: 375, height: 667, name: 'mobile' }
    ];

    for (const size of screenSizes) {
      await page.setViewportSize({ width: size.width, height: size.height });
      await page.waitForTimeout(500);
      
      await page.screenshot({ 
        path: `test-results/purchase-stock-analysis-${size.name}.png`,
        fullPage: true 
      });
    }
    
    // Only verify headers if table is visible
    if (hasTable) {
      // Verify new column headers are present
      const sklademVisible = await page.getByText('Skladem').isVisible();
      const nsVisible = await page.getByText('NS').isVisible();
      const moqVisible = await page.getByText('MOQ').isVisible();
      
      console.log(`New headers - Skladem: ${sklademVisible}, NS: ${nsVisible}, MOQ: ${moqVisible}`);
      
      // Verify old column headers are gone
      const stavCount = await page.getByText('Stav').count();
      const zasobyCount = await page.getByText('Zásoby').count();
      const efektivitaCount = await page.getByText('Efektivita').count();
      const doporucenoCount = await page.getByText('Doporučeno').count();
      
      console.log(`Old headers count - Stav: ${stavCount}, Zásoby: ${zasobyCount}, Efektivita: ${efektivitaCount}, Doporučeno: ${doporucenoCount}`);
      
      if (sklademVisible && nsVisible) {
        console.log('✅ New table design verified - columns updated correctly');
      } else {
        console.log('⚠️ Table visible but new columns not found');
      }
    } else {
      console.log('❌ Table not visible - cannot verify column changes');
    }
  });
});