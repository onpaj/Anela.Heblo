import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Manufacture Order Creation', () => {
  test.beforeEach(async ({ page }) => {
    console.log('🏭 Starting manufacture order creation test setup...');
    
    try {
      // Create E2E authentication session before each test
      console.log('🔐 Creating E2E authentication session...');
      await createE2EAuthSession(page);
      
      // Navigate to application
      console.log('🚀 Navigating to application...');
      await navigateToApp(page);
      
      // Wait for app to load
      await page.waitForLoadState('networkidle');
      await page.waitForTimeout(3000); // Give extra time for React components to initialize
      
      console.log('✅ Manufacture order test setup completed successfully');
    } catch (error) {
      console.log(`❌ Setup failed: ${error.message}`);
      throw error;
    }
  });

  test('should create manufacture order through batch calculator workflow', async ({ page }) => {
    console.log('📍 Test: Create manufacture order via batch calculator');
    
    // Step 1: Navigate to Batch Planning Calculator (Kalkulačka dávek) via sidebar
    console.log('🔄 Navigating to Batch Planning Calculator...');
    
    // Click on "Výroba" section first
    await page.getByRole('button', { name: 'Výroba' }).click();
    console.log('✅ Clicked Výroba section');
    
    // Then click on "Kalkulačka dávek" link
    await page.getByRole('link', { name: 'Kalkulačka dávek' }).click();
    console.log('✅ Clicked Kalkulačka dávek link');
    
    // Wait for the batch calculator page to load
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000); // Give time for React components to initialize
    
    // Step 2: Verify we're on the batch calculator page and enter product code
    console.log('🔍 Verifying batch calculator page loaded...');
    const pageTitle = page.locator('h1').filter({ hasText: /Kalkulačka dávek pro výrobu/ });
    await expect(pageTitle.first()).toBeVisible({ timeout: 10000 });
    console.log('✅ Batch calculator page loaded successfully');
    
    console.log('🎯 Entering product code DEO001001M...');
    
    // Click on the combobox (react-select)
    const productCombobox = page.getByRole('combobox').or(
      page.locator('.css-18w4uv4').or(
        page.getByPlaceholder(/vyhledejte polotovar/i)
      )
    );
    
    await expect(productCombobox.first()).toBeVisible({ timeout: 10000 });
    await productCombobox.first().click();
    console.log('✅ Opened product selector dropdown');
    
    // Fill the react-select input with product code (fallback to specific ID)
    const productInput = page.locator('#react-select-2-input').or(
      page.locator('input[placeholder*="polotovar"]')
    );
    
    await productInput.fill('DEO001001M');
    console.log('✅ Entered product code DEO001001M');
    
    // Wait for autocomplete options to appear
    await page.waitForTimeout(1500);
    
    // Try to select first autocomplete option if available, otherwise press Enter
    const firstOption = page.locator('[role="option"]').first();
    if (await firstOption.isVisible({ timeout: 2000 })) {
      await firstOption.click();
      console.log('✅ Selected first autocomplete option');
    } else {
      // Fallback to pressing Enter
      await page.locator('#react-select-2-input').press('Enter');
      console.log('✅ Pressed Enter to select product');
    }
    
    // Step 3: Set batch size to 10000g
    console.log('⚖️ Setting batch size to 10000g...');
    
    // Click on the placeholder input field
    await page.getByPlaceholder('0.00').click();
    
    // Fill with batch size
    await page.getByPlaceholder('0.00').fill('10000');
    console.log('✅ Set batch size to 10000g');
    
    // Press Enter to confirm
    await page.getByPlaceholder('0.00').press('Enter');
    
    // Wait for any processing
    await page.waitForTimeout(1000);
    
    // Step 4: Click "Přejít na plánování výroby" button
    console.log('🔄 Navigating to production planning...');
    
    await page.getByRole('button', { name: 'Přejít na plánování výroby' }).click();
    console.log('✅ Clicked production planning button');
    
    // Wait for navigation to planning page
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
    
    // Step 7: Verify we are on the "Plánování dávek" tab
    console.log('📋 Verifying we are on planning tab...');
    await expect(page.getByText(/plánování dávek|batch planning/i)).toBeVisible({ timeout: 10000 });
    console.log('✅ Successfully navigated to batch planning tab');
    
    // Step 8: Click "Vytvořit zakázku" button
    console.log('🏭 Creating manufacture order...');
    
    const createOrderButton = page.locator('button').filter({ hasText: /vytvořit zakázku|create order|create manufacture/i });
    await expect(createOrderButton).toBeVisible({ timeout: 10000 });
    await createOrderButton.click();
    console.log('✅ Clicked create order button');
    
    // Wait for modal to potentially appear
    await page.waitForTimeout(3000);
    
    // Step 9: Validate that manufacture order detail modal opens
    // This assertion will fail until the bug is fixed
    console.log('🔍 Checking for manufacture order modal...');
    
    const modal = page.getByRole('dialog').or(
      page.locator('[role="modal"]').or(
        page.locator('.modal').or(
          page.locator('[data-testid="modal"]').or(
            page.locator('.modal-overlay')
          )
        )
      )
    );
    
    await expect(modal).toBeVisible({ timeout: 5000 });
    console.log('✅ Manufacture order modal opened successfully');
    
    // Additional validation that modal contains manufacture order content
    await expect(modal.getByText(/manufacture order|výrobní zakázka|order detail/i)).toBeVisible({ timeout: 3000 });
    console.log('✅ Modal contains manufacture order content');
    
    console.log('🎉 Manufacture order creation test completed successfully!');
    console.log('📝 Test verified full workflow from batch calculator to order creation modal');
  });
});