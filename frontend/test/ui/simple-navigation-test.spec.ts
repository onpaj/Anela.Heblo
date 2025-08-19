import { test, expect } from '@playwright/test';

test.describe('Simple Navigation Test', () => {
  test('should display navigation with correct structure', async ({ page }) => {
    await page.goto('http://localhost:3001/');
    
    // Wait for page to load
    await page.waitForSelector('text="Dashboard"', { timeout: 10000 });
    
    // Check that Dashboard is visible
    await expect(page.locator('text="Dashboard"')).toBeVisible();
    
    // Check that Produkty section is visible
    await expect(page.locator('text="Produkty"')).toBeVisible();
    
    // Check that Nákup section is visible
    await expect(page.locator('text="Nákup"')).toBeVisible();
    
    // Since both sections are expanded by default, check for submenu items
    await expect(page.locator('text="Katalog"')).toBeVisible();
    await expect(page.locator('text="Marže produktů"')).toBeVisible();
    await expect(page.locator('text="Nákupní objednávky"')).toBeVisible();
    await expect(page.locator('text="Analýza skladů"')).toBeVisible();
    
    console.log('All navigation items are visible as expected');
  });
  
  test('should navigate to catalog when Katalog link is clicked', async ({ page }) => {
    await page.goto('http://localhost:3001/');
    
    // Wait for navigation to load
    await page.waitForSelector('text="Katalog"', { timeout: 10000 });
    
    // Click on Katalog link
    await page.locator('a:has-text("Katalog")').click();
    
    // Verify navigation to catalog page
    await expect(page).toHaveURL('http://localhost:3001/catalog');
    
    console.log('Successfully navigated to catalog page');
  });
});