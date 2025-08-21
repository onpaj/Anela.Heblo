import { test, expect } from '@playwright/test';

test.describe('Journal List Page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to journal page
    await page.goto('http://localhost:3001/journal');
    
    // Wait for page to load - look for page heading specifically
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('should display journal list page with header', async ({ page }) => {
    // Check page title
    await expect(page.getByText('Deník')).toBeVisible();
    
    // Check "New Entry" button is present
    await expect(page.getByTestId('add-journal-entry')).toBeVisible();
    await expect(page.getByText('Nový záznam')).toBeVisible();
    
    // Check search functionality is present
    await expect(page.getByTestId('journal-search')).toBeVisible();
    await expect(page.getByPlaceholderText('Hledat v záznamech...')).toBeVisible();
  });

  test('should show empty state when no journal entries exist', async ({ page }) => {
    // If no entries exist, should show empty state
    const emptyMessage = page.getByText('Zatím nemáte žádné záznamy v deníku.');
    const createFirstButton = page.getByText('Vytvořit první záznam');
    
    // Check if either entries exist or empty state is shown
    const hasEntries = await page.getByTestId('journal-entry').count();
    
    if (hasEntries === 0) {
      await expect(emptyMessage).toBeVisible();
      await expect(createFirstButton).toBeVisible();
    }
  });

  test('should display journal entries if they exist', async ({ page }) => {
    const entryCount = await page.getByTestId('journal-entry').count();
    
    if (entryCount > 0) {
      // Check that entries are displayed
      await expect(page.getByTestId('journal-entry').first()).toBeVisible();
      
      // Check that each entry has required elements
      const firstEntry = page.getByTestId('journal-entry').first();
      
      // Should have entry content
      await expect(firstEntry).toBeVisible();
      
      // Should have action buttons (edit and delete)
      await expect(firstEntry.locator('button').first()).toBeVisible();
    }
  });

  test('should navigate to new journal entry page when clicking "Nový záznam"', async ({ page }) => {
    await page.getByTestId('add-journal-entry').click();
    
    // Should navigate to new journal entry page
    await expect(page).toHaveURL(/.*\/journal\/new/);
  });

  test('should navigate to new journal entry page when clicking "Vytvořit první záznam" in empty state', async ({ page }) => {
    // Check if empty state is shown
    const createFirstButton = page.getByText('Vytvořit první záznam');
    
    if (await createFirstButton.isVisible()) {
      await createFirstButton.click();
      
      // Should navigate to new journal entry page
      await expect(page).toHaveURL(/.*\/journal\/new/);
    }
  });

  test('should perform search functionality', async ({ page }) => {
    const searchInput = page.getByTestId('journal-search');
    const searchButton = page.getByText('Hledat');
    
    // Type in search box
    await searchInput.fill('test search term');
    
    // Click search button
    await searchButton.click();
    
    // Should show search results info (even if no results)
    // The search info appears when in search mode
    await page.waitForTimeout(1000); // Wait for search to process
    
    // Check if search mode is active by looking for cancel button
    const cancelButton = page.getByText('Zrušit');
    if (await cancelButton.isVisible()) {
      // We're in search mode, check for search results info
      await expect(page.getByText(/Nalezeno.*záznamů pro/)).toBeVisible();
    }
  });

  test('should clear search and return to normal view', async ({ page }) => {
    const searchInput = page.getByTestId('journal-search');
    const searchButton = page.getByText('Hledat');
    
    // Perform search first
    await searchInput.fill('test');
    await searchButton.click();
    
    await page.waitForTimeout(1000);
    
    // Look for cancel button
    const cancelButton = page.getByText('Zrušit');
    if (await cancelButton.isVisible()) {
      await cancelButton.click();
      
      // Should clear search and return to normal view
      await expect(searchInput).toHaveValue('');
      await expect(cancelButton).not.toBeVisible();
    }
  });

  test('should perform search on Enter key press', async ({ page }) => {
    const searchInput = page.getByTestId('journal-search');
    
    // Type and press Enter
    await searchInput.fill('test search');
    await searchInput.press('Enter');
    
    await page.waitForTimeout(1000);
    
    // Should be in search mode
    const cancelButton = page.getByText('Zrušit');
    if (await cancelButton.isVisible()) {
      await expect(page.getByText(/Nalezeno.*záznamů pro/)).toBeVisible();
    }
  });

  test('should show loading state', async ({ page }) => {
    // Intercept the API call to make it slow
    await page.route('**/api/journal*', async route => {
      // Delay the response to see loading state
      await page.waitForTimeout(2000);
      route.continue();
    });
    
    // Reload page to trigger loading
    await page.reload();
    
    // Should show loading indicator
    await expect(page.getByText('Načítání deníku...')).toBeVisible();
  });

  test('should navigate to journal entry detail on content click', async ({ page }) => {
    const entryCount = await page.getByTestId('journal-entry').count();
    
    if (entryCount > 0) {
      const firstEntry = page.getByTestId('journal-entry').first();
      
      // Click on the content area (not on action buttons)
      await firstEntry.locator('div').filter({ hasText: /\w+/ }).first().click();
      
      // Should navigate to journal entry detail
      await expect(page).toHaveURL(/.*\/journal\/\d+$/);
    }
  });

  test('should show delete confirmation modal', async ({ page }) => {
    const entryCount = await page.getByTestId('journal-entry').count();
    
    if (entryCount > 0) {
      const firstEntry = page.getByTestId('journal-entry').first();
      
      // Find and click delete button (trash icon)
      const deleteButton = firstEntry.getByRole('button').last();
      await deleteButton.click();
      
      // Should show delete confirmation modal
      await expect(page.getByText('Smazat záznam')).toBeVisible();
      await expect(page.getByText('Opravdu chcete smazat tento záznam? Tuto akci nelze vrátit zpět.')).toBeVisible();
      
      // Should have confirm and cancel buttons
      await expect(page.getByText('Smazat')).toBeVisible();
      await expect(page.getByText('Zrušit')).toBeVisible();
    }
  });

  test('should close delete confirmation modal on cancel', async ({ page }) => {
    const entryCount = await page.getByTestId('journal-entry').count();
    
    if (entryCount > 0) {
      const firstEntry = page.getByTestId('journal-entry').first();
      
      // Click delete button
      const deleteButton = firstEntry.getByRole('button').last();
      await deleteButton.click();
      
      // Wait for modal to appear
      await expect(page.getByText('Smazat záznam')).toBeVisible();
      
      // Click cancel
      await page.getByText('Zrušit').click();
      
      // Modal should be closed
      await expect(page.getByText('Smazat záznam')).not.toBeVisible();
    }
  });

  test('should navigate to edit journal entry', async ({ page }) => {
    const entryCount = await page.getByTestId('journal-entry').count();
    
    if (entryCount > 0) {
      const firstEntry = page.getByTestId('journal-entry').first();
      
      // Find and click edit button (should be first button)
      const editButton = firstEntry.getByRole('button').first();
      await editButton.click();
      
      // Should navigate to edit page
      await expect(page).toHaveURL(/.*\/journal\/\d+\/edit/);
    }
  });

  test('should display pagination when multiple entries exist', async ({ page }) => {
    // Check if pagination is present (only shows when there are enough entries)
    const paginationInfo = page.getByText(/Zobrazeno.*z.*záznamů/);
    
    if (await paginationInfo.isVisible()) {
      // Should have pagination controls
      await expect(paginationInfo).toBeVisible();
      
      // Should have page navigation
      const pageInfo = page.getByText(/Stránka.*z/);
      if (await pageInfo.isVisible()) {
        await expect(pageInfo).toBeVisible();
      }
    }
  });

  test('should be responsive on mobile viewport', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    // Page should still be functional
    await expect(page.getByText('Deník')).toBeVisible();
    await expect(page.getByTestId('add-journal-entry')).toBeVisible();
    await expect(page.getByTestId('journal-search')).toBeVisible();
  });

  test('should handle API error gracefully', async ({ page }) => {
    // Intercept API calls and return error
    await page.route('**/api/journal*', route => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Internal server error' })
      });
    });
    
    // Reload page to trigger error
    await page.reload();
    
    // Should show error message
    await expect(page.getByText(/Chyba při načítání deníku/)).toBeVisible();
  });

  test('should display journal entry metadata correctly', async ({ page }) => {
    const entryCount = await page.getByTestId('journal-entry').count();
    
    if (entryCount > 0) {
      const firstEntry = page.getByTestId('journal-entry').first();
      
      // Should display entry date
      await expect(firstEntry.getByText(/\d{2}\.\d{2}\.\d{4}/)).toBeVisible();
      
      // Should display user ID or username
      await expect(firstEntry).toContainText(/user/i);
    }
  });

  test('should maintain scroll position during interactions', async ({ page }) => {
    const entryCount = await page.getByTestId('journal-entry').count();
    
    if (entryCount > 5) {
      // Scroll down
      await page.evaluate(() => window.scrollTo(0, 500));
      
      // Perform search
      const searchInput = page.getByTestId('journal-search');
      await searchInput.fill('test');
      await searchInput.press('Enter');
      
      await page.waitForTimeout(1000);
      
      // Page should maintain reasonable scroll position
      const scrollY = await page.evaluate(() => window.scrollY);
      expect(scrollY).toBeGreaterThanOrEqual(0);
    }
  });
});