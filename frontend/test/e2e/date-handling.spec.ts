import { test, expect } from '@playwright/test';

test.describe('Date Handling Timezone Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the application
    await page.goto('/');
    
    // Wait for the page to load completely
    await page.waitForLoadState('networkidle');
    
    // Login or authenticate if needed (skip if already authenticated)
    const isLoggedIn = await page.locator('[data-testid="user-menu"]').isVisible();
    if (!isLoggedIn) {
      await page.click('[data-testid="login-button"]');
      await page.waitForURL('/dashboard');
    }
  });

  test('should handle date inputs without timezone shifts', async ({ page, browserName }) => {
    test.setTimeout(60000); // Increase timeout for this test
    
    // Navigate to manufacture inventory page
    await page.click('a[href="/manufacture-inventory"]');
    await page.waitForLoadState('networkidle');
    
    // Look for any item with expiration date functionality
    const itemRows = page.locator('[data-testid="catalog-item-row"]');
    const itemCount = await itemRows.count();
    
    if (itemCount === 0) {
      test.skip('No catalog items available for testing date handling');
    }
    
    // Click on the first item to open modal
    await itemRows.first().click();
    
    // Wait for modal to open
    await page.waitForSelector('[data-testid="manufacture-inventory-modal"]', { timeout: 10000 });
    
    // Look for date inputs in the modal
    const dateInputs = page.locator('input[type="date"]');
    const dateInputCount = await dateInputs.count();
    
    if (dateInputCount === 0) {
      // Try to add a new lot to get date inputs
      const addLotButton = page.locator('button:has-text("Přidat šarži")');
      if (await addLotButton.isVisible()) {
        await addLotButton.click();
        await page.waitForTimeout(1000); // Wait for new lot to be added
      } else {
        test.skip('No date inputs available for testing');
      }
    }
    
    // Test date input handling
    const firstDateInput = page.locator('input[type="date"]').first();
    
    // Test with a specific date that could be problematic across timezones
    const testDate = '2024-03-31'; // DST transition date in Europe
    
    // Clear and set the date
    await firstDateInput.fill('');
    await firstDateInput.fill(testDate);
    
    // Verify the date is set correctly (should not shift due to timezone)
    const inputValue = await firstDateInput.inputValue();
    expect(inputValue).toBe(testDate);
    
    // Test with different problematic dates
    const problematicDates = [
      '2024-01-01', // New Year
      '2024-12-31', // New Year's Eve
      '2024-06-21', // Summer solstice
      '2024-10-27', // DST end in Europe
    ];
    
    for (const date of problematicDates) {
      await firstDateInput.fill('');
      await firstDateInput.fill(date);
      
      const value = await firstDateInput.inputValue();
      expect(value).toBe(date);
      
      // Wait a bit to ensure no async operations are interfering
      await page.waitForTimeout(100);
    }
    
    // Close modal
    await page.click('[data-testid="close-modal"]');
  });

  test('should display dates consistently across page refreshes', async ({ page }) => {
    test.setTimeout(45000);
    
    // Navigate to manufacture inventory page
    await page.click('a[href="/manufacture-inventory"]');
    await page.waitForLoadState('networkidle');
    
    // Find an item with expiration data (if any)
    const itemWithExpiration = page.locator('[data-testid="expiration-display"]').first();
    
    if (!(await itemWithExpiration.isVisible())) {
      test.skip('No items with expiration dates found for consistency testing');
    }
    
    // Get the displayed date before refresh
    const dateBeforeRefresh = await itemWithExpiration.textContent();
    
    // Refresh the page
    await page.reload();
    await page.waitForLoadState('networkidle');
    
    // Get the displayed date after refresh
    const dateAfterRefresh = await page.locator('[data-testid="expiration-display"]').first().textContent();
    
    // Dates should be identical (no timezone shifts)
    expect(dateAfterRefresh).toBe(dateBeforeRefresh);
  });

  test('should handle date formatting consistently', async ({ page }) => {
    test.setTimeout(30000);
    
    // Test date formatting by examining displayed dates
    await page.goto('/manufacture-inventory');
    await page.waitForLoadState('networkidle');
    
    // Look for any displayed dates and verify they follow expected format
    const dateElements = page.locator('[data-testid*="date"], [data-testid*="expiration"]');
    const count = await dateElements.count();
    
    if (count === 0) {
      test.skip('No date elements found for formatting testing');
    }
    
    // Check that dates are in expected format (YYYY-MM-DD or localized format)
    for (let i = 0; i < Math.min(count, 5); i++) {
      const dateText = await dateElements.nth(i).textContent();
      
      if (dateText && dateText.trim() !== '') {
        // Should not contain timezone indicators or time components for date-only fields
        expect(dateText).not.toMatch(/GMT|UTC|\+\d{2}:\d{2}|T\d{2}:\d{2}/);
        
        // Should be a reasonable date format
        expect(dateText).toMatch(/\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{4}[./-]\d{1,2}[./-]\d{1,2}/);
      }
    }
  });
});