import { test, expect } from '@playwright/test';

test.describe('Journal Product Autocomplete Simple', () => {
  test('should allow manual product entry and removal', async ({ page }) => {
    await page.goto('http://localhost:3001');
    await page.waitForLoadState('networkidle');
    
    // Navigate to Journal
    await page.click('text=Deník');
    
    // Open new entry form
    await page.click('button:has-text("Nový záznam")');
    
    // Fill basic form fields
    await page.fill('input[placeholder="Zadejte název záznamu"]', 'Test Autocomplete');
    await page.fill('textarea[placeholder="Zadejte obsah záznamu..."]', 'Test obsahu s produkty');
    
    // Find product input field
    const productInput = page.locator('input[placeholder*="název nebo kód produktu"]');
    await expect(productInput).toBeVisible();
    
    // Add first product manually
    await productInput.fill('TEST-001');
    await productInput.press('Enter');
    
    // Check if product was added
    await expect(page.locator('.inline-flex:has-text("TEST-001")')).toBeVisible();
    
    // Add second product
    await productInput.fill('TEST-002');
    await productInput.press('Enter');
    
    // Check both products are visible
    await expect(page.locator('.inline-flex:has-text("TEST-001")')).toBeVisible();
    await expect(page.locator('.inline-flex:has-text("TEST-002")')).toBeVisible();
    
    // Remove first product by clicking X button
    await page.click('.inline-flex:has-text("TEST-001") button');
    
    // First product should be gone, second should remain
    await expect(page.locator('.inline-flex:has-text("TEST-001")')).not.toBeVisible();
    await expect(page.locator('.inline-flex:has-text("TEST-002")')).toBeVisible();
    
    console.log('✅ Manual product entry and removal works correctly');
  });

  test('should save journal entry with products', async ({ page }) => {
    await page.goto('http://localhost:3001');
    await page.waitForLoadState('networkidle');
    
    // Navigate to Journal
    await page.click('text=Deník');
    
    // Open new entry form
    await page.click('button:has-text("Nový záznam")');
    
    // Fill form completely
    await page.fill('input[placeholder="Zadejte název záznamu"]', 'Test Uložení');
    await page.fill('textarea[placeholder="Zadejte obsah záznamu..."]', 'Test uložení s produkty');
    
    // Add product
    const productInput = page.locator('input[placeholder*="název nebo kód produktu"]');
    await productInput.fill('SAVE-TEST-001');
    await productInput.press('Enter');
    
    // Verify product is added
    await expect(page.locator('.inline-flex:has-text("SAVE-TEST-001")')).toBeVisible();
    
    // Save the form
    await page.click('button:has-text("Vytvořit záznam")');
    
    // Should return to journal list
    await expect(page.locator('h1:has-text("Deník")')).toBeVisible();
    
    // Check if entry appears in list (might take a moment to load)
    await page.waitForTimeout(1000);
    // Use more specific selector for the title column
    await expect(page.locator('td .max-w-48:has-text("Test Uložení")')).toBeVisible();
    
    console.log('✅ Journal entry with products saved successfully');
  });
});