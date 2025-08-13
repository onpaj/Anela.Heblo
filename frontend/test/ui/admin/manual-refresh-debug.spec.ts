import { test, expect } from '@playwright/test';

test.describe('Manual Refresh Debug', () => {
  
  test('should debug dashboard page structure', async ({ page }) => {
    // Navigate to dashboard (root path)
    await page.goto('http://localhost:3001/');
    
    // Wait for page to load
    await page.waitForLoadState('networkidle');
    
    // Take screenshot
    await page.screenshot({ path: 'dashboard-debug.png', fullPage: true });
    
    // Debug: Print all visible text
    const bodyText = await page.textContent('body');
    console.log('=== BODY TEXT (first 1000 chars) ===');
    console.log(bodyText?.substring(0, 1000));
    
    // Debug: Check if tabs exist
    console.log('=== TAB ELEMENTS ===');
    const tabs = await page.locator('button[class*="border-b-2"]').all();
    for (let i = 0; i < tabs.length; i++) {
      const text = await tabs[i].textContent();
      console.log(`Tab ${i}: "${text}"`);
    }
    
    // Debug: Look for "Manuální" text specifically
    const manualElements = await page.locator('*:has-text("Manuální")').all();
    console.log(`=== FOUND ${manualElements.length} ELEMENTS WITH "Manuální" ===`);
    for (let i = 0; i < manualElements.length; i++) {
      const text = await manualElements[i].textContent();
      const tag = await manualElements[i].evaluate(el => el.tagName);
      const visible = await manualElements[i].isVisible();
      console.log(`Element ${i}: ${tag}, visible: ${visible}, text: "${text}"`);
    }
    
    // Debug: Check all buttons
    console.log('=== ALL BUTTONS ===');
    const allButtons = await page.locator('button').all();
    for (let i = 0; i < Math.min(10, allButtons.length); i++) {
      const text = await allButtons[i].textContent();
      const visible = await allButtons[i].isVisible();
      console.log(`Button ${i}: visible: ${visible}, text: "${text}"`);
    }
    
    // Verify basic page elements exist
    await expect(page.locator('text=Administrační dashboard')).toBeVisible();
  });

});