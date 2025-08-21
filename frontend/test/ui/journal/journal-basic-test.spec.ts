import { test, expect } from '@playwright/test';

test.describe('Journal Basic Functionality', () => {
  test('should load journal page without errors', async ({ page }) => {
    // Go to main page
    await page.goto('http://localhost:3001');
    
    // Wait for page to load completely
    await page.waitForLoadState('networkidle');
    
    // Check if we can see the main navigation
    await expect(page.locator('text=Anela Heblo')).toBeVisible();
    
    // Navigate to Journal via sidebar
    await page.click('text=Deník');
    
    // Check if journal page loads
    await expect(page.locator('h1:has-text("Deník")')).toBeVisible();
    
    // Check for "Nový záznam" button
    await expect(page.locator('button:has-text("Nový záznam")')).toBeVisible();
  });

  test('should open new journal entry form', async ({ page }) => {
    await page.goto('http://localhost:3001');
    await page.waitForLoadState('networkidle');
    
    // Navigate to Journal
    await page.click('text=Deník');
    
    // Click "Nový záznam" button
    await page.click('button:has-text("Nový záznam")');
    
    // Check if form opens
    await expect(page.locator('h1:has-text("Nový záznam"), h2:has-text("Nový záznam"), h3:has-text("Nový záznam")')).toBeVisible();
    
    // Check for form fields
    await expect(page.locator('input[placeholder="Zadejte název záznamu"]')).toBeVisible();
    await expect(page.locator('textarea[placeholder="Zadejte obsah záznamu..."]')).toBeVisible();
    
    // Check for product autocomplete field
    await expect(page.locator('input[placeholder*="název nebo kód produktu"]')).toBeVisible();
  });

  test('should be able to cancel form creation', async ({ page }) => {
    await page.goto('http://localhost:3001');
    await page.waitForLoadState('networkidle');
    
    // Navigate to Journal
    await page.click('text=Deník');
    
    // Open new entry form
    await page.click('button:has-text("Nový záznam")');
    
    // Cancel the form
    await page.click('button:has-text("Zrušit")');
    
    // Should return to journal list
    await expect(page.locator('h1:has-text("Deník")')).toBeVisible();
  });
});