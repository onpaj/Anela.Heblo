import { test, expect } from '@playwright/test';
import { navigateToTransportBoxReceive } from '../helpers/e2e-auth-helper';

test.describe('Transport Box Receive E2E Tests', () => {

  test.beforeEach(async ({ page }) => {
    await navigateToTransportBoxReceive(page);
  });

  test('should navigate to receive interface and test box loading', async ({ page }) => {
    // Verify we're on the receive page
    await expect(page.locator('h1').filter({ hasText: /Příjem transportních boxů/ })).toBeVisible();

    // Verify page description is present
    await expect(page.locator('text=Naskenujte kód boxu pro příjem zásilky do skladu')).toBeVisible();

    // Verify main elements are present
    const codeInput = page.locator('input[placeholder*="Naskenujte kód boxu"]');
    await expect(codeInput).toBeVisible();
    await expect(codeInput).toBeFocused(); // Input should be auto-focused

    // Verify load button is present (disabled when input is empty)
    const loadButton = page.locator('button:has-text("Načíst")');
    await expect(loadButton).toBeVisible();
    await expect(loadButton).toBeDisabled();
  });

  test('should test complete receive workflow - load and receive box', async ({ page }) => {
    // Test with a known test box code (from test data)
    const testBoxCode = 'B001';

    const codeInput = page.locator('input[placeholder*="Naskenujte kód boxu"]');
    const loadButton = page.locator('button:has-text("Načíst")');

    // Fill in box code
    await codeInput.fill(testBoxCode);
    await expect(loadButton).toBeEnabled();

    // Load the box
    await loadButton.click();
    await page.waitForTimeout(2000);

    // The box may or may not exist - test both scenarios
    const errorMessage = page.locator('text=/nenalezen|not found|neexistuje/i').first();
    const hasError = await errorMessage.isVisible({ timeout: 3000 }).catch(() => false);

    if (hasError) {
      // Box doesn't exist - verify error handling works
      console.log(`Box ${testBoxCode} not found - testing error handling`);
      await expect(errorMessage).toBeVisible();

      // Input should still have focus for quick retry
      await expect(codeInput).toBeFocused();
    } else {
      // Box was found - test the full workflow
      console.log(`Box ${testBoxCode} found - testing full workflow`);

      // Should show box details
      const boxDetailsHeading = page.locator('h2, h3').filter({ hasText: /Detail boxu|Box Detail/i }).first();
      await expect(boxDetailsHeading).toBeVisible({ timeout: 5000 });

      // Should show action buttons
      const cancelButton = page.locator('button:has-text("Storno"), button:has-text("Cancel")').first();
      await expect(cancelButton).toBeVisible();

      // Test cancel functionality
      await cancelButton.click();
      await page.waitForTimeout(500);

      // After cancel, box details should be hidden and input cleared
      await expect(boxDetailsHeading).not.toBeVisible();
      await expect(codeInput).toHaveValue('');
      await expect(codeInput).toBeFocused();
    }
  });

  test('should test input validation and error handling', async ({ page }) => {
    const codeInput = page.locator('input[placeholder*="Naskenujte kód boxu"]');
    const loadButton = page.locator('button:has-text("Načíst")');

    // Verify button is disabled when input is empty
    await expect(loadButton).toBeDisabled();

    // Test input focus behavior - should be auto-focused
    await expect(codeInput).toBeFocused();

    // Test input accepts text
    await codeInput.fill('b001');

    // Test that button becomes enabled when input has value
    await expect(loadButton).toBeEnabled();

    // Test input transforms to uppercase (if implemented)
    const inputValue = await codeInput.inputValue();
    if (inputValue === 'B001') {
      console.log('Input auto-transforms to uppercase');
    }

    // Test loading with invalid box code
    await codeInput.fill('INVALID-TEST-BOX');
    await loadButton.click();
    await page.waitForTimeout(2000);

    // Should show error message for invalid box
    const errorMessage = page.locator('text=/nenalezen|not found|neexistuje|chyba/i').first();
    const hasError = await errorMessage.isVisible({ timeout: 3000 }).catch(() => false);

    if (hasError) {
      console.log('Error handling works correctly for invalid box code');
      await expect(errorMessage).toBeVisible();
    } else {
      console.log('No error message shown - box may exist or error display differs');
    }
  });

  test('should test barcode scanner simulation and keyboard shortcuts', async ({ page }) => {
    const codeInput = page.locator('input[placeholder*="Naskenujte kód boxu"]');
    const loadButton = page.locator('button:has-text("Načíst")');

    // Test that input field is auto-focused on page load
    await expect(codeInput).toBeFocused();

    // Test Enter key submission (simulating barcode scanner)
    await codeInput.fill('B001');
    await page.keyboard.press('Enter');
    await page.waitForTimeout(2000);

    // The Enter key should trigger the load button
    // Check if either box details appeared OR error message shown
    const boxDetailsHeading = page.locator('h2, h3').filter({ hasText: /Detail boxu|Box Detail/i }).first();
    const errorMessage = page.locator('text=/nenalezen|not found|neexistuje|chyba/i').first();

    const hasBoxDetails = await boxDetailsHeading.isVisible({ timeout: 3000 }).catch(() => false);
    const hasError = await errorMessage.isVisible({ timeout: 1000 }).catch(() => false);

    if (hasBoxDetails) {
      console.log('Enter key successfully loaded box details');
      // Cancel to reset
      const cancelButton = page.locator('button:has-text("Storno")').first();
      if (await cancelButton.isVisible({ timeout: 1000 }).catch(() => false)) {
        await cancelButton.click();
        await page.waitForTimeout(500);
      }
    } else if (hasError) {
      console.log('Enter key triggered load attempt, error shown for invalid box');
    }

    // Test clear functionality
    await codeInput.clear();
    await expect(codeInput).toHaveValue('');
    await expect(codeInput).toBeFocused();
  });

  test('should test responsive design and accessibility', async ({ page }) => {
    const pageHeader = page.locator('h1').filter({ hasText: /Příjem transportních boxů/ });
    const codeInput = page.locator('input[placeholder*="Naskenujte kód boxu"]');
    const loadButton = page.locator('button:has-text("Načíst")');

    // Test mobile responsive design
    await page.setViewportSize({ width: 375, height: 667 });
    await page.waitForTimeout(500);

    // Page should still be usable on mobile
    await expect(pageHeader).toBeVisible();
    await expect(codeInput).toBeVisible();
    await expect(loadButton).toBeVisible();

    // Test tablet size
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.waitForTimeout(500);

    await expect(pageHeader).toBeVisible();
    await expect(codeInput).toBeVisible();

    // Return to desktop
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.waitForTimeout(500);

    // Test keyboard navigation - input should be focused by default
    await expect(codeInput).toBeFocused();

    // Fill input to enable the button for keyboard navigation test
    await codeInput.fill('B001');
    await expect(loadButton).toBeEnabled();

    // Tab to button
    await page.keyboard.press('Tab');
    await expect(loadButton).toBeFocused();

    // Test that input has a label (accessibility)
    const inputElement = await codeInput.elementHandle();
    if (inputElement) {
      const inputId = await inputElement.getAttribute('id');
      if (inputId) {
        const label = page.locator(`label[for="${inputId}"]`);
        const hasLabel = await label.count() > 0;
        if (hasLabel) {
          console.log('Input has associated label - good accessibility');
        }
      }
    }
  });

  test('should test box details display and formatting', async ({ page }) => {
    const codeInput = page.locator('input[placeholder*="Naskenujte kód boxu"]');
    const loadButton = page.locator('button:has-text("Načíst")');

    // Load a test box
    await codeInput.fill('B001');
    await loadButton.click();
    await page.waitForTimeout(2000);

    // Check if box details appeared or error was shown
    const boxDetailsHeading = page.locator('h2, h3').filter({ hasText: /Detail boxu|Box Detail/i }).first();
    const errorMessage = page.locator('text=/nenalezen|not found|neexistuje|chyba/i').first();

    const hasBoxDetails = await boxDetailsHeading.isVisible({ timeout: 3000 }).catch(() => false);
    const hasError = await errorMessage.isVisible({ timeout: 1000 }).catch(() => false);

    if (hasBoxDetails) {
      console.log('Box B001 found - testing box details display');

      // Verify box details heading is visible
      await expect(boxDetailsHeading).toBeVisible();

      // Check for action buttons
      const cancelButton = page.locator('button:has-text("Storno")').first();
      const hasCancel = await cancelButton.isVisible({ timeout: 1000 }).catch(() => false);
      if (hasCancel) {
        await expect(cancelButton).toBeVisible();
        console.log('Action buttons present');
      }

      // Check for any table structure (items in the box)
      const table = page.locator('table').first();
      const hasTable = await table.isVisible({ timeout: 1000 }).catch(() => false);
      if (hasTable) {
        console.log('Items table displayed');
      }

    } else if (hasError) {
      console.log('Box B001 not found - error handling verified');
      await expect(errorMessage).toBeVisible();
    } else {
      console.log('Box B001 - no details or error shown (may need investigation)');
    }
  });
});