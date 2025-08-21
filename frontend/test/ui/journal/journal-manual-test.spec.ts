import { test, expect } from '@playwright/test';

test.describe('Journal Manual Functionality Test', () => {
  test('should manually test journal creation flow via modal', async ({ page }) => {
    // Navigate to journal page
    await page.goto('http://localhost:3001/journal');
    
    // Wait for page to load
    await page.waitForLoadState('networkidle');
    
    // Check if page loaded successfully
    await expect(page.locator('body')).toContainText('Deník');
    
    // Take screenshot of journal list
    await page.screenshot({ path: 'test-results/journal-list-state.png' });
    console.log('Journal list page loaded successfully');
    
    // Try to click on "Nový záznam" button to open modal
    const newEntryButton = page.locator('button[data-testid="add-journal-entry"]');
    if (await newEntryButton.isVisible()) {
      console.log('New entry button found, clicking...');
      await newEntryButton.click();
      
      // Wait for modal to appear
      await page.waitForSelector('[role="dialog"], .modal, .fixed.inset-0', { timeout: 5000 });
      console.log('Modal appeared');
      
      // Take screenshot of modal
      await page.screenshot({ path: 'test-results/journal-modal-opened.png' });
      
      // Try to fill out the form in modal
      const titleInput = page.locator('input[placeholder*="název"], input#title');
      if (await titleInput.isVisible()) {
        await titleInput.fill('Test Entry from Playwright');
        console.log('Title filled');
      }
      
      const contentTextarea = page.locator('textarea[placeholder*="obsah"], textarea#content');
      if (await contentTextarea.isVisible()) {
        await contentTextarea.fill('This is a test entry created by Playwright automation test');
        console.log('Content filled');
      }
      
      // Take screenshot after filling form
      await page.screenshot({ path: 'test-results/journal-modal-filled.png' });
      
      // Try to save the form
      const saveButton = page.locator('button:has-text("Vytvořit záznam"), button:has-text("Uložit")');
      if (await saveButton.isVisible()) {
        console.log('Save button found, attempting to save...');
        await saveButton.click();
        
        // Wait for modal to close
        await page.waitForTimeout(2000);
        await page.screenshot({ path: 'test-results/journal-after-save.png' });
        console.log('Save attempted, took screenshot');
      }
    }
    
    // Final page status check
    const currentUrl = page.url();
    console.log('Final URL:', currentUrl);
    
    const pageContent = await page.locator('body').textContent();
    console.log('Page content contains "Test Entry":', pageContent?.includes('Test Entry') || false);
    
    await page.screenshot({ path: 'test-results/journal-final-state.png' });
  });

  test('should check if journal list loads correctly', async ({ page }) => {
    await page.goto('http://localhost:3001/journal');
    await page.waitForLoadState('networkidle');
    
    // Check basic structure
    await expect(page.locator('h1')).toContainText('Deník');
    
    // Look for the new entry button
    const newButton = page.locator('button:has-text("Nový záznam")');
    await expect(newButton).toBeVisible();
    
    // Look for search functionality
    const searchInput = page.locator('input[placeholder*="Hledat"]');
    await expect(searchInput).toBeVisible();
    
    console.log('Journal list structure verified successfully');
  });
});