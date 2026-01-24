import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToApp } from './helpers/e2e-auth-helper';

test.describe('Date Handling Timezone Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Create E2E authentication session before each test
    await createE2EAuthSession(page);
    
    // Navigate to application with proper authentication
    await navigateToApp(page);
    
    // Wait for the page to load completely
    await page.waitForLoadState('networkidle');
  });

  test('should handle date inputs without timezone shifts', async ({ page, browserName }) => {
    test.setTimeout(90000); // Increase timeout for this test
    
    console.log('🔍 Starting date input timezone test...');
    
    try {
      // Navigate to transport boxes via Sklad navigation (known working path)
      console.log('📍 Navigating via Sklad menu...');
      
      // Wait for sidebar to be loaded
      await page.waitForSelector('nav', { timeout: 10000 });
      
      // Click on Sklad menu item (working navigation pattern)
      const skladSelector = page.locator('button').filter({ hasText: 'Sklad' }).first();
      if (await skladSelector.isVisible({ timeout: 5000 })) {
        console.log('✅ Found Sklad menu, clicking...');
        await skladSelector.click();
        await page.waitForTimeout(1000);
        
        // Click on Transportní boxy submenu
        const transportSelector = page.locator('a').filter({ hasText: 'Transportní boxy' }).first();
        if (await transportSelector.isVisible({ timeout: 5000 })) {
          console.log('✅ Found Transportní boxy submenu, clicking...');
          await transportSelector.click();
          await page.waitForLoadState('networkidle');
        } else {
          console.log('⚠️ Transportní boxy not found, trying direct navigation...');
          await page.goto('/transport-boxes');
          await page.waitForLoadState('networkidle');
        }
      } else {
        console.log('⚠️ Sklad not found, trying direct navigation...');
        await page.goto('/transport-boxes');
        await page.waitForLoadState('networkidle');
      }
      
      // Look for transport box rows in the table
      console.log('🔍 Looking for transport box rows...');

      // Wait for table to be visible
      await page.waitForSelector('table', { timeout: 10000 });

      // Find table rows (transport boxes)
      const itemRows = page.locator('tbody tr');
      const rowCount = await itemRows.count();

      console.log(`📊 Found ${rowCount} transport box rows`);

      // Test should FAIL if no rows found, not skip
      expect(rowCount).toBeGreaterThan(0);
      
      // Click on the first item to open modal or detail view
      console.log('🔄 Clicking on first item...');
      await itemRows.first().click();
      
      // Wait for modal or detail view to open with multiple selectors
      console.log('⏳ Waiting for modal/detail view...');
      
      const modalSelectors = [
        '[data-testid="manufacture-inventory-modal"]',
        '[role="dialog"]',
        '.modal',
        '[data-testid*="modal"]',
        '.overlay'
      ];
      
      let modalFound = false;
      for (const selector of modalSelectors) {
        try {
          await page.waitForSelector(selector, { timeout: 5000 });
          console.log(`✅ Modal found with selector: ${selector}`);
          modalFound = true;
          break;
        } catch (e) {
          continue;
        }
      }
      
      if (!modalFound) {
        console.log('⚠️ No modal opened, checking for date inputs on main page...');
      }
      
      // Look for date inputs
      console.log('🔍 Looking for date inputs...');
      const dateInputs = page.locator('input[type="date"]');
      let dateInputCount = await dateInputs.count();
      
      if (dateInputCount === 0) {
        // Try to add a new lot or find other ways to get date inputs
        console.log('🔄 Trying to find add lot button...');
        const addButtons = [
          'button:has-text("Přidat šarži")',
          'button:has-text("Přidat")',
          'button:has-text("Add")',
          '[data-testid*="add"]'
        ];
        
        let addButtonFound = false;
        for (const buttonSelector of addButtons) {
          const button = page.locator(buttonSelector);
          if (await button.isVisible({ timeout: 2000 })) {
            console.log(`✅ Found add button: ${buttonSelector}`);
            await button.click();
            await page.waitForTimeout(2000);
            addButtonFound = true;
            break;
          }
        }

        if (!addButtonFound) {
          console.log('❌ No add button found - test will fail');
        }

        // Check again for date inputs after clicking add
        dateInputCount = await dateInputs.count();
      }

      // Test should FAIL if no date inputs found, not skip
      expect(dateInputCount, 'Expected to find date input fields').toBeGreaterThan(0);
      }
    } catch (error) {
      console.log(`❌ Error during setup: ${error.message}`);
      throw new Error(`Test setup failed - unable to navigate to date inputs: ${error.message}`);
    }
    
    // Test date input handling
    console.log('📅 Testing date input handling...');
    const firstDateInput = page.locator('input[type="date"]').first();
    
    // Test with a specific date that could be problematic across timezones
    const testDate = '2024-03-31'; // DST transition date in Europe
    
    console.log(`🔄 Testing with date: ${testDate}`);
    
    // Clear and set the date
    await firstDateInput.fill('');
    await firstDateInput.fill(testDate);
    
    // Verify the date is set correctly (should not shift due to timezone)
    const inputValue = await firstDateInput.inputValue();
    console.log(`📝 Input value after setting: ${inputValue}`);
    expect(inputValue).toBe(testDate);
    
    // Test with different problematic dates
    const problematicDates = [
      '2024-01-01', // New Year
      '2024-12-31', // New Year's Eve
      '2024-06-21', // Summer solstice
      '2024-10-27', // DST end in Europe
    ];
    
    console.log('🔄 Testing problematic dates...');
    for (const date of problematicDates) {
      console.log(`📅 Testing date: ${date}`);
      await firstDateInput.fill('');
      await firstDateInput.fill(date);
      
      const value = await firstDateInput.inputValue();
      console.log(`📝 Value for ${date}: ${value}`);
      expect(value).toBe(date);
      
      // Wait a bit to ensure no async operations are interfering
      await page.waitForTimeout(200);
    }
    
    // Try to close modal with multiple selectors
    console.log('🔄 Closing modal...');
    const closeSelectors = [
      '[data-testid="close-modal"]',
      'button[aria-label="Close"]',
      '[aria-label="Close"]',
      '.modal-close',
      'button:has-text("Zavřít")',
      'button:has-text("Close")',
      '[data-testid*="close"]'
    ];
    
    let modalClosed = false;
    for (const selector of closeSelectors) {
      const closeButton = page.locator(selector);
      if (await closeButton.isVisible({ timeout: 1000 })) {
        console.log(`✅ Found close button: ${selector}`);
        await closeButton.click();
        modalClosed = true;
        break;
      }
    }
    
    if (!modalClosed) {
      console.log('⚠️ No close button found, pressing Escape key');
      await page.keyboard.press('Escape');
    }
    
    console.log('✅ Date input timezone test completed successfully!');
  });

  test('should display dates consistently across page refreshes', async ({ page }) => {
    test.setTimeout(60000);
    
    console.log('🔄 Testing date consistency across page refreshes...');
    
    try {
      // Navigate to manufacture inventory page
      console.log('📍 Navigating to inventory page...');
      await page.goto('/manufacture-inventory');
      await page.waitForLoadState('networkidle');
      
      // Find any item with date data
      console.log('🔍 Looking for items with date displays...');
      
      const dateSelectors = [
        '[data-testid="expiration-display"]',
        '[data-testid*="date"]',
        'td',
        'span',
        '.date-display'
      ];
      
      let itemWithDate = null;
      for (const selector of dateSelectors) {
        const elements = page.locator(selector);
        const count = await elements.count();
        if (count > 0) {
          console.log(`📊 Found ${count} elements with selector: ${selector}, checking for dates...`);
          
          // Check if any of these elements actually contain date-like text
          for (let i = 0; i < Math.min(count, 10); i++) {
            const element = elements.nth(i);
            const text = await element.textContent();
            if (text && text.match(/\d{1,2}[./-]\d{1,2}[./-]\d{4}/)) {
              console.log(`✅ Found date element with text: "${text}"`);
              itemWithDate = element;
              break;
            }
          }
          
          if (itemWithDate) break;
        }
      }


      // Test should FAIL if no date elements found, not skip
      expect(itemWithDate, 'Expected to find at least one element with date text').toBeTruthy();
      await expect(itemWithDate!).toBeVisible({ timeout: 5000 });
      
      // Get the displayed date before refresh
      const dateBeforeRefresh = await itemWithDate.textContent();
      console.log(`📅 Date before refresh: ${dateBeforeRefresh}`);
      
      // Refresh the page
      console.log('🔄 Refreshing page...');
      await page.reload();
      await page.waitForLoadState('networkidle');
      
      // Find the same element again
      let itemWithDateAfter = null;
      for (const selector of dateSelectors) {
        const elements = page.locator(selector);
        const count = await elements.count();
        if (count > 0) {
          itemWithDateAfter = elements.first();
          break;
        }
      }


      // Test should FAIL if element not found after refresh
      expect(itemWithDateAfter, 'Expected to find date element after refresh').toBeTruthy();
      
      // Get the displayed date after refresh
      const dateAfterRefresh = await itemWithDateAfter.textContent();
      console.log(`📅 Date after refresh: ${dateAfterRefresh}`);
      
      // Dates should be identical (no timezone shifts)
      expect(dateAfterRefresh).toBe(dateBeforeRefresh);
      console.log('✅ Date consistency test passed!');
      
    } catch (error) {
      console.log(`❌ Error during date consistency test: ${error.message}`);
      throw new Error(`Date consistency test failed: ${error.message}`);
    }
  });

  test('should handle date formatting consistently', async ({ page }) => {
    test.setTimeout(45000);
    
    console.log('📝 Testing date formatting consistency...');
    
    try {
      // Test date formatting by examining displayed dates
      console.log('📍 Navigating to inventory page...');
      await page.goto('/manufacture-inventory');
      await page.waitForLoadState('networkidle');
      
      // Look for any displayed dates and verify they follow expected format
      console.log('🔍 Looking for date elements...');
      
      const dateElementSelectors = [
        '[data-testid*="date"]',
        '[data-testid*="expiration"]',
        'td',
        'span',
        '.date'
      ];
      
      let dateElements = null;
      let count = 0;
      
      for (const selector of dateElementSelectors) {
        const elements = page.locator(selector);
        const elementCount = await elements.count();
        if (elementCount > 0) {
          console.log(`📊 Found ${elementCount} elements with selector: ${selector}, filtering for dates...`);
          
          // Filter elements that actually contain date-like text
          const dateFilteredElements = [];
          for (let i = 0; i < Math.min(elementCount, 20); i++) {
            const element = elements.nth(i);
            const text = await element.textContent();
            if (text && text.match(/\d{1,2}[./-]\d{1,2}[./-]\d{4}/)) {
              dateFilteredElements.push(element);
            }
          }
          
          if (dateFilteredElements.length > 0) {
            console.log(`✅ Found ${dateFilteredElements.length} actual date elements`);
            count = dateFilteredElements.length;
            dateElements = dateFilteredElements;
            break;
          }
        }
      }


      // Test should FAIL if no date elements found, not skip
      expect(count, 'Expected to find at least one date element').toBeGreaterThan(0);
      
      // Check that dates are in expected format (YYYY-MM-DD or localized format)
      console.log(`📊 Checking format of ${Math.min(count, 5)} date elements...`);
      
      for (let i = 0; i < Math.min(count, 5); i++) {
        const element = dateElements[i];
        const dateText = await element.textContent();
        console.log(`📅 Date ${i + 1}: "${dateText}"`);
        
        if (dateText && dateText.trim() !== '') {
          // Should not contain timezone indicators or time components for date-only fields
          console.log(`🔍 Checking for timezone indicators in: "${dateText}"`);
          expect(dateText).not.toMatch(/GMT|UTC|\+\d{2}:\d{2}|T\d{2}:\d{2}/);
          
          // Should be a reasonable date format
          console.log(`🔍 Checking date format for: "${dateText}"`);
          const dateFormatRegex = /\d{1,2}[./-]\d{1,2}[./-]\d{4}|\d{4}[./-]\d{1,2}[./-]\d{1,2}/;
          
          if (!dateFormatRegex.test(dateText)) {
            console.log(`⚠️ Date "${dateText}" does not match expected format`);
            // Log but don't fail if it's just empty or loading text
            if (!dateText.match(/loading|načítání|--/i)) {
              expect(dateText).toMatch(dateFormatRegex);
            }
          } else {
            console.log(`✅ Date "${dateText}" has valid format`);
          }
        }
      }
      
      console.log('✅ Date formatting test completed successfully!');
      
    } catch (error) {
      console.log(`❌ Error during date formatting test: ${error.message}`);
      throw new Error(`Date formatting test failed: ${error.message}`);
    }
  });
});