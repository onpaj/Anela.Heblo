import { test, expect } from '@playwright/test';

test.describe('Debug Navigation Structure', () => {
  test('should show current navigation structure', async ({ page }) => {
    await page.goto('http://localhost:3001/');
    
    // Take a screenshot to see the navigation
    await page.screenshot({ path: 'debug-navigation.png' });
    
    // Log the navigation structure
    const sidebar = page.locator('[data-testid="sidebar"], .w-64, .w-16').first();
    if (await sidebar.isVisible()) {
      console.log('Sidebar found');
      
      // Find all navigation items
      const navItems = await page.locator('nav a, nav button').allTextContents();
      console.log('Navigation items:', navItems);
      
      // Look for specific elements
      const dashboard = page.locator('text="Dashboard"');
      const produkty = page.locator('text="Produkty"');
      const nakup = page.locator('text="Nákup"');
      const katalog = page.locator('text="Katalog"');
      
      console.log('Dashboard visible:', await dashboard.isVisible());
      console.log('Produkty visible:', await produkty.isVisible());
      console.log('Nákup visible:', await nakup.isVisible());
      console.log('Katalog visible:', await katalog.isVisible());
      
      // Check if Produkty is expandable
      const produktyButton = page.locator('button:has-text("Produkty")');
      if (await produktyButton.isVisible()) {
        console.log('Produkty button found, clicking...');
        await produktyButton.click();
        await page.waitForTimeout(1000);
        
        const katalogAfterClick = page.locator('text="Katalog"');
        console.log('Katalog visible after clicking Produkty:', await katalogAfterClick.isVisible());
      }
    } else {
      console.log('Sidebar not found');
    }
  });
});