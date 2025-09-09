import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToTransportBoxes } from './helpers/e2e-auth-helper';

test.describe('Transport Box Management E2E Tests', () => {
  
  test.beforeEach(async ({ page }) => {
    await createE2EAuthSession(page);
  });

  test('should navigate to Transport Box list page', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Verify page title and main elements
    await expect(page.locator('h1')).toContainText('Transportní boxy');
    
    // Verify essential UI elements are present
    const searchBox = page.locator('input[type="search"], input[placeholder*="search"], input[placeholder*="hledat"]');
    const filterControls = page.locator('select, .filter-button, .dropdown');
    const boxList = page.locator('[data-testid="transport-box-list"], .transport-box-list, .box-list');
    
    // At least one of these should be present on a management page
    const hasSearchBox = await searchBox.count() > 0;
    const hasFilterControls = await filterControls.count() > 0;
    const hasBoxList = await boxList.count() > 0;
    
    expect(hasSearchBox || hasFilterControls || hasBoxList).toBe(true);
  });

  test('should test transport box filtering and search functionality', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Test search functionality
    const searchBox = page.locator('input[type="search"], input[placeholder*="search"], input[placeholder*="hledat"]').first();
    
    if (await searchBox.count() > 0) {
      // Get initial results count
      const initialBoxes = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
      const initialCount = await initialBoxes.count();
      
      // Perform search
      await searchBox.fill('test');
      await page.keyboard.press('Enter');
      await page.waitForTimeout(1000);
      
      // Verify search was performed (results changed or no results message)
      const afterSearchBoxes = await initialBoxes.count();
      const noResultsMessage = page.locator('text=No results, text=Žádné výsledky, .empty-state, .no-results');
      
      const searchWorked = afterSearchBoxes !== initialCount || await noResultsMessage.count() > 0;
      expect(searchWorked).toBe(true);
      
      // Clear search
      await searchBox.clear();
      await page.keyboard.press('Enter');
      await page.waitForTimeout(1000);
    }
  });

  test('should validate box status indicators and state display', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Look for transport boxes in the list
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    const boxCount = await boxItems.count();
    
    if (boxCount > 0) {
      // Check first few boxes for status indicators
      const boxesToCheck = Math.min(3, boxCount);
      
      for (let i = 0; i < boxesToCheck; i++) {
        const box = boxItems.nth(i);
        
        // Look for status badges/indicators
        const statusBadge = box.locator('.badge, .status, .state, [class*="bg-"], [class*="text-"]');
        const statusText = box.locator('text=/New|Opened|InTransit|Received|Stocked|Reserve|Closed|Error|Nový|Otevřený|V přepravě|Přijatý|Naskladněný|V rezervě|Uzavřený|Chyba/');
        
        // At least one should be present
        const hasBadge = await statusBadge.count() > 0;
        const hasStatusText = await statusText.count() > 0;
        
        expect(hasBadge || hasStatusText).toBe(true);
        
        // If status is visible, verify it's one of the expected states
        if (await statusText.count() > 0) {
          const text = await statusText.first().textContent();
          const validStates = ['New', 'Opened', 'InTransit', 'Received', 'Stocked', 'Reserve', 'Closed', 'Error',
                              'Nový', 'Otevřený', 'V přepravě', 'Přijatý', 'Naskladněný', 'V rezervě', 'Uzavřený', 'Chyba'];
          expect(validStates.some(state => text?.includes(state))).toBe(true);
        }
      }
    }
  });

  test('should test box sorting and pagination', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Test sorting functionality
    const sortButtons = page.locator('button').filter({ hasText: /Sort|Seřadit|Date|Created|State|Status/ });
    const sortHeaders = page.locator('th[role="button"], th.sortable, .sort-header');
    
    // Try clicking sort controls
    if (await sortButtons.count() > 0) {
      const originalOrder = await page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item').allTextContents();
      
      await sortButtons.first().click();
      await page.waitForTimeout(1000);
      
      const newOrder = await page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item').allTextContents();
      
      // Order should change (unless there's only one item)
      if (originalOrder.length > 1) {
        expect(JSON.stringify(originalOrder) !== JSON.stringify(newOrder)).toBe(true);
      }
    }
    
    // Try clicking sortable table headers
    if (await sortHeaders.count() > 0) {
      await sortHeaders.first().click();
      await page.waitForTimeout(1000);
      
      // Should not throw error and page should still be functional
      await expect(page.locator('h1')).toContainText('Transport Boxes');
    }
    
    // Test pagination
    const paginationControls = page.locator('.pagination, .page-navigation, button').filter({ hasText: /Next|Previous|Další|Předchozí|\d+/ });
    const nextButton = page.locator('button').filter({ hasText: /Next|Další|>/ });
    const prevButton = page.locator('button').filter({ hasText: /Previous|Předchozí|</ });
    
    if (await nextButton.count() > 0) {
      const isEnabled = await nextButton.first().isEnabled();
      if (isEnabled) {
        await nextButton.first().click();
        await page.waitForTimeout(1000);
        
        // Should navigate to next page
        await expect(page.locator('h1')).toContainText('Transportní boxy');
        
        // Previous button should now be available
        if (await prevButton.count() > 0) {
          await expect(prevButton.first()).toBeEnabled();
        }
      }
    }
  });

  test('should verify bulk operations on multiple boxes', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Look for checkboxes to select multiple items
    const checkboxes = page.locator('input[type="checkbox"]');
    const selectAllCheckbox = page.locator('input[type="checkbox"]').first();
    
    if (await checkboxes.count() > 1) {
      // Test individual selection
      const firstItemCheckbox = checkboxes.nth(1); // Skip select-all if present
      await firstItemCheckbox.check();
      await expect(firstItemCheckbox).toBeChecked();
      
      const secondItemCheckbox = checkboxes.nth(2);
      if (await secondItemCheckbox.count() > 0) {
        await secondItemCheckbox.check();
        await expect(secondItemCheckbox).toBeChecked();
      }
      
      // Look for bulk action buttons that appear when items are selected
      const bulkActions = page.locator('button').filter({ 
        hasText: /Delete Selected|Export|Batch|Bulk|Označené|Hromadně/ 
      });
      
      if (await bulkActions.count() > 0) {
        // Verify bulk actions are available
        await expect(bulkActions.first()).toBeVisible();
        
        // Test that clicking shows confirmation or performs action
        await bulkActions.first().click();
        await page.waitForTimeout(500);
        
        // Should show confirmation dialog or perform action
        const confirmDialog = page.locator('[role="dialog"], .modal, .confirmation');
        const notification = page.locator('.notification, .toast, .alert');
        
        const hasConfirmDialog = await confirmDialog.count() > 0;
        const hasNotification = await notification.count() > 0;
        
        expect(hasConfirmDialog || hasNotification).toBe(true);
        
        // If there's a confirmation dialog, cancel it
        if (hasConfirmDialog) {
          const cancelButton = confirmDialog.locator('button').filter({ hasText: /Cancel|Zrušit|No|Ne/ });
          if (await cancelButton.count() > 0) {
            await cancelButton.click();
          }
        }
      }
      
      // Test select all functionality
      if (await selectAllCheckbox.count() > 0) {
        await selectAllCheckbox.check();
        await page.waitForTimeout(500);
        
        // All checkboxes should be checked
        const allCheckboxes = await checkboxes.all();
        for (const checkbox of allCheckboxes.slice(1)) { // Skip select-all itself
          if (await checkbox.count() > 0) {
            await expect(checkbox).toBeChecked();
          }
        }
        
        // Uncheck select all
        await selectAllCheckbox.uncheck();
        await page.waitForTimeout(500);
        
        // All checkboxes should be unchecked
        for (const checkbox of allCheckboxes.slice(1)) {
          if (await checkbox.count() > 0) {
            await expect(checkbox).not.toBeChecked();
          }
        }
      }
    } else {
      console.log('No bulk selection functionality found - this may be expected');
    }
  });

  test('should test filtering by status and date range', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Test status filtering
    const statusFilter = page.locator('select[name*="status"], select[name*="state"], select').filter({ hasText: /Status|State|Stav/ });
    
    if (await statusFilter.count() > 0) {
      const originalCount = await page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)').count();
      
      // Select a specific status
      await statusFilter.first().selectOption({ index: 1 });
      await page.waitForTimeout(1000);
      
      // Results should change or show filtered results
      const filteredCount = await page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)').count();
      const noResults = page.locator('text=No results, text=Žádné výsledky, .empty-state');
      
      const filterWorked = filteredCount !== originalCount || await noResults.count() > 0;
      expect(filterWorked).toBe(true);
    }
    
    // Test date range filtering
    const dateFromInput = page.locator('input[type="date"], input[name*="from"], input[name*="start"], input[placeholder*="od"]').first();
    const dateToInput = page.locator('input[type="date"], input[name*="to"], input[name*="end"], input[placeholder*="do"]').first();
    
    if (await dateFromInput.count() > 0 && await dateToInput.count() > 0) {
      // Set date range (last month to today)
      const today = new Date();
      const lastMonth = new Date();
      lastMonth.setMonth(lastMonth.getMonth() - 1);
      
      await dateFromInput.fill(lastMonth.toISOString().split('T')[0]);
      await dateToInput.fill(today.toISOString().split('T')[0]);
      
      // Apply filter (look for apply button or auto-apply)
      const applyButton = page.locator('button').filter({ hasText: /Apply|Filter|Použít|Filtrovat/ });
      if (await applyButton.count() > 0) {
        await applyButton.first().click();
      }
      
      await page.waitForTimeout(1000);
      
      // Should show filtered results
      const hasResults = await page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)').count() > 0;
      const hasNoResults = await page.locator('text=No results, text=Žádné výsledky').count() > 0;
      
      expect(hasResults || hasNoResults).toBe(true);
    }
  });

  test('should test responsive behavior on different screen sizes', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Test desktop view (default)
    await page.setViewportSize({ width: 1200, height: 800 });
    await page.waitForTimeout(500);
    
    // All elements should be visible
    await expect(page.locator('h1')).toBeVisible();
    const desktopElements = await page.locator('button, input, select').count();
    expect(desktopElements).toBeGreaterThan(0);
    
    // Test tablet view
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.waitForTimeout(500);
    
    // Page should still be functional
    await expect(page.locator('h1')).toBeVisible();
    
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 });
    await page.waitForTimeout(500);
    
    // Page should still be functional, possibly with responsive layout
    await expect(page.locator('h1')).toBeVisible();
    
    // Mobile menu might be present
    const mobileMenu = page.locator('.mobile-menu, .hamburger, button[aria-label="Menu"]');
    if (await mobileMenu.count() > 0) {
      await mobileMenu.first().click();
      await page.waitForTimeout(500);
      
      // Menu should expand or navigation should be visible
      const navigation = page.locator('nav, .navigation, .menu');
      await expect(navigation.first()).toBeVisible();
    }
    
    // Reset to desktop
    await page.setViewportSize({ width: 1200, height: 800 });
  });
});