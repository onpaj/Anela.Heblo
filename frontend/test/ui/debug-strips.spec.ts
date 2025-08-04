import { test, expect } from '@playwright/test';

test.describe('Debug Strips Test', () => {
  test('take screenshot of strips', async ({ page }) => {
    await page.goto('http://localhost:3001/purchase/stock-analysis');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(3000);
    
    // Take screenshot
    await page.screenshot({ path: 'test-results/debug-strips-screenshot.png', fullPage: false });
    
    // Check if red debug strip is visible
    const debugStrips = page.locator('div.bg-red-500');
    const count = await debugStrips.count();
    console.log('Debug strips count:', count);
    
    expect(count).toBeGreaterThan(0);
  });
});