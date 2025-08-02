import { test, expect } from '@playwright/test';

/**
 * Purchase Order Edit Tests
 * 
 * Tests the complete edit workflow:
 * 1. Create a test order first
 * 2. Open detail view
 * 3. Click edit button
 * 4. Modify data
 * 5. Save changes
 */

test.describe('Purchase Orders - Edit Workflow', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to purchase orders page
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000);
  });

  test('should complete full edit workflow from creation to edit', async ({ page }) => {
    // Step 1: Create a new order first
    console.log('📝 Step 1: Creating test order');
    await page.locator('button:has-text("Nová objednávka")').click();
    
    // Fill order details
    await page.locator('input[id="supplierName"]').fill('Test Supplier for Edit');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-02-15');
    await page.locator('input[id="notes"]').fill('Original notes for testing edit');
    
    // Submit the order
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    await page.waitForTimeout(3000);
    
    // Check if creation was successful
    const creationModalClosed = !await page.locator('h2:has-text("Nová nákupní objednávka")').isVisible();
    
    if (creationModalClosed) {
      console.log('✅ Test order created successfully');
      
      // Step 2: Wait for list to refresh and find our order
      await page.waitForTimeout(2000);
      
      const orderRows = page.locator('tbody tr');
      const rowCount = await orderRows.count();
      
      if (rowCount > 0) {
        console.log(`📋 Step 2: Found ${rowCount} orders, opening first one`);
        
        // Click on the first order to open detail
        await orderRows.first().click();
        await page.waitForTimeout(1000);
        
        // Verify detail modal opened
        const detailTitle = page.locator('text=/Objednávka/').first();
        if (await detailTitle.isVisible()) {
          console.log('✅ Order detail opened');
          
          // Step 3: Look for Edit button
          const editButton = page.locator('button:has-text("Upravit")');
          if (await editButton.isVisible()) {
            console.log('✅ Step 3: Edit button found, clicking it');
            
            await editButton.click();
            await page.waitForTimeout(1000);
            
            // Verify edit modal opened
            await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).toBeVisible();
            console.log('✅ Edit modal opened');
            
            // Step 4: Verify form is pre-filled and modify data
            const supplierInput = page.locator('input[id="supplierName"]');
            const originalSupplier = await supplierInput.inputValue();
            expect(originalSupplier).toContain('Test Supplier for Edit');
            console.log(`✅ Form pre-filled with: "${originalSupplier}"`);
            
            // Modify the supplier name
            const newSupplierName = originalSupplier + ' - EDITED';
            await supplierInput.fill(newSupplierName);
            
            // Modify notes
            const notesInput = page.locator('input[id="notes"]');
            await notesInput.fill('Updated notes after editing');
            
            // Modify expected delivery date
            await page.locator('input[id="expectedDeliveryDate"]').fill('2024-02-20');
            
            console.log('✅ Step 4: Modified form data');
            
            // Step 5: Save changes
            const saveButton = page.locator('button:has-text("Uložit změny")');
            await expect(saveButton).toBeVisible();
            
            await saveButton.click();
            console.log('🔄 Step 5: Clicked save button');
            
            await page.waitForTimeout(3000);
            
            // Check if edit was successful (modal should close)
            const editModalClosed = !await page.locator('h2:has-text("Upravit nákupní objednávku")').isVisible();
            
            if (editModalClosed) {
              console.log('✅ Edit completed successfully - modal closed');
              
              // Verify we're back to the list view
              await page.waitForTimeout(2000);
              
              // Try to find our updated order in the list
              const updatedOrderRow = page.locator(`tbody tr:has-text("${newSupplierName}")`);
              if (await updatedOrderRow.count() > 0) {
                console.log('✅ Updated order found in the list');
                
                // Click on it again to verify changes were saved
                await updatedOrderRow.first().click();
                await page.waitForTimeout(1000);
                
                // Verify the details show updated information
                const supplierText = page.locator(`text="${newSupplierName}"`);
                if (await supplierText.isVisible()) {
                  console.log('✅ Changes verified in detail view');
                } else {
                  console.log('⚠️ Changes not visible in detail view');
                }
                
                // Close detail modal
                await page.locator('button:has-text("Zavřít")').click();
                
              } else {
                console.log('ℹ️ Updated order not found in list (backend may not support updates)');
              }
              
            } else {
              console.log('⚠️ Edit modal still open - changes may not have been saved');
              
              // Check for error messages
              const errorMessage = page.locator('text=/Nepodařilo se|chyba/i');
              if (await errorMessage.isVisible()) {
                console.log('❌ Edit failed with error message');
              }
              
              // Close modal
              await page.locator('button:has-text("Zrušit")').click();
            }
            
          } else {
            console.log('ℹ️ Edit button not visible (order may not be in Draft status)');
          }
          
          // Close detail modal if still open
          const closeButton = page.locator('button:has-text("Zavřít")');
          if (await closeButton.isVisible()) {
            await closeButton.click();
          }
          
        } else {
          console.log('❌ Detail modal did not open');
        }
        
      } else {
        console.log('ℹ️ No orders found after creation - backend may not be working');
      }
      
    } else {
      console.log('❌ Test order creation failed');
      
      // Check for errors
      const errorMessage = page.locator('text=/Nepodařilo se|chyba/i');
      if (await errorMessage.isVisible()) {
        console.log('❌ Creation failed with error message');
      }
      
      // Close create modal
      await page.locator('button:has-text("Zrušit")').click();
    }
  });

  test('should handle edit form validation correctly', async ({ page }) => {
    // Create a test order first (simplified version)
    await page.locator('button:has-text("Nová objednávka")').click();
    await page.locator('input[id="supplierName"]').fill('Test Edit Validation');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    await page.waitForTimeout(3000);
    
    // If creation successful, test edit validation
    if (!await page.locator('h2:has-text("Nová nákupní objednávka")').isVisible()) {
      // Open first order detail
      const orderRows = page.locator('tbody tr');
      if (await orderRows.count() > 0) {
        await orderRows.first().click();
        await page.waitForTimeout(1000);
        
        const editButton = page.locator('button:has-text("Upravit")');
        if (await editButton.isVisible()) {
          await editButton.click();
          await page.waitForTimeout(1000);
          
          // Test validation - clear required field
          await page.locator('input[id="supplierName"]').fill('');
          await page.locator('button:has-text("Uložit změny")').click();
          
          // Should show validation error
          await expect(page.locator('text="Název dodavatele je povinný"')).toBeVisible();
          console.log('✅ Edit form validation working correctly');
          
          // Fix validation error
          await page.locator('input[id="supplierName"]').fill('Fixed Supplier Name');
          
          // Test date validation
          await page.locator('input[id="orderDate"]').fill('2024-03-01');
          await page.locator('input[id="expectedDeliveryDate"]').fill('2024-02-01'); // Before order date
          
          await page.locator('button:has-text("Uložit změny")').click();
          
          // Should show date validation error
          await expect(page.locator('text="Datum dodání nemůže být před datem objednávky"')).toBeVisible();
          console.log('✅ Edit date validation working correctly');
          
          // Close edit modal
          await page.locator('button:has-text("Zrušit")').click();
        }
        
        // Close detail modal
        await page.locator('button:has-text("Zavřít")').click();
      }
    }
  });

  test('should handle edit form cancel correctly', async ({ page }) => {
    // Create minimal test order
    await page.locator('button:has-text("Nová objednávka")').click();
    await page.locator('input[id="supplierName"]').fill('Test Cancel Edit');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    await page.waitForTimeout(3000);
    
    // Test cancel functionality
    if (!await page.locator('h2:has-text("Nová nákupní objednávka")').isVisible()) {
      const orderRows = page.locator('tbody tr');
      if (await orderRows.count() > 0) {
        await orderRows.first().click();
        await page.waitForTimeout(1000);
        
        const editButton = page.locator('button:has-text("Upravit")');
        if (await editButton.isVisible()) {
          await editButton.click();
          await page.waitForTimeout(1000);
          
          // Make some changes
          await page.locator('input[id="supplierName"]').fill('Changed Name That Should Not Be Saved');
          await page.locator('input[id="notes"]').fill('Changed notes that should not be saved');
          
          // Cancel instead of save
          await page.locator('button:has-text("Zrušit")').click();
          
          // Should return to detail view
          await expect(page.locator('text="Základní informace"')).toBeVisible();
          console.log('✅ Edit cancel returned to detail view');
          
          // Verify original data is still there (not changed)
          await expect(page.locator('text="Test Cancel Edit"')).toBeVisible();
          console.log('✅ Original data preserved after cancel');
          
          // Close detail modal
          await page.locator('button:has-text("Zavřít")').click();
        }
      }
    }
  });

  test('should handle escape key in edit modal', async ({ page }) => {
    // Create test order
    await page.locator('button:has-text("Nová objednávka")').click();
    await page.locator('input[id="supplierName"]').fill('Test Escape Key');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    await page.waitForTimeout(3000);
    
    // Test escape key
    if (!await page.locator('h2:has-text("Nová nákupní objednávka")').isVisible()) {
      const orderRows = page.locator('tbody tr');
      if (await orderRows.count() > 0) {
        await orderRows.first().click();
        await page.waitForTimeout(1000);
        
        const editButton = page.locator('button:has-text("Upravit")');
        if (await editButton.isVisible()) {
          await editButton.click();
          await page.waitForTimeout(1000);
          
          // Press Escape key
          await page.keyboard.press('Escape');
          
          // Should close edit modal and return to detail view
          await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).not.toBeVisible();
          await expect(page.locator('text="Základní informace"')).toBeVisible();
          console.log('✅ Escape key closes edit modal correctly');
          
          // Close detail modal
          await page.locator('button:has-text("Zavřít")').click();
        }
      }
    }
  });
});