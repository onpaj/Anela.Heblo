import { test, expect } from '@playwright/test';

test.describe('Purchase Order Creation', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the application
    await page.goto('http://localhost:3001');
    
    // Wait for the app to load
    await expect(page.locator('body')).toBeVisible();
  });

  test('should create purchase order with lines successfully', async ({ page }) => {
    // Navigate to purchase orders section
    await page.click('[data-testid="nav-purchase-orders"]');
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');

    // Click "New Purchase Order" button
    await page.click('[data-testid="new-purchase-order-button"]');
    
    // Wait for form modal to open
    await expect(page.locator('[data-testid="purchase-order-form"]')).toBeVisible();
    await expect(page.locator('h2')).toContainText('Nová nákupní objednávka');

    // Fill basic information
    await page.fill('[data-testid="supplier-name-input"]', 'Test Supplier Playwright');
    await page.fill('[data-testid="order-date-input"]', '2024-08-02');
    await page.fill('[data-testid="expected-delivery-date-input"]', '2024-08-16');
    await page.fill('[data-testid="notes-input"]', 'Playwright test order with lines');

    // Add first line - material selection
    const firstMaterialInput = page.locator('[data-testid="material-autocomplete-0"]');
    await firstMaterialInput.click();
    await firstMaterialInput.fill('Test Material');
    
    // Wait for autocomplete dropdown and select first material
    await page.waitForSelector('[data-testid="material-option"]', { timeout: 5000 });
    await page.click('[data-testid="material-option"]:first-child');

    // Fill quantity and unit price for first line
    await page.fill('[data-testid="quantity-input-0"]', '10');
    await page.fill('[data-testid="unit-price-input-0"]', '25.50');
    await page.fill('[data-testid="line-notes-input-0"]', 'First test line');

    // Verify line total is calculated
    await expect(page.locator('[data-testid="line-total-0"]')).toContainText('255,00 Kč');

    // Add second line by selecting material in the auto-added empty row
    const secondMaterialInput = page.locator('[data-testid="material-autocomplete-1"]');
    await secondMaterialInput.click();
    await secondMaterialInput.fill('Another Material');
    
    // Select second material
    await page.waitForSelector('[data-testid="material-option"]', { timeout: 5000 });
    await page.click('[data-testid="material-option"]:first-child');

    // Fill quantity and unit price for second line
    await page.fill('[data-testid="quantity-input-1"]', '5');
    await page.fill('[data-testid="unit-price-input-1"]', '15.00');
    await page.fill('[data-testid="line-notes-input-1"]', 'Second test line');

    // Verify second line total
    await expect(page.locator('[data-testid="line-total-1"]')).toContainText('75,00 Kč');

    // Verify overall total
    await expect(page.locator('[data-testid="order-total"]')).toContainText('330,00 Kč');

    // Submit the form
    await page.click('[data-testid="submit-purchase-order-button"]');

    // Wait for success and form to close
    await expect(page.locator('[data-testid="purchase-order-form"]')).not.toBeVisible({ timeout: 10000 });

    // Verify we're back on the purchase orders list
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');

    // Verify the new order appears in the list
    await expect(page.locator('[data-testid="purchase-order-row"]').first()).toContainText('Test Supplier Playwright');
    await expect(page.locator('[data-testid="purchase-order-row"]').first()).toContainText('330,00 Kč');

    // Click on the order to view details
    await page.click('[data-testid="purchase-order-row"]:first-child [data-testid="view-order-button"]');

    // Wait for detail modal to open
    await expect(page.locator('[data-testid="purchase-order-detail"]')).toBeVisible();

    // Verify order details
    await expect(page.locator('[data-testid="detail-supplier-name"]')).toContainText('Test Supplier Playwright');
    await expect(page.locator('[data-testid="detail-order-total"]')).toContainText('330,00 Kč');
    await expect(page.locator('[data-testid="detail-notes"]')).toContainText('Playwright test order with lines');

    // **CRITICAL: Verify that lines are present and saved correctly**
    const lineRows = page.locator('[data-testid="order-line-row"]');
    await expect(lineRows).toHaveCount(2);

    // Verify first line details
    const firstLine = lineRows.nth(0);
    await expect(firstLine.locator('[data-testid="line-material-name"]')).toContainText('Test Material');
    await expect(firstLine.locator('[data-testid="line-quantity"]')).toContainText('10');
    await expect(firstLine.locator('[data-testid="line-unit-price"]')).toContainText('25,50');
    await expect(firstLine.locator('[data-testid="line-total"]')).toContainText('255,00');
    await expect(firstLine.locator('[data-testid="line-notes"]')).toContainText('First test line');

    // Verify second line details  
    const secondLine = lineRows.nth(1);
    await expect(secondLine.locator('[data-testid="line-material-name"]')).toContainText('Another Material');
    await expect(secondLine.locator('[data-testid="line-quantity"]')).toContainText('5');
    await expect(secondLine.locator('[data-testid="line-unit-price"]')).toContainText('15,00');
    await expect(secondLine.locator('[data-testid="line-total"]')).toContainText('75,00');
    await expect(secondLine.locator('[data-testid="line-notes"]')).toContainText('Second test line');
  });

  test('should validate that order without lines shows error', async ({ page }) => {
    // Navigate to purchase orders and open form
    await page.click('[data-testid="nav-purchase-orders"]');
    await page.click('[data-testid="new-purchase-order-button"]');
    
    // Fill only basic information, no lines
    await page.fill('[data-testid="supplier-name-input"]', 'Test Supplier No Lines');
    await page.fill('[data-testid="order-date-input"]', '2024-08-02');

    // Try to submit without adding any lines
    await page.click('[data-testid="submit-purchase-order-button"]');

    // Should show validation error
    await expect(page.locator('[data-testid="lines-error"]')).toContainText('Přidejte alespoň jednu položku objednávky');
    
    // Form should still be open
    await expect(page.locator('[data-testid="purchase-order-form"]')).toBeVisible();
  });

  test('should edit purchase order and modify lines', async ({ page }) => {
    // First create an order (reuse creation logic)
    await page.click('[data-testid="nav-purchase-orders"]');
    await page.click('[data-testid="new-purchase-order-button"]');
    
    await page.fill('[data-testid="supplier-name-input"]', 'Edit Test Supplier');
    await page.fill('[data-testid="order-date-input"]', '2024-08-02');
    
    // Add one line
    const materialInput = page.locator('[data-testid="material-autocomplete-0"]');
    await materialInput.click();
    await materialInput.fill('Edit Material');
    await page.waitForSelector('[data-testid="material-option"]', { timeout: 5000 });
    await page.click('[data-testid="material-option"]:first-child');
    await page.fill('[data-testid="quantity-input-0"]', '3');
    await page.fill('[data-testid="unit-price-input-0"]', '10.00');
    
    await page.click('[data-testid="submit-purchase-order-button"]');
    await expect(page.locator('[data-testid="purchase-order-form"]')).not.toBeVisible({ timeout: 10000 });

    // Now edit the order
    await page.click('[data-testid="purchase-order-row"]:first-child [data-testid="edit-order-button"]');
    await expect(page.locator('[data-testid="purchase-order-form"]')).toBeVisible();
    await expect(page.locator('h2')).toContainText('Upravit nákupní objednávku');

    // Modify existing line quantity
    await page.fill('[data-testid="quantity-input-0"]', '5');
    
    // Add second line
    const secondMaterialInput = page.locator('[data-testid="material-autocomplete-1"]');
    await secondMaterialInput.click();
    await secondMaterialInput.fill('Additional Material');
    await page.waitForSelector('[data-testid="material-option"]', { timeout: 5000 });
    await page.click('[data-testid="material-option"]:first-child');
    await page.fill('[data-testid="quantity-input-1"]', '2');
    await page.fill('[data-testid="unit-price-input-1"]', '20.00');

    // Submit changes
    await page.click('[data-testid="submit-purchase-order-button"]');
    await expect(page.locator('[data-testid="purchase-order-form"]')).not.toBeVisible({ timeout: 10000 });

    // Verify changes are persisted
    await page.click('[data-testid="purchase-order-row"]:first-child [data-testid="view-order-button"]');
    await expect(page.locator('[data-testid="purchase-order-detail"]')).toBeVisible();

    // Should have 2 lines now
    const lineRows = page.locator('[data-testid="order-line-row"]');
    await expect(lineRows).toHaveCount(2);

    // Verify first line was updated
    const firstLine = lineRows.nth(0);
    await expect(firstLine.locator('[data-testid="line-quantity"]')).toContainText('5');
    
    // Verify second line was added
    const secondLine = lineRows.nth(1);
    await expect(secondLine.locator('[data-testid="line-quantity"]')).toContainText('2');
    await expect(secondLine.locator('[data-testid="line-unit-price"]')).toContainText('20,00');
  });

  test('should handle network errors gracefully during creation', async ({ page }) => {
    // Intercept the API call and make it fail
    await page.route('**/api/purchase-orders', route => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Internal server error' })
      });
    });

    // Try to create order
    await page.click('[data-testid="nav-purchase-orders"]');
    await page.click('[data-testid="new-purchase-order-button"]');
    
    await page.fill('[data-testid="supplier-name-input"]', 'Error Test Supplier');
    await page.fill('[data-testid="order-date-input"]', '2024-08-02');
    
    // Add a line
    const materialInput = page.locator('[data-testid="material-autocomplete-0"]');
    await materialInput.click();
    await materialInput.fill('Error Material');
    await page.waitForSelector('[data-testid="material-option"]', { timeout: 5000 });
    await page.click('[data-testid="material-option"]:first-child');
    await page.fill('[data-testid="quantity-input-0"]', '1');
    await page.fill('[data-testid="unit-price-input-0"]', '10.00');

    await page.click('[data-testid="submit-purchase-order-button"]');

    // Should show error message
    await expect(page.locator('[data-testid="submit-error"]')).toContainText('Nepodařilo se vytvořit objednávku');
    
    // Form should still be open
    await expect(page.locator('[data-testid="purchase-order-form"]')).toBeVisible();
  });
});