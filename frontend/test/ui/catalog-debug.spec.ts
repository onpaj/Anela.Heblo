import { test, expect } from '@playwright/test';

test.describe('Catalog Debug', () => {
  test('should check what is on catalog page', async ({ page }) => {
    // Listen for console logs and errors
    page.on('console', (msg) => {
      console.log(`BROWSER CONSOLE [${msg.type()}]:`, msg.text());
    });
    
    page.on('pageerror', (error) => {
      console.log('BROWSER ERROR:', error.message);
    });
    
    await page.goto('/catalog');
    
    // Wait for network and JS to load
    await page.waitForLoadState('networkidle');
    
    // Check if root exists at all
    const rootExists = await page.locator('#root').count();
    console.log('Root div exists:', rootExists > 0);
    
    // Try to wait for React to render, but don't fail if it doesn't
    try {
      await page.waitForFunction(() => {
        const root = document.querySelector('#root');
        return root && root.innerHTML.trim() !== '';
      }, { timeout: 5000 });
      console.log('React content loaded successfully');
    } catch (e) {
      console.log('React content did not load within timeout');
    }
    
    await page.waitForTimeout(2000);
    
    // Take screenshot to see what's actually on the page
    await page.screenshot({ path: 'test-results/catalog-debug.png', fullPage: true });
    
    // Check if page loaded
    const title = await page.title();
    console.log('Page title:', title);
    
    // Check what's in the root div
    const rootContent = await page.locator('#root').textContent();
    console.log('Root content (first 500 chars):', rootContent?.substring(0, 500));
    
    // Check what h1 elements are present
    const h1Elements = await page.locator('h1').allTextContents();
    console.log('H1 elements:', h1Elements);
    
    // Check what's in the body
    const bodyText = await page.locator('body').textContent();
    console.log('Body text (first 500 chars):', bodyText?.substring(0, 500));
    
    // Check for any error messages
    const errorElements = await page.locator('text=/error/i').allTextContents();
    console.log('Error elements:', errorElements);
    
    // Check for loading states
    const loadingElements = await page.locator('text=/loading/i').allTextContents();
    console.log('Loading elements:', loadingElements);

    // Don't fail the test, just log what we found
    console.log('Test completed - check logs and screenshot for details');
  });
});