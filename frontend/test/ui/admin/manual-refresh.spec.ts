import { test, expect } from '@playwright/test';

test.describe('Admin Manual Refresh Functionality', () => {
  
  test.beforeEach(async ({ page }) => {
    // Navigate to admin dashboard (root path)
    await page.goto('http://localhost:3001/');
    
    // Wait for the page to load completely
    await page.waitForLoadState('networkidle');
    
    // Switch to manual refresh tab
    await page.click('text=Manuální načítání');
    
    // Wait for the tab content to load
    await expect(page.locator('text=Spustit načítání jednotlivých typů dat z externích zdrojů')).toBeVisible();
  });

  test('should display manual refresh tab and all refresh operations', async ({ page }) => {
    // Verify tab is selected
    await expect(page.locator('[class*="border-indigo-500"]:has-text("Manuální načítání")')).toBeVisible();
    
    // Verify page title and description
    await expect(page.locator('text=Manuální načítání dat')).toBeVisible();
    await expect(page.locator('text=Spustit načítání jednotlivých typů dat z externích zdrojů')).toBeVisible();
    
    // Check that all 12 refresh operations are displayed
    const expectedOperations = [
      'Transport Data',
      'Reserve Data', 
      'Sales Data',
      'Attributes Data',
      'ERP Stock Data',
      'E-shop Stock Data',
      'Purchase History',
      'Consumed History',
      'Stock Taking',
      'Lots Data',
      'E-shop Prices',
      'ERP Prices'
    ];
    
    for (const operation of expectedOperations) {
      await expect(page.locator(`text=${operation}`)).toBeVisible();
    }
    
    // Check that all refresh buttons are present and enabled
    const refreshButtons = page.locator('button:has-text("Načíst")');
    await expect(refreshButtons).toHaveCount(12);
    
    // Verify all buttons are enabled initially
    for (let i = 0; i < 12; i++) {
      await expect(refreshButtons.nth(i)).toBeEnabled();
    }
  });

  test('should show loading state when refresh operation is triggered', async ({ page }) => {
    // Click on the first refresh button (Transport Data)
    const transportButton = page.locator('button:has-text("Načíst")').first();
    
    // Start the refresh operation
    await transportButton.click();
    
    // Check that the button shows loading state
    await expect(page.locator('button:has-text("Načítá...")')).toBeVisible({ timeout: 1000 });
    
    // Check that the loading spinner is visible
    await expect(page.locator('[class*="animate-spin"]')).toBeVisible();
    
    // Wait for the operation to complete (max 10 seconds)
    await expect(page.locator('button:has-text("Načítá...")')).not.toBeVisible({ timeout: 10000 });
  });

  test('should show success message after successful refresh', async ({ page }) => {
    // Click on a refresh button
    const refreshButton = page.locator('button:has-text("Načíst")').first();
    await refreshButton.click();
    
    // Wait for operation to complete
    await expect(page.locator('button:has-text("Načítá...")')).not.toBeVisible({ timeout: 10000 });
    
    // Check for success message
    await expect(page.locator('text=Data úspěšně načtena')).toBeVisible({ timeout: 2000 });
    await expect(page.locator('text=Operace byla dokončena úspěšně.')).toBeVisible();
    
    // Check that success icon is visible
    await expect(page.locator('[class*="text-emerald-500"]')).toBeVisible();
  });

  test('should disable all buttons during refresh operation', async ({ page }) => {
    // Click on first refresh button
    const firstButton = page.locator('button:has-text("Načíst")').first();
    await firstButton.click();
    
    // Check that all other buttons are disabled during operation
    const allButtons = page.locator('button:has-text("Načíst"), button:has-text("Načítá...")');
    const buttonCount = await allButtons.count();
    
    for (let i = 1; i < buttonCount; i++) {
      await expect(allButtons.nth(i)).toBeDisabled();
    }
    
    // Wait for operation to complete
    await expect(page.locator('button:has-text("Načítá...")')).not.toBeVisible({ timeout: 10000 });
    
    // Check that buttons are enabled again
    const enabledButtons = page.locator('button:has-text("Načíst")');
    for (let i = 0; i < await enabledButtons.count(); i++) {
      await expect(enabledButtons.nth(i)).toBeEnabled();
    }
  });

  test('should have proper styling and layout', async ({ page }) => {
    // Check responsive grid layout
    const grid = page.locator('[class*="grid-cols-1"][class*="sm:grid-cols-2"][class*="lg:grid-cols-3"][class*="xl:grid-cols-4"]');
    await expect(grid).toBeVisible();
    
    // Check operation cards have proper styling
    const operationCards = page.locator('[class*="bg-gray-50"][class*="p-4"][class*="rounded-lg"]');
    await expect(operationCards).toHaveCount(12);
    
    // Check that each card has title, description, and button
    for (let i = 0; i < 12; i++) {
      const card = operationCards.nth(i);
      await expect(card.locator('[class*="font-medium"][class*="text-gray-900"]')).toBeVisible();
      await expect(card.locator('[class*="text-xs"][class*="text-gray-500"]')).toBeVisible();
      await expect(card.locator('button')).toBeVisible();
    }
  });

  test('should be able to switch back to other tabs', async ({ page }) => {
    // Verify we're on manual refresh tab
    await expect(page.locator('[class*="border-indigo-500"]:has-text("Manuální načítání")')).toBeVisible();
    
    // Switch to overview tab
    await page.click('text=Přehled');
    await expect(page.locator('[class*="border-indigo-500"]:has-text("Přehled")')).toBeVisible();
    
    // Check that manual refresh content is hidden
    await expect(page.locator('text=Spustit načítání jednotlivých typů dat z externích zdrojů')).not.toBeVisible();
    
    // Switch to audit logs tab
    await page.click('text=Audit logy');
    await expect(page.locator('[class*="border-indigo-500"]:has-text("Audit logy")')).toBeVisible();
    
    // Switch back to manual refresh tab
    await page.click('text=Manuální načítání');
    await expect(page.locator('[class*="border-indigo-500"]:has-text("Manuální načítání")')).toBeVisible();
    await expect(page.locator('text=Spustit načítání jednotlivých typů dat z externích zdrojů')).toBeVisible();
  });

  test('should handle multiple operations sequentially', async ({ page }) => {
    // Test first operation
    const firstButton = page.locator('button:has-text("Načíst")').first();
    await firstButton.click();
    
    // Wait for first operation to complete
    await expect(page.locator('button:has-text("Načítá...")')).not.toBeVisible({ timeout: 10000 });
    await expect(page.locator('text=Data úspěšně načtena')).toBeVisible({ timeout: 2000 });
    
    // Test second operation
    const secondButton = page.locator('button:has-text("Načíst")').nth(1);
    await secondButton.click();
    
    // Wait for second operation to complete
    await expect(page.locator('button:has-text("Načítá...")')).not.toBeVisible({ timeout: 10000 });
    
    // Success message should still be visible
    await expect(page.locator('text=Data úspěšně načtena')).toBeVisible();
  });

});