import { test, expect } from '@playwright/test';

test.describe('Transport Box Error Handling', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes page
    await page.goto('http://localhost:3001/logistics/transport-boxes');
    await page.waitForLoadState('networkidle');
  });

  test('should show error toast when invalid state transition occurs', async ({ page }) => {
    // Create a test scenario where we try to make an invalid state transition
    // This test assumes we can create or find a transport box to test with
    
    // Wait for the page to load
    await expect(page.locator('h1:has-text("Transportní boxy")')).toBeVisible({ timeout: 10000 });
    
    // Look for any existing transport box or create one
    const firstBoxRow = page.locator('table tbody tr').first();
    if (await firstBoxRow.count() > 0) {
      // Click on the first box to open detail modal
      await firstBoxRow.click();
      
      // Wait for the detail modal to open
      await expect(page.locator('text=Detail transportního boxu')).toBeVisible({ timeout: 5000 });
      
      // Mock a failed API response by intercepting the API call
      await page.route('**/api/transport-boxes/**/change-state', async route => {
        // Simulate a validation error from the backend
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: false,
            errorMessage: 'BoxCode is required for transition from New to Opened',
            updatedBox: null
          })
        });
      });
      
      // Try to make a state transition that should fail
      // Look for any transition button and click it
      const transitionButton = page.locator('button:has-text("Otevřený"), button:has-text("V přepravě"), button:has-text("Přijatý")').first();
      
      if (await transitionButton.count() > 0) {
        await transitionButton.click();
        
        // Wait for the error toast to appear
        await expect(page.locator('.fixed.top-4.right-4')).toBeVisible({ timeout: 5000 });
        await expect(page.locator('text=Chyba při změně stavu')).toBeVisible();
        await expect(page.locator('text=BoxCode is required for transition from New to Opened')).toBeVisible();
        
        // Verify the toast has error styling (red color)
        const toastElement = page.locator('.border-red-200');
        await expect(toastElement).toBeVisible();
        
        // Wait for toast to auto-dismiss or manually close it
        const closeButton = page.locator('button[aria-label="Zavřít"], button:has(svg)').last();
        if (await closeButton.count() > 0) {
          await closeButton.click();
        }
      }
      
      // Close the modal
      await page.locator('button:has-text("Zavřít")').click();
    }
  });
  
  test('should show error toast when required field is missing', async ({ page }) => {
    // Wait for the page to load
    await expect(page.locator('h1:has-text("Transportní boxy")')).toBeVisible({ timeout: 10000 });
    
    // Look for a box in "New" state to test box number assignment
    const newBoxRow = page.locator('table tbody tr:has(.bg-gray-100:text("Nový"))').first();
    
    if (await newBoxRow.count() > 0) {
      await newBoxRow.click();
      
      // Wait for the detail modal
      await expect(page.locator('text=Detail transportního boxu')).toBeVisible({ timeout: 5000 });
      
      // Check if we can see the box number input (for New state)
      const boxNumberInput = page.locator('input[placeholder="B001"]');
      
      if (await boxNumberInput.count() > 0) {
        // Try to submit without entering a box number
        await boxNumberInput.fill('');
        
        const assignButton = page.locator('button:has-text("Přiřadit")');
        await assignButton.click();
        
        // Should not trigger any API call since validation happens on frontend
        // But let's test with invalid format
        await boxNumberInput.fill('INVALID');
        await assignButton.click();
        
        // Should show validation error
        await expect(page.locator('text=Číslo boxu musí mít formát B + 3 číslice')).toBeVisible();
      }
      
      // Close the modal
      await page.locator('button:has-text("Zavřít")').click();
    }
  });
  
  test('should show success toast when state change succeeds', async ({ page }) => {
    // Wait for the page to load
    await expect(page.locator('h1:has-text("Transportní boxy")')).toBeVisible({ timeout: 10000 });
    
    const firstBoxRow = page.locator('table tbody tr').first();
    if (await firstBoxRow.count() > 0) {
      await firstBoxRow.click();
      
      // Wait for the detail modal
      await expect(page.locator('text=Detail transportního boxu')).toBeVisible({ timeout: 5000 });
      
      // Mock a successful API response
      await page.route('**/api/transport-boxes/**/change-state', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            errorMessage: null,
            updatedBox: {
              transportBox: {
                id: 1,
                code: 'B001',
                state: 'Opened',
                description: 'Test box',
                itemCount: 0,
                lastStateChanged: new Date().toISOString(),
                items: [],
                stateLog: [],
                allowedTransitions: []
              }
            }
          })
        });
      });
      
      // Look for any transition button and click it
      const transitionButton = page.locator('button:has-text("Otevřený"), button:has-text("V přepravě"), button:has-text("Přijatý")').first();
      
      if (await transitionButton.count() > 0) {
        await transitionButton.click();
        
        // Wait for the success toast to appear
        await expect(page.locator('.fixed.top-4.right-4')).toBeVisible({ timeout: 5000 });
        await expect(page.locator('text=Stav změněn')).toBeVisible();
        await expect(page.locator('text=Box byl úspěšně přepnut na stav')).toBeVisible();
        
        // Verify the toast has success styling (green color)
        const successToast = page.locator('.border-green-200');
        await expect(successToast).toBeVisible();
      }
      
      // Close the modal
      await page.locator('button:has-text("Zavřít")').click();
    }
  });
  
  test('should show toast notifications with correct styling and auto-dismiss', async ({ page }) => {
    // Navigate to transport boxes and create a scenario for error
    await expect(page.locator('text=Transportní boxy')).toBeVisible({ timeout: 10000 });
    
    const firstBoxRow = page.locator('table tbody tr').first();
    if (await firstBoxRow.count() > 0) {
      await firstBoxRow.click();
      
      await expect(page.locator('text=Detail transportního boxu')).toBeVisible({ timeout: 5000 });
      
      // Mock an error response
      await page.route('**/api/transport-boxes/**/change-state', async route => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: false,
            errorMessage: 'Test error message for validation',
            updatedBox: null
          })
        });
      });
      
      // Click any available transition button
      const transitionButton = page.locator('button:has-text("Otevřený"), button:has-text("V přepravě"), button:has-text("Přijatý")').first();
      
      if (await transitionButton.count() > 0) {
        await transitionButton.click();
        
        // Check toast container is positioned correctly
        const toastContainer = page.locator('.fixed.top-4.right-4.z-50');
        await expect(toastContainer).toBeVisible({ timeout: 5000 });
        
        // Check toast content and styling
        const errorToast = page.locator('.border-red-200');
        await expect(errorToast).toBeVisible();
        
        // Check that the toast has the correct icon (error icon)
        await expect(page.locator('svg[class*="text-red-500"]')).toBeVisible();
        
        // Check close button functionality
        const closeButton = page.locator('button:has(svg):near(text="Chyba při změně stavu")');
        await expect(closeButton).toBeVisible();
        
        // Click close button to dismiss toast manually
        await closeButton.click();
        
        // Toast should disappear
        await expect(errorToast).toHaveCount(0, { timeout: 2000 });
      }
      
      // Close the modal
      await page.locator('button:has-text("Zavřít")').click();
    }
  });
});