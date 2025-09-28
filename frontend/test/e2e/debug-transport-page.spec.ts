import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToTransportBoxes } from './helpers/e2e-auth-helper';

test.describe('Debug Transport Page', () => {
  
  test.beforeEach(async ({ page }) => {
    await createE2EAuthSession(page);
  });

  test('debug transport boxes navigation', async ({ page }) => {
    // Test the updated navigation logic
    await navigateToTransportBoxes(page);
    
    // Wait for page to load
    await page.waitForTimeout(3000);
    
    // Get current URL
    const currentUrl = page.url();
    console.log('Final URL:', currentUrl);
    
    // Get page title
    const title = await page.title();
    console.log('Page title:', title);
    
    // Get all h1 elements
    const h1Elements = await page.locator('h1').allTextContents();
    console.log('H1 elements:', h1Elements);
    
    // Get all h2 elements
    const h2Elements = await page.locator('h2').allTextContents();
    console.log('H2 elements:', h2Elements);
    
    // Get all h3 elements
    const h3Elements = await page.locator('h3').allTextContents();
    console.log('H3 elements:', h3Elements);
    
    // Get all button texts
    const buttons = await page.locator('button').allTextContents();
    console.log('Buttons found:', buttons);
    
    // Look specifically for transport box related elements
    const transportElements = await page.locator('[data-testid*="transport"], .transport, [class*="transport"]').count();
    console.log('Transport-related elements:', transportElements);
    
    // Look for "Otevřít nový box" button
    const createBoxButton = await page.locator('button').filter({ hasText: /Otevřít nový box/ }).count();
    console.log('Create box buttons found:', createBoxButton);
    
    // Look for any create/add buttons
    const createButtons = await page.locator('button').filter({ hasText: /Create|Vytvořit|Nový|Add|Přidat/ }).count();
    console.log('General create buttons found:', createButtons);
    
    // Get page text content (first 1000 chars)
    const pageText = await page.textContent('body');
    console.log('Page text (first 1000 chars):', pageText?.substring(0, 1000));
    
    // Take a screenshot for debugging
    await page.screenshot({ path: 'debug-transport-navigation.png', fullPage: true });
    
    // The test should pass - we're just gathering debug info
    expect(true).toBe(true);
  });
});