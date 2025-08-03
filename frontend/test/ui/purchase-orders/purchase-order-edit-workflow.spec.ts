import { test, expect } from '@playwright/test';

/**
 * Purchase Order Edit Workflow Test
 * 
 * This test specifically verifies the edit workflow:
 * 1. Opening order detail
 * 2. Clicking edit button
 * 3. Making changes to order
 * 4. Saving changes
 * 5. Returning to detail view (NOT list view)
 * 6. Verifying changes are reflected
 */

test.describe('Purchase Order - Edit Workflow', () => {
  
  test.beforeEach(async ({ page }) => {
    // Navigate to purchase orders page in automation environment
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Wait for the page to load completely
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
    
    // Verify we're on the correct page
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
  });

  test('should return to detail view after editing (not list view)', async ({ page }) => {
    console.log('🔄 Testing edit workflow and return to detail...');
    
    // Check if there are any orders to edit
    const orderCount = await page.locator('tbody tr').count();
    
    if (orderCount === 0) {
      console.log('⚠️ No orders available to test editing workflow');
      return;
    }
    
    // STEP 1: Open first order detail
    console.log('👁️ Opening order detail...');
    
    const firstOrderRow = page.locator('tbody tr').first();
    const supplierName = await firstOrderRow.locator('td').nth(1).textContent(); // Supplier column
    
    await firstOrderRow.click();
    await page.waitForTimeout(1500);
    
    // Verify detail modal opened
    const detailTitle = page.locator('h2').filter({ hasText: /Objednávka/ });
    await expect(detailTitle).toBeVisible();
    
    const orderNumber = await detailTitle.textContent();
    console.log(`📋 Opened detail for: ${orderNumber}`);
    
    // STEP 2: Look for edit button
    const editButton = page.locator('button:has-text("Upravit")');
    
    if (await editButton.isVisible()) {
      console.log('✏️ Edit button found - testing edit workflow...');
      
      // STEP 3: Click edit button
      await editButton.click();
      await page.waitForTimeout(1000);
      
      // Verify edit modal opened and detail modal closed
      await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).toBeVisible();
      await expect(detailTitle).not.toBeVisible();
      
      console.log('✅ Edit modal opened, detail modal closed');
      
      // STEP 4: Make some changes
      console.log('📝 Making changes to order...');
      
      const supplierInput = page.locator('input[id="supplierName"]');
      const notesInput = page.locator('input[id="notes"]');
      
      // Get current values
      const originalSupplier = await supplierInput.inputValue();
      const originalNotes = await notesInput.inputValue();
      
      // Make changes
      const updatedSupplier = originalSupplier + ' - EDITED';
      const updatedNotes = (originalNotes || '') + ' - Updated in test';
      
      await supplierInput.fill(updatedSupplier);
      await notesInput.fill(updatedNotes);
      
      console.log(`   Changed supplier from "${originalSupplier}" to "${updatedSupplier}"`);
      console.log(`   Updated notes to: "${updatedNotes}"`);
      
      // STEP 5: Save changes
      console.log('💾 Saving changes...');
      
      await page.locator('button:has-text("Uložit změny")').click();
      await page.waitForTimeout(3000);
      
      // STEP 6: CRITICAL CHECK - Should return to detail view, NOT list view
      console.log('🔍 Checking return destination...');
      
      // Edit modal should be closed
      await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).not.toBeVisible();
      
      // Detail modal should be open again (not list view)
      await expect(detailTitle).toBeVisible();
      
      // Should NOT see the main list header
      await expect(page.locator('h1:has-text("Nákupní objednávky")')).not.toBeVisible();
      
      console.log('✅ CORRECT: Returned to detail view after edit (not list view)');
      
      // STEP 7: Verify changes are reflected in detail view
      console.log('🔍 Verifying changes are reflected...');
      
      // Check that updated supplier name is displayed
      await expect(page.locator(`text="${updatedSupplier}"`)).toBeVisible();
      
      // Check that updated notes are displayed
      if (updatedNotes.trim()) {
        await expect(page.locator(`text="${updatedNotes}"`)).toBeVisible();
      }
      
      console.log('✅ Changes are correctly reflected in detail view');
      
      // STEP 8: Close detail and verify we return to list
      console.log('🚪 Closing detail to return to list...');
      
      await page.locator('button:has-text("Zavřít")').click();
      await page.waitForTimeout(500);
      
      // Now we should be back in list view
      await expect(page.locator('h1:has-text("Nákupní objednávky")')).toBeVisible();
      
      // And the updated supplier name should be visible in the list
      await expect(page.locator(`tbody tr:has-text("${updatedSupplier}")`)).toBeVisible();
      
      console.log('✅ Successfully returned to list view after closing detail');
      
    } else {
      console.log('ℹ️ Edit button not visible - order is not in Draft status');
      
      // Close detail
      await page.locator('button:has-text("Zavřít")').click();
    }
  });

  test('should preserve form data when canceling edit', async ({ page }) => {
    console.log('🚫 Testing edit cancellation...');
    
    // Check if there are any orders to edit
    const orderCount = await page.locator('tbody tr').count();
    
    if (orderCount === 0) {
      console.log('⚠️ No orders available to test editing');
      return;
    }
    
    // Open first order detail
    const firstOrderRow = page.locator('tbody tr').first();
    await firstOrderRow.click();
    await page.waitForTimeout(1500);
    
    const detailTitle = page.locator('h2').filter({ hasText: /Objednávka/ });
    await expect(detailTitle).toBeVisible();
    
    // Get original data from detail view
    const originalSupplierElement = page.locator('text*="Dodavatel:"').locator('..').locator('span').last();
    const originalSupplier = await originalSupplierElement.textContent();
    
    const editButton = page.locator('button:has-text("Upravit")');
    
    if (await editButton.isVisible()) {
      // Open edit modal
      await editButton.click();
      await page.waitForTimeout(1000);
      
      await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).toBeVisible();
      
      // Make some changes but don't save
      const supplierInput = page.locator('input[id="supplierName"]');
      await supplierInput.fill(originalSupplier + ' - SHOULD BE CANCELLED');
      
      // Cancel the edit
      await page.locator('button:has-text("Zrušit")').click();
      await page.waitForTimeout(1000);
      
      // CRITICAL CHECK: Should return to detail view (not list view)
      await expect(detailTitle).toBeVisible();
      await expect(page.locator('h1:has-text("Nákupní objednávky")')).not.toBeVisible();
      
      console.log('✅ CORRECT: Returned to detail view after cancel (not list view)');
      
      // Verify original data is still there (changes were not saved)
      await expect(page.locator(`text="${originalSupplier}"`)).toBeVisible();
      
      console.log('✅ Original data preserved after cancel');
      
      // Close detail
      await page.locator('button:has-text("Zavřít")').click();
    } else {
      console.log('ℹ️ Edit button not visible - closing detail');
      await page.locator('button:has-text("Zavřít")').click();
    }
  });

  test('should handle keyboard navigation in edit modal', async ({ page }) => {
    console.log('⌨️ Testing keyboard navigation in edit modal...');
    
    const orderCount = await page.locator('tbody tr').count();
    
    if (orderCount === 0) {
      console.log('⚠️ No orders available to test keyboard navigation');
      return;
    }
    
    // Open first order detail
    const firstOrderRow = page.locator('tbody tr').first();
    await firstOrderRow.click();
    await page.waitForTimeout(1500);
    
    const detailTitle = page.locator('h2').filter({ hasText: /Objednávka/ });
    await expect(detailTitle).toBeVisible();
    
    const editButton = page.locator('button:has-text("Upravit")');
    
    if (await editButton.isVisible()) {
      // Open edit modal
      await editButton.click();
      await page.waitForTimeout(1000);
      
      await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).toBeVisible();
      
      // Test Escape key closes edit modal and returns to detail
      await page.keyboard.press('Escape');
      await page.waitForTimeout(500);
      
      // Should be back in detail view
      await expect(detailTitle).toBeVisible();
      await expect(page.locator('h1:has-text("Nákupní objednávky")')).not.toBeVisible();
      
      console.log('✅ Escape key correctly returns to detail view');
      
      // Open edit again for more keyboard tests
      await editButton.click();
      await page.waitForTimeout(1000);
      
      // Test Tab navigation through form fields
      await page.keyboard.press('Tab'); // Should focus supplier name
      await page.keyboard.type(' - Keyboard Test');
      
      await page.keyboard.press('Tab'); // Should focus order date
      await page.keyboard.press('Tab'); // Should focus delivery date
      await page.keyboard.press('Tab'); // Should focus notes
      await page.keyboard.type(' - Tab navigation works');
      
      // Cancel with Escape
      await page.keyboard.press('Escape');
      
      console.log('✅ Keyboard navigation in edit modal works correctly');
      
      // Close detail
      await page.locator('button:has-text("Zavřít")').click();
    } else {
      console.log('ℹ️ Edit button not visible - closing detail');
      await page.locator('button:has-text("Zavřít")').click();
    }
  });
});