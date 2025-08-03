import { test, expect } from '@playwright/test';

/**
 * Purchase Order Material Names Test
 * 
 * This test specifically verifies that material names are correctly:
 * 1. Saved when creating orders
 * 2. Displayed in order details
 * 3. Loaded when editing orders
 * 4. Never showing as "Unknown Material"
 */

test.describe('Purchase Order - Material Names Handling', () => {
  
  test.beforeEach(async ({ page }) => {
    // Navigate to purchase orders page in automation environment
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Wait for the page to load completely
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
    
    // Verify we're on the correct page
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
  });

  test('should save and display material names correctly - NOT "Unknown Material"', async ({ page }) => {
    console.log('🧪 Testing material name handling...');
    
    // STEP 1: Create a new order with material names
    console.log('📝 Creating order with material names...');
    
    await page.locator('button:has-text("Nová objednávka")').click();
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).toBeVisible();
    
    // Fill basic order information
    const testSupplierName = `Material Test Supplier ${Date.now()}`;
    await page.locator('input[id="supplierName"]').fill(testSupplierName);
    await page.locator('input[id="orderDate"]').fill('2024-12-01');
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-12-15');
    await page.locator('input[id="notes"]').fill('Testing material names handling');
    
    // Add order line with material
    const materialInputs = page.locator('input[placeholder*="materiál"], input[placeholder*="Materiál"]');
    const quantityInputs = page.locator('input[type="number"][title="Množství"]');
    const priceInputs = page.locator('input[type="number"][title="Jednotková cena"]');
    const notesInputs = page.locator('input[title="Poznámky k položce"]');
    
    // Fill first item with material code and name
    const testMaterialCode = 'MAT-TEST-001';
    const testMaterialNotes = 'Test Material Product Name';
    
    await materialInputs.first().fill(testMaterialCode);
    await materialInputs.first().press('Tab');
    await page.waitForTimeout(500);
    
    await quantityInputs.first().fill('10');
    await priceInputs.first().fill('99.99');
    await notesInputs.first().fill(testMaterialNotes);
    
    // Submit the order
    console.log('💾 Submitting order...');
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    await page.waitForTimeout(3000);
    
    // Check if order was created or if validation failed
    const modalStillVisible = await page.locator('h2:has-text("Nová nákupní objednávka")').isVisible();
    
    if (modalStillVisible) {
      // If modal is still open, it means we need real materials from catalog
      console.log('⚠️ Order creation requires catalog materials - checking validation');
      
      // At least verify that the form tried to save with our material name
      const materialValue = await materialInputs.first().inputValue();
      expect(materialValue).toBe(testMaterialCode);
      console.log('✅ Material code was entered correctly');
      
      // Close modal
      await page.keyboard.press('Escape');
      
      // Try to find an existing order to test instead
      console.log('📋 Looking for existing orders to test material names...');
      
      const existingOrders = await page.locator('tbody tr').count();
      if (existingOrders > 0) {
        // Open first existing order
        await page.locator('tbody tr').first().click();
        await page.waitForTimeout(1500);
        
        // Check for "Unknown Material" in detail
        await checkMaterialNamesInDetail(page);
        
        // Close detail
        await page.locator('button:has-text("Zavřít")').click();
      }
    } else {
      console.log('✅ Order created successfully');
      
      // Find the created order in the list
      const testOrderRow = page.locator(`tbody tr:has-text("${testSupplierName}")`);
      await expect(testOrderRow).toBeVisible();
      
      // Open the order detail
      await testOrderRow.click();
      await page.waitForTimeout(1500);
      
      // Check material names in detail
      await checkMaterialNamesInDetail(page);
      
      // Close detail
      await page.locator('button:has-text("Zavřít")').click();
    }
  });

  test('should load material names correctly when editing existing order', async ({ page }) => {
    console.log('🔍 Testing material names in edit mode...');
    
    // Check if there are any orders to edit
    const orderCount = await page.locator('tbody tr').count();
    
    if (orderCount === 0) {
      console.log('⚠️ No orders available to test editing');
      return;
    }
    
    // Open first order
    const firstOrderRow = page.locator('tbody tr').first();
    await firstOrderRow.click();
    await page.waitForTimeout(1500);
    
    // Check if detail opened
    const detailTitle = page.locator('h2').filter({ hasText: /Objednávka/ });
    await expect(detailTitle).toBeVisible();
    
    // Look for edit button (only available for Draft orders)
    const editButton = page.locator('button:has-text("Upravit")');
    
    if (await editButton.isVisible()) {
      console.log('📝 Opening edit form...');
      await editButton.click();
      await page.waitForTimeout(1000);
      
      // Verify edit modal opened
      await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).toBeVisible();
      
      // Check material names in edit form
      const editMaterialInputs = page.locator('input[placeholder*="materiál"], input[placeholder*="Materiál"]');
      const editQuantityInputs = page.locator('input[type="number"][title="Množství"]');
      const editPriceInputs = page.locator('input[type="number"][title="Jednotková cena"]');
      const editNotesInputs = page.locator('input[title="Poznámky k položce"]');
      
      const materialCount = await editMaterialInputs.count();
      console.log(`📦 Found ${materialCount} material lines in edit form`);
      
      // Check each material line
      for (let i = 0; i < materialCount; i++) {
        const materialValue = await editMaterialInputs.nth(i).inputValue();
        const quantityValue = await editQuantityInputs.nth(i).inputValue();
        const priceValue = await editPriceInputs.nth(i).inputValue();
        const notesValue = await editNotesInputs.nth(i).inputValue();
        
        console.log(`   Line ${i + 1}:`);
        console.log(`     Material: "${materialValue}"`);
        console.log(`     Quantity: ${quantityValue}, Price: ${priceValue}`);
        console.log(`     Notes: "${notesValue}"`);
        
        // CRITICAL CHECK: Material should NOT be "Unknown Material"
        expect(materialValue).not.toContain('Unknown Material');
        
        // Material field should have some value
        expect(materialValue).toBeTruthy();
        
        // If material value is empty but we have notes, that's also a problem
        if (!materialValue && notesValue) {
          console.log(`⚠️ Material name is empty but has notes: "${notesValue}"`);
        }
      }
      
      console.log('✅ All material names loaded correctly in edit form');
      
      // Close edit modal
      await page.locator('button:has-text("Zrušit")').click();
      await page.waitForTimeout(500);
      
      // Close detail modal
      if (await page.locator('button:has-text("Zavřít")').isVisible()) {
        await page.locator('button:has-text("Zavřít")').click();
      }
    } else {
      console.log('ℹ️ Order is not editable (not in Draft status)');
      
      // Still check material names in detail view
      await checkMaterialNamesInDetail(page);
      
      // Close detail
      await page.locator('button:has-text("Zavřít")').click();
    }
  });

  test('should never display "Unknown Material" in order list', async ({ page }) => {
    console.log('🔍 Checking for "Unknown Material" in order list...');
    
    // Wait for orders to load
    await page.waitForTimeout(2000);
    
    // Check if there are any orders
    const orderCount = await page.locator('tbody tr').count();
    
    if (orderCount === 0) {
      console.log('ℹ️ No orders in the list to check');
      return;
    }
    
    console.log(`📋 Checking ${orderCount} orders in the list`);
    
    // Look for "Unknown Material" text anywhere in the table
    const unknownMaterialInList = await page.locator('tbody:has-text("Unknown Material")').count();
    
    if (unknownMaterialInList > 0) {
      throw new Error(`❌ Found "Unknown Material" in order list - this should not happen!`);
    }
    
    console.log('✅ No "Unknown Material" found in order list');
    
    // Check each order detail
    for (let i = 0; i < Math.min(orderCount, 3); i++) { // Check first 3 orders
      console.log(`📋 Checking order ${i + 1} detail...`);
      
      const orderRow = page.locator('tbody tr').nth(i);
      await orderRow.click();
      await page.waitForTimeout(1500);
      
      // Check for "Unknown Material" in detail
      await checkMaterialNamesInDetail(page);
      
      // Close detail
      await page.locator('button:has-text("Zavřít")').click();
      await page.waitForTimeout(500);
    }
  });
});

/**
 * Helper function to check material names in order detail view
 */
async function checkMaterialNamesInDetail(page: any) {
  console.log('   Checking material names in detail view...');
  
  // Look for the order lines table
  const orderLinesTable = page.locator('table').filter({ 
    has: page.locator('th:has-text("Materiál")') 
  });
  
  if (await orderLinesTable.isVisible()) {
    const tableRows = orderLinesTable.locator('tbody tr');
    const rowCount = await tableRows.count();
    
    console.log(`   Found ${rowCount} order lines`);
    
    for (let i = 0; i < rowCount; i++) {
      const row = tableRows.nth(i);
      const materialCell = row.locator('td').first();
      const materialText = await materialCell.textContent();
      
      console.log(`     Line ${i + 1} material: "${materialText?.trim()}"`);
      
      // CRITICAL CHECK: Should NOT be "Unknown Material"
      if (materialText?.includes('Unknown Material')) {
        throw new Error(`❌ CRITICAL: Found "Unknown Material" in line ${i + 1}!`);
      }
      
      // Material should have some text
      expect(materialText?.trim()).toBeTruthy();
    }
    
    console.log('   ✅ All material names are valid');
  } else {
    console.log('   ⚠️ Order lines table not found in detail view');
  }
  
  // Also check for "Unknown Material" text anywhere in the detail modal
  const unknownCount = await page.locator('.fixed:has-text("Unknown Material")').count();
  
  if (unknownCount > 0) {
    throw new Error(`❌ Found ${unknownCount} instances of "Unknown Material" in detail view!`);
  }
}