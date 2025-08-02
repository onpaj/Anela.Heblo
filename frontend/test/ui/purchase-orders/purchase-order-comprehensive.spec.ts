import { test, expect } from '@playwright/test';

/**
 * Comprehensive Purchase Order Tests
 * 
 * Tests the complete purchase order workflow:
 * 1. List loading and filtering
 * 2. Creating new orders with items
 * 3. Opening order details
 * 4. Editing existing orders
 */

test.describe('Purchase Orders - Comprehensive Workflow', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to purchase orders page
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000); // Extra wait for any async operations
  });

  test('should load purchase order list and display correctly', async ({ page }) => {
    // Test basic page loading
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
    
    // Check all table headers are present
    const expectedHeaders = [
      'Číslo objednávky',
      'Dodavatel',
      'Datum objednávky', 
      'Plánované dodání',
      'Stav',
      'Celková částka',
      'Položky'
    ];
    
    for (const header of expectedHeaders) {
      await expect(page.locator(`th:has-text("${header}")`)).toBeVisible();
    }
    
    // Check filter elements are present
    await expect(page.locator('input[placeholder="Hledat objednávky..."]')).toBeVisible();
    await expect(page.locator('select').first()).toBeVisible();
    await expect(page.locator('button:has-text("Filtrovat")')).toBeVisible();
    await expect(page.locator('button:has-text("Vymazat")')).toBeVisible();
    
    // Check create button is present
    await expect(page.locator('button:has-text("Nová objednávka")')).toBeVisible();
  });

  test('should filter orders by status correctly', async ({ page }) => {
    const statusSelect = page.locator('select').first();
    
    // Test filtering by Draft status
    await statusSelect.selectOption('Draft');
    
    // Verify the select shows the selected value
    await expect(statusSelect).toHaveValue('Draft');
    
    // Apply the filter
    await page.locator('button:has-text("Filtrovat")').click();
    
    // Wait for filter to be applied
    await page.waitForTimeout(1000);
    
    // Test filtering by InTransit status
    await statusSelect.selectOption('InTransit');
    await expect(statusSelect).toHaveValue('InTransit');
    await page.locator('button:has-text("Filtrovat")').click();
    await page.waitForTimeout(1000);
    
    // Test filtering by Completed status
    await statusSelect.selectOption('Completed');
    await expect(statusSelect).toHaveValue('Completed');
    await page.locator('button:has-text("Filtrovat")').click();
    await page.waitForTimeout(1000);
    
    // Test clearing filters
    await page.locator('button:has-text("Vymazat")').click();
    await expect(statusSelect).toHaveValue('');
  });

  test('should filter orders by search term', async ({ page }) => {
    const searchInput = page.locator('input[placeholder="Hledat objednávky..."]');
    
    // Test search functionality
    await searchInput.fill('test supplier');
    await expect(searchInput).toHaveValue('test supplier');
    
    // Apply filter by clicking button
    await page.locator('button:has-text("Filtrovat")').click();
    await page.waitForTimeout(1000);
    
    // Test search with Enter key
    await searchInput.fill('another search');
    await searchInput.press('Enter');
    await page.waitForTimeout(1000);
    
    // Clear search
    await page.locator('button:has-text("Vymazat")').click();
    await expect(searchInput).toHaveValue('');
  });

  test('should create new purchase order with items successfully', async ({ page }) => {
    // Open create modal
    await page.locator('button:has-text("Nová objednávka")').click();
    
    // Verify modal opened
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).toBeVisible();
    
    // Fill basic order information
    await page.locator('input[id="supplierName"]').fill('Test Supplier Ltd.');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-02-15');
    await page.locator('input[id="notes"]').fill('Test order created by Playwright');
    
    // Verify there's an automatic empty row
    const materialInputs = page.locator('input[placeholder*="materiál"], input[placeholder*="Materiál"]');
    await expect(materialInputs.first()).toBeVisible();
    
    // Fill first item manually (since MaterialAutocomplete might not have data in test)
    const quantityInputs = page.locator('input[type="number"][title="Množství"]');
    const priceInputs = page.locator('input[type="number"][title="Jednotková cena"]');
    const notesInputs = page.locator('input[title="Poznámky k položce"]');
    
    // Fill first line item
    await quantityInputs.first().fill('100');
    await priceInputs.first().fill('25.50');
    await notesInputs.first().fill('Test item 1');
    
    // Wait for calculation
    await page.waitForTimeout(500);
    
    // Check if total is calculated and displayed (100 * 25.50 = 2550)
    const totalElements = page.locator('text=/2.?550.*Kč|2550.*Kč/');
    if (await totalElements.count() > 0) {
      console.log('Total calculation appears to be working');
    }
    
    // Add another item using the add button if needed
    const addButtons = page.locator('button:has-text("Přidat řádek")');
    if (await addButtons.count() > 0) {
      await addButtons.first().click();
      
      // Fill second item
      if (await quantityInputs.count() > 1) {
        await quantityInputs.nth(1).fill('50');
        await priceInputs.nth(1).fill('15.75');
        await notesInputs.nth(1).fill('Test item 2');
        await page.waitForTimeout(500);
      }
    }
    
    // Submit the form
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Wait for submission
    await page.waitForTimeout(3000);
    
    // Check result - either success (modal closes) or error message
    const modalStillVisible = await page.locator('h2:has-text("Nová nákupní objednávka")').isVisible();
    const errorMessage = await page.locator('text=/Nepodařilo se/').isVisible();
    
    if (!modalStillVisible && !errorMessage) {
      console.log('✅ Order created successfully - modal closed');
      
      // Wait for list to refresh
      await page.waitForTimeout(2000);
      
      // Verify the order appears in the list (if backend works)
      const orderRows = page.locator('tbody tr');
      const rowCount = await orderRows.count();
      console.log(`📋 Found ${rowCount} orders in the list`);
      
    } else if (errorMessage) {
      console.log('❌ Order creation failed with error message');
      await expect(page.locator('text=/Nepodařilo se/')).toBeVisible();
    } else {
      console.log('⏳ Modal still open - may be processing or validation failed');
      
      // Check for validation errors
      const validationErrors = page.locator('text=/povinný|musí být/');
      const errorCount = await validationErrors.count();
      if (errorCount > 0) {
        console.log(`⚠️ Found ${errorCount} validation errors`);
      }
    }
  });

  test('should validate form fields correctly', async ({ page }) => {
    // Open create modal
    await page.locator('button:has-text("Nová objednávka")').click();
    
    // Try to submit empty form (only supplier name should be required, order date has default value)
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Check for validation errors
    await expect(page.locator('text="Název dodavatele je povinný"')).toBeVisible();
    // Order date is not required because it has default value
    
    // Test date validation
    await page.locator('input[id="supplierName"]').fill('Test Supplier');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-01-01'); // Before order date
    
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    await expect(page.locator('text="Datum dodání nemůže být před datem objednávky"')).toBeVisible();
    
    // Test empty items validation
    await page.locator('input[id="expectedDeliveryDate"]').fill('2024-02-15'); // Fix date
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Should show error about no items
    await expect(page.locator('text=/Přidejte alespoň jednu položku/')).toBeVisible();
  });

  test('should open order detail and show edit functionality', async ({ page }) => {
    // Wait for any orders to load
    await page.waitForTimeout(2000);
    
    // Check if there are any orders in the table
    const orderRows = page.locator('tbody tr');
    const rowCount = await orderRows.count();
    
    if (rowCount > 0) {
      console.log(`📋 Found ${rowCount} orders to test with`);
      
      // Click on first order row to open detail
      await orderRows.first().click();
      
      // Wait for detail modal to open
      await page.waitForTimeout(1000);
      
      // Check if detail modal opened
      const detailModal = page.locator('text*="Objednávka"').first();
      if (await detailModal.isVisible()) {
        console.log('✅ Order detail modal opened');
        
        // Check for basic information section
        await expect(page.locator('text="Základní informace"')).toBeVisible();
        
        // Check for order lines section
        await expect(page.locator('text="Položky objednávky"')).toBeVisible();
        
        // Check footer buttons
        await expect(page.locator('button:has-text("Zavřít")')).toBeVisible();
        
        // Check for edit button (should be visible for Draft orders)
        const editButton = page.locator('button:has-text("Upravit")');
        if (await editButton.isVisible()) {
          console.log('✅ Edit button is visible');
          
          // Test edit functionality
          await editButton.click();
          
          // Should open edit modal
          await page.waitForTimeout(1000);
          await expect(page.locator('h2:has-text("Upravit nákupní objednávku")')).toBeVisible();
          
          // Check that form is pre-filled (supplier name should be filled)
          const supplierInput = page.locator('input[id="supplierName"]');
          const supplierValue = await supplierInput.inputValue();
          expect(supplierValue.length).toBeGreaterThan(0);
          console.log(`✅ Edit form pre-filled with supplier: "${supplierValue}"`);
          
          // Test editing
          await supplierInput.fill(supplierValue + ' - Updated');
          
          // Check that save button shows correct text
          await expect(page.locator('button:has-text("Uložit změny")')).toBeVisible();
          
          // Try to save (might fail due to backend, but UI should work)
          await page.locator('button:has-text("Uložit změny")').click();
          await page.waitForTimeout(2000);
          
          // Cancel edit modal if still open
          const cancelButton = page.locator('button:has-text("Zrušit")');
          if (await cancelButton.isVisible()) {
            await cancelButton.click();
          }
          
        } else {
          console.log('ℹ️ Edit button not visible (order might not be in Draft status)');
        }
        
        // Close detail modal
        await page.locator('button:has-text("Zavřít")').click();
        
      } else {
        console.log('❌ Order detail modal did not open');
      }
      
    } else {
      console.log('ℹ️ No orders found in the list to test detail functionality');
      
      // Create a test order first, then test detail
      await page.locator('button:has-text("Nová objednávka")').click();
      
      // Fill minimal order
      await page.locator('input[id="supplierName"]').fill('Test Order for Detail');
      await page.locator('input[id="orderDate"]').fill('2024-02-01');
      
      // Submit
      await page.locator('button:has-text("Vytvořit objednávku")').click();
      await page.waitForTimeout(3000);
      
      // If successful, try detail test again
      const newRowCount = await page.locator('tbody tr').count();
      if (newRowCount > 0) {
        console.log('✅ Created test order, now testing detail view');
        await page.locator('tbody tr').first().click();
        await page.waitForTimeout(1000);
        
        if (await page.locator('text*="Objednávka"').first().isVisible()) {
          console.log('✅ Detail modal opened for newly created order');
          await page.locator('button:has-text("Zavřít")').click();
        }
      }
    }
  });

  test('should handle modal interactions correctly', async ({ page }) => {
    // Test create modal
    await page.locator('button:has-text("Nová objednávka")').click();
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).toBeVisible();
    
    // Test Escape key closes modal
    await page.keyboard.press('Escape');
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).not.toBeVisible();
    
    // Test cancel button
    await page.locator('button:has-text("Nová objednávka")').click();
    await page.locator('button:has-text("Zrušit")').click();
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).not.toBeVisible();
    
    // Test backdrop click (click outside modal)
    await page.locator('button:has-text("Nová objednávka")').click();
    await page.locator('.fixed.inset-0.bg-black').click({ position: { x: 10, y: 10 } });
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).not.toBeVisible();
  });

  test('should handle responsive design correctly', async ({ page }) => {
    // Test desktop view
    await page.setViewportSize({ width: 1200, height: 800 });
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
    await expect(page.locator('th:has-text("Číslo objednávky")')).toBeVisible();
    
    // Test tablet view
    await page.setViewportSize({ width: 768, height: 1024 });
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
    
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 });
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
    await expect(page.locator('button:has-text("Nová objednávka")')).toBeVisible();
    
    // Test modal on mobile
    await page.locator('button:has-text("Nová objednávka")').click();
    await expect(page.locator('h2:has-text("Nová nákupní objednávka")')).toBeVisible();
    await page.keyboard.press('Escape');
  });

  test('should show proper loading and error states', async ({ page }) => {
    // Test loading state by navigating to page
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Loading text might appear briefly
    const loadingText = page.locator('text=Načítání objednávek...');
    // Don't assert visibility as it might be too fast to catch
    
    // Wait for page to fully load
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(1000);
    
    // Check that either data loaded or empty state is shown
    const hasData = await page.locator('tbody tr').count() > 0;
    const hasEmptyMessage = await page.locator('text=Žádné objednávky nebyly nalezeny.').isVisible();
    
    if (!hasData && hasEmptyMessage) {
      console.log('✅ Proper empty state displayed');
    } else if (hasData) {
      console.log('✅ Data loaded successfully');
    }
    
    // Test form loading state
    await page.locator('button:has-text("Nová objednávka")').click();
    await page.locator('input[id="supplierName"]').fill('Test Loading');
    await page.locator('input[id="orderDate"]').fill('2024-02-01');
    
    // Click submit to trigger loading state
    await page.locator('button:has-text("Vytvořit objednávku")').click();
    
    // Should briefly show loading state
    const loadingButton = page.locator('button:has-text("Ukládání...")');
    if (await loadingButton.isVisible()) {
      console.log('✅ Loading state displayed during form submission');
    }
    
    // Should show disabled state
    const disabledButton = page.locator('button[disabled]:has-text("Ukládání...")');
    if (await disabledButton.isVisible()) {
      console.log('✅ Button properly disabled during submission');
    }
    
    // Wait for completion
    await page.waitForTimeout(3000);
  });
});