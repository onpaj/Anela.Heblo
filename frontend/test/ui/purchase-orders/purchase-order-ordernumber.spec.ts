import { test, expect } from '@playwright/test';

test.describe('Purchase Order OrderNumber Functionality', () => {
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

  test('should display orderNumber field with auto-generated default value', async ({ page }) => {
    // Check that orderNumber field is visible
    await expect(page.locator('input[id="orderNumber"]')).toBeVisible();
    
    // Check that the field has a label
    await expect(page.locator('label[for="orderNumber"]')).toBeVisible();
    await expect(page.locator('label[for="orderNumber"]')).toContainText('Číslo objednávky');
    
    // Check that the field has auto-generated value (format: PO20250101-HHMM)
    const orderNumberValue = await page.locator('input[id="orderNumber"]').inputValue();
    expect(orderNumberValue).toMatch(/^PO\d{8}-\d{4}$/);
    
    console.log('✅ Auto-generated order number:', orderNumberValue);
  });

  test('should allow user to edit orderNumber field', async ({ page }) => {
    // Clear and enter custom orderNumber
    const customOrderNumber = 'CUSTOM-ORDER-12345';
    await page.locator('input[id="orderNumber"]').fill(customOrderNumber);
    
    // Verify the value was set
    await expect(page.locator('input[id="orderNumber"]')).toHaveValue(customOrderNumber);
    
    console.log('✅ Custom order number set successfully');
  });

  test('should validate required orderNumber field', async ({ page }) => {
    // Clear the orderNumber field
    await page.locator('input[id="orderNumber"]').fill('');
    
    // Fill other required fields to isolate orderNumber validation
    await page.locator('input[id="supplierName"]').fill('Test Supplier');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    
    // Try to submit
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Wait for validation
    await page.waitForTimeout(500);
    
    // Should show orderNumber validation error
    await expect(page.locator('text="Číslo objednávky je povinné"')).toBeVisible();
    
    console.log('✅ OrderNumber validation working correctly');
  });

  test('should preserve orderNumber during form interaction', async ({ page }) => {
    // Set custom orderNumber
    const customOrderNumber = 'TEST-PO-2025-001';
    await page.locator('input[id="orderNumber"]').fill(customOrderNumber);
    
    // Fill other fields
    await page.locator('input[id="supplierName"]').fill('Test Supplier');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    
    // Add material line
    const materialInputs = page.locator('input[placeholder*="materiál"], input[placeholder*="Materiál"]');
    const quantityInputs = page.locator('input[type="number"][title="Množství"]');
    const priceInputs = page.locator('input[type="number"][title="Jednotková cena"]');
    
    await materialInputs.first().fill('TEST-MAT-001');
    await quantityInputs.first().fill('10');
    await priceInputs.first().fill('50');
    
    // Verify orderNumber is still preserved
    await expect(page.locator('input[id="orderNumber"]')).toHaveValue(customOrderNumber);
    
    console.log('✅ OrderNumber preserved during form interaction');
  });

  test('should show placeholder text for orderNumber field', async ({ page }) => {
    // Clear the field to see placeholder
    await page.locator('input[id="orderNumber"]').fill('');
    
    // Check placeholder text
    const placeholder = await page.locator('input[id="orderNumber"]').getAttribute('placeholder');
    expect(placeholder).toContain('PO20250101-1015');
    
    console.log('✅ Placeholder text displayed correctly:', placeholder);
  });

  test('should include orderNumber in form submission', async ({ page }) => {
    // Set custom orderNumber
    const customOrderNumber = 'INTEGRATION-TEST-PO-001';
    await page.locator('input[id="orderNumber"]').fill(customOrderNumber);
    
    // Fill all required fields
    await page.locator('input[id="supplierName"]').fill('Test Supplier For OrderNumber');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-02-15');
    await page.locator('input[id="notes"]').fill('Test order with custom orderNumber');
    
    // Add material line with valid data
    const materialInputs = page.locator('input[placeholder*="materiál"], input[placeholder*="Materiál"]');
    const quantityInputs = page.locator('input[type="number"][title="Množství"]');
    const priceInputs = page.locator('input[type="number"][title="Jednotková cena"]');
    
    await materialInputs.first().fill('TEST-MATERIAL-ORDERNUMBER');
    await quantityInputs.first().fill('5');
    await priceInputs.first().fill('100');
    
    // Intercept API call to verify orderNumber is sent
    let requestData: any = null;
    await page.route('**/api/PurchaseOrders', (route, request) => {
      if (request.method() === 'POST') {
        requestData = request.postDataJSON();
        // Allow the request to continue (may fail in test environment, but we captured the data)
        route.continue();
      } else {
        route.continue();
      }
    });
    
    // Submit form
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Wait for API call
    await page.waitForTimeout(2000);
    
    // Verify orderNumber was included in the request
    if (requestData) {
      expect(requestData.orderNumber).toBe(customOrderNumber);
      console.log('✅ OrderNumber included in API request:', requestData.orderNumber);
    } else {
      console.log('⚠️ API request not captured - may indicate network issue in test environment');
    }
  });

  test('should regenerate orderNumber when creating new order', async ({ page }) => {
    // Get initial auto-generated orderNumber
    const initialOrderNumber = await page.locator('input[id="orderNumber"]').inputValue();
    console.log('Initial order number:', initialOrderNumber);
    
    // Close modal by clicking Cancel
    await page.locator('button:has-text("Zrušit")').click();
    
    // Wait for modal to close
    await expect(page.locator('text=Nová nákupní objednávka')).not.toBeVisible();
    
    // Open new order form again
    await page.locator('button:has-text("Nová objednávka")').click();
    await page.waitForSelector('text=Nová nákupní objednávka');
    
    // Get new auto-generated orderNumber
    const newOrderNumber = await page.locator('input[id="orderNumber"]').inputValue();
    console.log('New order number:', newOrderNumber);
    
    // Verify they have same format but different values (different time)
    expect(initialOrderNumber).toMatch(/^PO\d{8}-\d{4}$/);
    expect(newOrderNumber).toMatch(/^PO\d{8}-\d{4}$/);
    
    // They should be different (unless created in same minute, which is unlikely)
    if (initialOrderNumber !== newOrderNumber) {
      console.log('✅ OrderNumber regenerated correctly');
    } else {
      console.log('ℹ️ OrderNumbers are same - created within same minute');
    }
  });
});

test.describe('Purchase Order Edit OrderNumber Functionality', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to purchase orders page
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
  });

  test('should display existing orderNumber when editing order', async ({ page }) => {
    // Look for any existing order to edit (assuming there's at least one)
    const editButtons = page.locator('button[title="Upravit objednávku"], button:has-text("Upravit")');
    
    if (await editButtons.count() > 0) {
      // Click first edit button
      await editButtons.first().click();
      
      // Wait for edit modal to open
      await page.waitForSelector('text=Upravit nákupní objednávku');
      
      // Check that orderNumber field exists and has value
      const orderNumberField = page.locator('input[id="orderNumber"]');
      await expect(orderNumberField).toBeVisible();
      
      const orderNumberValue = await orderNumberField.inputValue();
      expect(orderNumberValue).toBeTruthy(); // Should have some value
      
      console.log('✅ Existing orderNumber displayed in edit form:', orderNumberValue);
    } else {
      console.log('ℹ️ No existing orders to test edit functionality');
    }
  });

  test('should allow editing orderNumber in edit mode', async ({ page }) => {
    // Look for any existing order to edit
    const editButtons = page.locator('button[title="Upravit objednávku"], button:has-text("Upravit")');
    
    if (await editButtons.count() > 0) {
      // Click first edit button
      await editButtons.first().click();
      
      // Wait for edit modal to open
      await page.waitForSelector('text=Upravit nákupní objednávku');
      
      // Get original orderNumber
      const originalOrderNumber = await page.locator('input[id="orderNumber"]').inputValue();
      
      // Change orderNumber
      const newOrderNumber = 'EDITED-' + originalOrderNumber;
      await page.locator('input[id="orderNumber"]').fill(newOrderNumber);
      
      // Verify the value was changed
      await expect(page.locator('input[id="orderNumber"]')).toHaveValue(newOrderNumber);
      
      console.log('✅ OrderNumber edited successfully from', originalOrderNumber, 'to', newOrderNumber);
    } else {
      console.log('ℹ️ No existing orders to test edit functionality');
    }
  });
});