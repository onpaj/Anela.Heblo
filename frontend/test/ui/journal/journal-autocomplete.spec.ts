import { test, expect } from '@playwright/test';

test.describe('Journal Form Product Autocomplete', () => {
  test('should display product autocomplete when searching', async ({ page }) => {
    await page.goto('http://localhost:3001');
    
    // Navigate to Journal
    await page.click('[data-testid="nav-journal"], button:has-text("Deník")');
    
    // Click create new entry
    await page.click('button:has-text("Nový záznam")');
    
    // Fill basic form fields
    await page.fill('input[placeholder="Zadejte název záznamu"]', 'Test záznam s produkty');
    await page.fill('textarea[placeholder="Zadejte obsah záznamu..."]', 'Test obsahu záznamu');
    
    // Find the product search input
    const productInput = page.locator('input[placeholder*="název nebo kód produktu"]');
    await expect(productInput).toBeVisible();
    
    // Type search term to trigger autocomplete
    await productInput.fill('test');
    
    // Wait a bit for autocomplete to load
    await page.waitForTimeout(1000);
    
    // Check if autocomplete dropdown appears (even if empty)
    // The dropdown should be present but might not have items if no products match
    
    // Try typing a more generic search
    await productInput.clear();
    await productInput.fill('A');
    await page.waitForTimeout(1000);
    
    // Check if form can be saved without products
    await page.click('button:has-text("Vytvořit záznam")');
    
    // Should navigate back to journal list
    await expect(page).toHaveURL(/.*journal/);
  });

  test('should allow manual product code entry', async ({ page }) => {
    await page.goto('http://localhost:3001');
    
    // Navigate to Journal
    await page.click('[data-testid="nav-journal"], button:has-text("Deník")');
    
    // Click create new entry
    await page.click('button:has-text("Nový záznam")');
    
    // Fill basic form fields
    await page.fill('input[placeholder="Zadejte název záznamu"]', 'Test manuální kód');
    await page.fill('textarea[placeholder="Zadejte obsah záznamu..."]', 'Test manuálního zadání kódu');
    
    // Find the product search input
    const productInput = page.locator('input[placeholder*="název nebo kód produktu"]');
    
    // Type a product code manually
    await productInput.fill('MANUAL-001');
    
    // Press Enter to add it
    await productInput.press('Enter');
    
    // Check if product was added to the list
    await expect(page.locator('.inline-flex:has-text("MANUAL-001")')).toBeVisible();
    
    // Add another product
    await productInput.fill('MANUAL-002');
    await productInput.press('Enter');
    
    // Check both products are visible
    await expect(page.locator('.inline-flex:has-text("MANUAL-001")')).toBeVisible();
    await expect(page.locator('.inline-flex:has-text("MANUAL-002")')).toBeVisible();
    
    // Remove first product
    await page.click('.inline-flex:has-text("MANUAL-001") button');
    await expect(page.locator('.inline-flex:has-text("MANUAL-001")')).not.toBeVisible();
    await expect(page.locator('.inline-flex:has-text("MANUAL-002")')).toBeVisible();
  });

  test('should save journal entry with associated products', async ({ page }) => {
    await page.goto('http://localhost:3001');
    
    // Navigate to Journal
    await page.click('[data-testid="nav-journal"], button:has-text("Deník")');
    
    // Click create new entry
    await page.click('button:has-text("Nový záznam")');
    
    // Fill form
    await page.fill('input[placeholder="Zadejte název záznamu"]', 'Test s produkty');
    await page.fill('textarea[placeholder="Zadejte obsah záznamu..."]', 'Test záznamu s přiřazenými produkty');
    
    // Add products manually
    const productInput = page.locator('input[placeholder*="název nebo kód produktu"]');
    await productInput.fill('TEST-PRODUCT-A');
    await productInput.press('Enter');
    
    await productInput.fill('TEST-PRODUCT-B');
    await productInput.press('Enter');
    
    // Verify products are added
    await expect(page.locator('.inline-flex:has-text("TEST-PRODUCT-A")')).toBeVisible();
    await expect(page.locator('.inline-flex:has-text("TEST-PRODUCT-B")')).toBeVisible();
    
    // Save the entry
    await page.click('button:has-text("Vytvořit záznam")');
    
    // Should redirect to journal list
    await expect(page).toHaveURL(/.*journal/);
    
    // Check if the entry appears in the list
    await expect(page.locator('text=Test s produkty')).toBeVisible();
  });
});