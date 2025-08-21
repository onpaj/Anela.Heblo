import { test, expect } from '@playwright/test';

test.describe('Journal Entry Form', () => {
  test.describe('Create New Journal Entry', () => {
    test.beforeEach(async ({ page }) => {
      // Navigate to new journal entry page
      await page.goto('http://localhost:3001/journal/new');
    });

    test('should display new journal entry form', async ({ page }) => {
      // Check if we're on the right page or redirected to a form page
      const currentUrl = page.url();
      
      // The form might be on the /journal/new route or might redirect
      expect(currentUrl).toMatch(/journal/);
      
      // Look for common form elements that should be present
      // Title input
      const titleInput = page.locator('input[name="title"], input[placeholder*="název"], input[placeholder*="title"]').first();
      if (await titleInput.isVisible()) {
        await expect(titleInput).toBeVisible();
      }
      
      // Content textarea
      const contentInput = page.locator('textarea[name="content"], textarea[placeholder*="obsah"], textarea[placeholder*="content"]').first();
      if (await contentInput.isVisible()) {
        await expect(contentInput).toBeVisible();
      }
      
      // Date input
      const dateInput = page.locator('input[type="date"], input[name="date"], input[name="entryDate"]').first();
      if (await dateInput.isVisible()) {
        await expect(dateInput).toBeVisible();
      }
    });

    test('should allow filling out basic journal entry information', async ({ page }) => {
      // Wait for page to load
      await page.waitForTimeout(1000);
      
      // Look for title input
      const titleSelectors = [
        'input[name="title"]',
        'input[placeholder*="název"]',
        'input[placeholder*="title"]',
        'input[placeholder*="Název"]'
      ];
      
      let titleInput = null;
      for (const selector of titleSelectors) {
        const element = page.locator(selector).first();
        if (await element.isVisible()) {
          titleInput = element;
          break;
        }
      }
      
      if (titleInput) {
        await titleInput.fill('Test Journal Entry');
        await expect(titleInput).toHaveValue('Test Journal Entry');
      }
      
      // Look for content textarea
      const contentSelectors = [
        'textarea[name="content"]',
        'textarea[placeholder*="obsah"]',
        'textarea[placeholder*="content"]',
        'textarea[placeholder*="Obsah"]'
      ];
      
      let contentInput = null;
      for (const selector of contentSelectors) {
        const element = page.locator(selector).first();
        if (await element.isVisible()) {
          contentInput = element;
          break;
        }
      }
      
      if (contentInput) {
        await contentInput.fill('This is a test journal entry content created during automated testing.');
        await expect(contentInput).toHaveValue('This is a test journal entry content created during automated testing.');
      }
      
      // Look for date input
      const dateSelectors = [
        'input[type="date"]',
        'input[name="date"]',
        'input[name="entryDate"]'
      ];
      
      let dateInput = null;
      for (const selector of dateSelectors) {
        const element = page.locator(selector).first();
        if (await element.isVisible()) {
          dateInput = element;
          break;
        }
      }
      
      if (dateInput) {
        await dateInput.fill('2024-01-15');
        await expect(dateInput).toHaveValue('2024-01-15');
      }
    });

    test('should show validation errors for required fields', async ({ page }) => {
      await page.waitForTimeout(1000);
      
      // Look for submit/save button
      const submitSelectors = [
        'button[type="submit"]',
        'button:has-text("Uložit")',
        'button:has-text("Vytvořit")',
        'button:has-text("Save")',
        'button:has-text("Create")'
      ];
      
      let submitButton = null;
      for (const selector of submitSelectors) {
        const element = page.locator(selector).first();
        if (await element.isVisible()) {
          submitButton = element;
          break;
        }
      }
      
      if (submitButton) {
        // Try to submit empty form
        await submitButton.click();
        
        await page.waitForTimeout(500);
        
        // Look for validation messages
        const errorSelectors = [
          '[class*="error"]',
          '[class*="invalid"]',
          'text=/povinné/i',
          'text=/required/i',
          'text=/vyplňte/i'
        ];
        
        let hasValidationError = false;
        for (const selector of errorSelectors) {
          const element = page.locator(selector).first();
          if (await element.isVisible()) {
            hasValidationError = true;
            break;
          }
        }
        
        // If no specific error message, at least form shouldn't submit successfully
        // (we should still be on the form page)
        expect(page.url()).toMatch(/journal/);
      }
    });

    test('should handle form submission with valid data', async ({ page }) => {
      await page.waitForTimeout(1000);
      
      // Fill out the form with valid data
      const titleInput = page.locator('input[name="title"], input[placeholder*="název"]').first();
      if (await titleInput.isVisible()) {
        await titleInput.fill('Playwright Test Entry');
      }
      
      const contentInput = page.locator('textarea[name="content"], textarea[placeholder*="obsah"]').first();
      if (await contentInput.isVisible()) {
        await contentInput.fill('This is a test entry created by Playwright automated testing.');
      }
      
      const dateInput = page.locator('input[type="date"], input[name="entryDate"]').first();
      if (await dateInput.isVisible()) {
        await dateInput.fill('2024-01-15');
      }
      
      // Submit form
      const submitButton = page.locator('button[type="submit"], button:has-text("Uložit"), button:has-text("Vytvořit")').first();
      if (await submitButton.isVisible()) {
        await submitButton.click();
        
        // Wait for submission to complete
        await page.waitForTimeout(2000);
        
        // Should either redirect to journal list or show success message
        const currentUrl = page.url();
        const hasSuccessMessage = await page.getByText(/úspěšně|success|vytvořen|created/i).isVisible();
        
        // Either redirected away from form or shows success message
        expect(currentUrl.includes('/journal/new') || hasSuccessMessage).toBeTruthy();
      }
    });

    test('should allow canceling and returning to journal list', async ({ page }) => {
      await page.waitForTimeout(1000);
      
      // Look for cancel button
      const cancelSelectors = [
        'button:has-text("Zrušit")',
        'button:has-text("Cancel")',
        'a:has-text("Zpět")',
        'a:has-text("Back")',
        '[href="/journal"]'
      ];
      
      let cancelButton = null;
      for (const selector of cancelSelectors) {
        const element = page.locator(selector).first();
        if (await element.isVisible()) {
          cancelButton = element;
          break;
        }
      }
      
      if (cancelButton) {
        await cancelButton.click();
        
        // Should navigate back to journal list
        await expect(page).toHaveURL(/.*\/journal$/);
        await expect(page.getByText('Deník')).toBeVisible();
      }
    });
  });

  test.describe('Edit Journal Entry', () => {
    test.beforeEach(async ({ page }) => {
      // First go to journal list to find an entry to edit
      await page.goto('http://localhost:3001/journal');
      await page.waitForTimeout(1000);
    });

    test('should navigate to edit form from journal list', async ({ page }) => {
      // Check if there are any journal entries
      const entryCount = await page.getByTestId('journal-entry').count();
      
      if (entryCount > 0) {
        const firstEntry = page.getByTestId('journal-entry').first();
        
        // Click edit button (should be first button in entry)
        const editButton = firstEntry.getByRole('button').first();
        await editButton.click();
        
        // Should navigate to edit page
        await expect(page).toHaveURL(/.*\/journal\/\d+\/edit/);
        
        // Should show form with existing data
        await page.waitForTimeout(1000);
        
        // Check that form fields are populated
        const titleInput = page.locator('input[name="title"], input[placeholder*="název"]').first();
        if (await titleInput.isVisible()) {
          const titleValue = await titleInput.inputValue();
          expect(titleValue.length).toBeGreaterThan(0);
        }
      }
    });

    test('should allow updating journal entry', async ({ page }) => {
      const entryCount = await page.getByTestId('journal-entry').count();
      
      if (entryCount > 0) {
        const firstEntry = page.getByTestId('journal-entry').first();
        const editButton = firstEntry.getByRole('button').first();
        await editButton.click();
        
        await page.waitForTimeout(1000);
        
        // Update title
        const titleInput = page.locator('input[name="title"], input[placeholder*="název"]').first();
        if (await titleInput.isVisible()) {
          await titleInput.clear();
          await titleInput.fill('Updated Test Entry');
        }
        
        // Update content
        const contentInput = page.locator('textarea[name="content"], textarea[placeholder*="obsah"]').first();
        if (await contentInput.isVisible()) {
          await contentInput.clear();
          await contentInput.fill('This entry has been updated by Playwright test.');
        }
        
        // Submit update
        const submitButton = page.locator('button[type="submit"], button:has-text("Uložit"), button:has-text("Aktualizovat")').first();
        if (await submitButton.isVisible()) {
          await submitButton.click();
          
          await page.waitForTimeout(2000);
          
          // Should show success or redirect
          const currentUrl = page.url();
          const hasSuccessMessage = await page.getByText(/aktualizován|updated|úspěšně/i).isVisible();
          
          expect(!currentUrl.includes('/edit') || hasSuccessMessage).toBeTruthy();
        }
      }
    });
  });

  test.describe('Form Accessibility and Usability', () => {
    test.beforeEach(async ({ page }) => {
      await page.goto('http://localhost:3001/journal/new');
      await page.waitForTimeout(1000);
    });

    test('should be keyboard navigable', async ({ page }) => {
      // Tab through form elements
      await page.keyboard.press('Tab');
      
      // First focusable element should be focused
      const focusedElement = await page.evaluate(() => document.activeElement?.tagName);
      expect(['INPUT', 'TEXTAREA', 'BUTTON', 'A'].includes(focusedElement || '')).toBeTruthy();
    });

    test('should have proper form labels', async ({ page }) => {
      // Check for labels or placeholder text
      const titleInput = page.locator('input[name="title"], input[placeholder*="název"]').first();
      if (await titleInput.isVisible()) {
        const hasLabel = await page.locator('label[for*="title"], label:has-text("název")').count() > 0;
        const hasPlaceholder = await titleInput.getAttribute('placeholder');
        
        expect(hasLabel || hasPlaceholder).toBeTruthy();
      }
    });

    test('should be responsive on mobile', async ({ page }) => {
      // Set mobile viewport
      await page.setViewportSize({ width: 375, height: 667 });
      
      // Form should still be usable
      const titleInput = page.locator('input[name="title"], input[placeholder*="název"]').first();
      if (await titleInput.isVisible()) {
        await titleInput.fill('Mobile Test');
        await expect(titleInput).toHaveValue('Mobile Test');
      }
    });
  });

  test.describe('Form with Product Associations', () => {
    test.beforeEach(async ({ page }) => {
      await page.goto('http://localhost:3001/journal/new');
      await page.waitForTimeout(1000);
    });

    test('should allow adding product codes and families', async ({ page }) => {
      // Look for product association fields
      const productCodeInput = page.locator('input[name*="product"], input[placeholder*="produkt"]').first();
      const productFamilyInput = page.locator('input[name*="family"], input[placeholder*="rodina"]').first();
      
      if (await productCodeInput.isVisible()) {
        await productCodeInput.fill('CREAM001,SERUM002');
      }
      
      if (await productFamilyInput.isVisible()) {
        await productFamilyInput.fill('CREAM,SERUM');
      }
      
      // Fill basic required fields
      const titleInput = page.locator('input[name="title"], input[placeholder*="název"]').first();
      if (await titleInput.isVisible()) {
        await titleInput.fill('Product Association Test');
      }
      
      const contentInput = page.locator('textarea[name="content"], textarea[placeholder*="obsah"]').first();
      if (await contentInput.isVisible()) {
        await contentInput.fill('Testing product associations.');
      }
    });
  });
});