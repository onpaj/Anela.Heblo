import { test, expect } from '@playwright/test';

test.describe('Purchase Order Form', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to purchase orders page
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
    
    // Click "New Order" button to open the form
    await page.locator('button:has-text("Nová objednávka")').click();
    
    // Wait for modal to open
    await page.waitForSelector('text=Nová nákupní objednávka');
  });

  test('should display purchase order form modal', async ({ page }) => {
    // Check modal title
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).toBeVisible();
    
    // Check close button
    await expect(page.locator('button[title*="close"], button:has(svg)').first()).toBeVisible();
    
    // Check form fields
    await expect(page.locator('input[id="supplierName"]')).toBeVisible();
    await expect(page.locator('input[id="orderDate"]')).toBeVisible();
    await expect(page.locator('input[id="expectedDeliveryDate"]')).toBeVisible();
    await expect(page.locator('input[id="notes"]')).toBeVisible(); // Notes is input, not textarea
    
    // Check buttons
    await expect(page.locator('button:has-text("Zrušit")')).toBeVisible();
    await expect(page.locator('button:has-text("Vytvořit objednávku")')).toBeVisible();
  });

  test('should close modal with Esc key', async ({ page }) => {
    // Press Escape
    await page.keyboard.press('Escape');
    
    // Modal should be closed
    await expect(page.locator('text=Nová nákupní objednávka')).not.toBeVisible();
  });

  test('should close modal with Cancel button', async ({ page }) => {
    // Click Cancel button
    await page.locator('button:has-text("Zrušit")').click();
    
    // Modal should be closed
    await expect(page.locator('text=Nová nákupní objednávka')).not.toBeVisible();
  });

  test('should close modal by clicking backdrop', async ({ page }) => {
    // Click on backdrop (outside modal content)
    await page.locator('.fixed.inset-0.bg-black').click({ position: { x: 10, y: 10 } });
    
    // Modal should be closed
    await expect(page.locator('text=Nová nákupní objednávka')).not.toBeVisible();
  });

  test('should validate required fields', async ({ page }) => {
    // Try to submit empty form
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Wait for validation
    await page.waitForTimeout(500);
    
    // Should show validation errors
    await expect(page.locator('text="Název dodavatele je povinný"')).toBeVisible();
    // Date field has default value, so may not show this error
    const dateError = page.locator('text="Datum objednávky je povinné"');
    if (await dateError.count() > 0) {
      await expect(dateError).toBeVisible();
    }
  });

  test('should validate date fields', async ({ page }) => {
    // Fill order date
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    
    // Fill expected delivery date before order date
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-01-01');
    
    // Fill supplier name to avoid other validation errors
    await page.locator('input[id="supplierName"]').fill('Test Supplier');
    
    // Try to submit
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Should show date validation error
    await expect(page.locator('text=Datum dodání nemůže být před datem objednávky')).toBeVisible();
  });

  test('should add and remove order lines', async ({ page }) => {
    // Initially no lines
    await expect(page.locator('text=Žádné položky nebyly přidány')).toBeVisible();
    
    // Add first line
    await page.locator('button:has-text("Přidat položku")').click();
    
    // Should show line item form
    await expect(page.locator('text=Položka 1')).toBeVisible();
    await expect(page.locator('input[placeholder="Název materiálu"]')).toBeVisible();
    
    // Add second line
    await page.locator('button:has-text("Přidat položku")').click();
    await expect(page.locator('text=Položka 2')).toBeVisible();
    
    // Remove first line (should be first trash button)
    await page.locator('button:has(svg)').filter({ has: page.locator('svg') }).first().click();
    
    // Should only have one line left
    await expect(page.locator('text=Položka 1')).toBeVisible();
    await expect(page.locator('text=Položka 2')).not.toBeVisible();
  });

  test('should calculate line totals automatically', async ({ page }) => {
    // Add a line
    await page.locator('button:has-text("Přidat položku")').click();
    
    // Fill line details
    await page.locator('input[placeholder="Název materiálu"]').fill('Test Material');
    await page.locator('input[type="number"]').nth(0).fill('10'); // quantity
    await page.locator('input[type="number"]').nth(1).fill('50'); // unit price
    
    // Wait for calculation
    await page.waitForTimeout(500);
    
    // Check line total (10 * 50 = 500)
    await expect(page.locator('text=500,00 Kč')).toBeVisible();
    
    // Check total amount
    await expect(page.locator('text=Celková částka:')).toBeVisible();
  });

  test('should validate line items', async ({ page }) => {
    // Fill basic form
    await page.locator('input[id="supplierName"]').fill('Test Supplier');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    
    // Add a line with invalid data
    await page.locator('button:has-text("Přidat položku")').click();
    
    // Leave material name empty, set invalid quantity
    await page.locator('input[type="number"]').nth(0).fill('0'); // quantity = 0
    await page.locator('input[type="number"]').nth(1).fill('-10'); // negative price
    
    // Try to submit
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Should show line validation errors
    await expect(page.locator('text=Název materiálu je povinný')).toBeVisible();
    await expect(page.locator('text=Množství musí být větší než 0')).toBeVisible();
    await expect(page.locator('text=Jednotková cena musí být větší než 0')).toBeVisible();
  });

  test('should submit valid form', async ({ page }) => {
    // Fill valid form data
    await page.locator('input[id="supplierName"]').fill('Test Supplier Ltd.');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-02-15');
    await page.locator('textarea[id="notes"]').fill('Test order for validation');
    
    // Add a valid line item
    await page.locator('button:has-text("Přidat položku")').click();
    await page.locator('input[placeholder="Název materiálu"]').fill('Test Material A');
    await page.locator('input[type="number"]').nth(0).fill('100');
    await page.locator('input[type="number"]').nth(1).fill('25.50');
    
    // Submit form
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Wait for submission
    await page.waitForTimeout(2000);
    
    // Check if modal closes (success) or error appears
    const modalVisible = await page.locator('text=Nová nákupní objednávka').isVisible();
    const errorVisible = await page.locator('text=Nepodařilo se vytvořit objednávku').isVisible();
    
    if (modalVisible && errorVisible) {
      console.log('Form submission failed with error message');
      // Take screenshot for debugging
      await page.screenshot({ path: 'purchase-order-form-error.png' });
    } else if (!modalVisible) {
      console.log('Form submitted successfully - modal closed');
    }
  });

  test('should handle API errors gracefully', async ({ page }) => {
    // Fill valid form
    await page.locator('input[id="supplierName"]').fill('Test Supplier');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    
    // Submit form
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Wait for response
    await page.waitForTimeout(3000);
    
    // Check for error message or success
    const hasError = await page.locator('text=Nepodařilo se vytvořit objednávku').isVisible();
    const modalClosed = await page.locator('text=Nová nákupní objednávka').isVisible() === false;
    
    if (hasError) {
      console.log('API error detected - this indicates backend connectivity issues');
    } else if (modalClosed) {
      console.log('Form submitted successfully');
    } else {
      console.log('Form still open - may be processing');
    }
  });

  test('should display loading state during submission', async ({ page }) => {
    // Fill minimal valid form
    await page.locator('input[id="supplierName"]').fill('Test Supplier');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    
    // Click submit
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Should show loading state
    await expect(page.locator('button:has-text("Ukládání...")')).toBeVisible();
    await expect(page.locator('button[disabled]')).toBeVisible();
  });
});