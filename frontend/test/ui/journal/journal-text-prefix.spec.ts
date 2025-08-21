import { test, expect } from '@playwright/test';

test.describe('Journal Product Text Prefix Input', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3001/journal/new');
    await page.waitForLoadState('networkidle');
  });

  test('should show autocomplete mode by default', async ({ page }) => {
    // Check that autocomplete button is active by default
    const autocompleteButton = page.getByRole('button', { name: /autocomplete/i });
    const textButton = page.getByRole('button', { name: /text/i });
    
    await expect(autocompleteButton).toHaveClass(/bg-indigo-100/);
    await expect(textButton).toHaveClass(/bg-gray-100/);
    
    // Check placeholder text for autocomplete mode
    const productInput = page.getByPlaceholder(/začněte psát název nebo kód produktu/i);
    await expect(productInput).toBeVisible();
  });

  test('should switch to text input mode', async ({ page }) => {
    // Click text mode button
    const textButton = page.getByRole('button', { name: /text/i });
    await textButton.click();
    
    // Check that text button is now active
    await expect(textButton).toHaveClass(/bg-indigo-100/);
    
    // Check placeholder changed to text mode
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await expect(productInput).toBeVisible();
    
    // Check help text appears
    await expect(page.getByText(/tip: zadejte produktový prefix/i)).toBeVisible();
  });

  test('should add product prefix via Enter key in text mode', async ({ page }) => {
    // Switch to text mode
    await page.getByRole('button', { name: /text/i }).click();
    
    // Fill basic info first
    await page.getByLabel(/název záznamu/i).fill('Test Entry');
    await page.getByLabel(/obsah záznamu/i).fill('Test content');
    
    // Add product prefix via Enter
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await productInput.fill('COSM-001');
    await productInput.press('Enter');
    
    // Check that product was added and input cleared
    await expect(page.getByText('COSM-001')).toBeVisible();
    await expect(productInput).toHaveValue('');
  });

  test('should add product prefix via blur event in text mode', async ({ page }) => {
    // Switch to text mode
    await page.getByRole('button', { name: /text/i }).click();
    
    // Fill basic info first
    await page.getByLabel(/název záznamu/i).fill('Test Entry');
    await page.getByLabel(/obsah záznamu/i).fill('Test content');
    
    // Add product prefix via blur (clicking elsewhere)
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await productInput.fill('BEAUTY-002');
    await page.getByLabel(/název záznamu/i).click(); // Click elsewhere to blur
    
    // Check that product was added
    await expect(page.getByText('BEAUTY-002')).toBeVisible();
    await expect(productInput).toHaveValue('');
  });

  test('should add multiple products in text mode', async ({ page }) => {
    // Switch to text mode
    await page.getByRole('button', { name: /text/i }).click();
    
    // Fill basic info first
    await page.getByLabel(/název záznamu/i).fill('Test Entry');
    await page.getByLabel(/obsah záznamu/i).fill('Test content');
    
    // Add first product
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await productInput.fill('COSM-001');
    await productInput.press('Enter');
    
    // Add second product
    await productInput.fill('BEAUTY-002');
    await productInput.press('Enter');
    
    // Check both products are visible
    await expect(page.getByText('COSM-001')).toBeVisible();
    await expect(page.getByText('BEAUTY-002')).toBeVisible();
  });

  test('should not show autocomplete dropdown in text mode', async ({ page }) => {
    // Switch to text mode
    await page.getByRole('button', { name: /text/i }).click();
    
    // Type something that would trigger autocomplete
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await productInput.fill('test product');
    
    // Wait a bit for any potential autocomplete to show
    await page.waitForTimeout(500);
    
    // Check no autocomplete dropdown is visible
    const autocompleteDropdown = page.locator('.absolute.z-10.mt-1.w-full.bg-white.shadow-lg');
    await expect(autocompleteDropdown).not.toBeVisible();
  });

  test('should remove products with X button', async ({ page }) => {
    // Switch to text mode
    await page.getByRole('button', { name: /text/i }).click();
    
    // Fill basic info first
    await page.getByLabel(/název záznamu/i).fill('Test Entry');
    await page.getByLabel(/obsah záznamu/i).fill('Test content');
    
    // Add product
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await productInput.fill('COSM-001');
    await productInput.press('Enter');
    
    // Verify product is added
    await expect(page.getByText('COSM-001')).toBeVisible();
    
    // Remove product using X button
    const removeButton = page.locator('span:has-text("COSM-001")').getByRole('button');
    await removeButton.click();
    
    // Verify product is removed
    await expect(page.getByText('COSM-001')).not.toBeVisible();
  });

  test('should prevent duplicate products', async ({ page }) => {
    // Switch to text mode
    await page.getByRole('button', { name: /text/i }).click();
    
    // Fill basic info first
    await page.getByLabel(/název záznamu/i).fill('Test Entry');
    await page.getByLabel(/obsah záznamu/i).fill('Test content');
    
    // Add same product twice
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await productInput.fill('COSM-001');
    await productInput.press('Enter');
    
    await productInput.fill('COSM-001');
    await productInput.press('Enter');
    
    // Should only show one instance
    const productElements = page.locator('span:has-text("COSM-001")');
    await expect(productElements).toHaveCount(1);
  });

  test('should switch between modes preserving existing products', async ({ page }) => {
    // Start in autocomplete mode, add product (if possible)
    await page.getByLabel(/název záznamu/i).fill('Test Entry');
    await page.getByLabel(/obsah záznamu/i).fill('Test content');
    
    // Switch to text mode
    await page.getByRole('button', { name: /text/i }).click();
    
    // Add product in text mode
    const productInput = page.getByPlaceholder(/zadejte produktový prefix/i);
    await productInput.fill('TEXT-001');
    await productInput.press('Enter');
    
    await expect(page.getByText('TEXT-001')).toBeVisible();
    
    // Switch back to autocomplete mode
    await page.getByRole('button', { name: /autocomplete/i }).click();
    
    // Product should still be there
    await expect(page.getByText('TEXT-001')).toBeVisible();
    
    // Placeholder should change back
    await expect(page.getByPlaceholder(/začněte psát název nebo kód produktu/i)).toBeVisible();
  });
});