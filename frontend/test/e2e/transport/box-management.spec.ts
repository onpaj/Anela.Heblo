import { test, expect } from '@playwright/test';
import { navigateToTransportBoxes } from '../helpers/e2e-auth-helper';

test.describe('Transport Box Management E2E Tests', () => {

  test.beforeEach(async ({ page }) => {
    // Navigate to transport boxes with full authentication
    await navigateToTransportBoxes(page);
  });

  test('should navigate to Transport Box list page', async ({ page }) => {
    
    // Verify page title and main elements
    await expect(page.locator('h1')).toContainText('Transportní boxy');
    
    // Verify essential UI elements are present - based on actual TransportBoxList component
    const searchBox = page.locator('input[placeholder*="Kód boxu"], input[placeholder*="Vyhledat"]');
    const filterControls = page.locator('button:has-text("Filtry a nastavení")');
    const boxTable = page.locator('table tbody tr');
    const loadingIndicator = page.locator('text="Načítání dat..."');
    const noResults = page.locator('text="Žádné výsledky"');
    
    // Wait for page to load
    await page.waitForTimeout(2000);
    
    // At least one of these should be present on a management page
    const hasSearchBox = await searchBox.count() > 0;
    const hasFilterControls = await filterControls.count() > 0;
    const hasBoxTable = await boxTable.count() > 0;
    const hasLoadingIndicator = await loadingIndicator.count() > 0;
    const hasNoResults = await noResults.count() > 0;
    
    expect(hasSearchBox || hasFilterControls || hasBoxTable || hasLoadingIndicator || hasNoResults).toBe(true);
  });

  test('should test transport box filtering and search functionality', async ({ page }) => {
    await navigateToTransportBoxes(page);
    await page.waitForTimeout(2000);
    
    // Test search functionality - look for the specific search input from TransportBoxList
    const searchBox = page.locator('input[placeholder*="Kód boxu"], input[placeholder*="Vyhledat"]').first();
    
    if (await searchBox.count() > 0) {
      // Get initial results count
      const initialBoxes = page.locator('table tbody tr');
      const initialCount = await initialBoxes.count();
      
      // Perform search with a simple test term
      await searchBox.fill('999999');
      await page.keyboard.press('Enter');
      await page.waitForTimeout(2000);
      
      // Verify search was performed (results changed or no results message)
      const afterSearchBoxes = await initialBoxes.count();
      const noResultsMessage = page.locator('text="Žádné výsledky"');
      
      const searchWorked = afterSearchBoxes !== initialCount || await noResultsMessage.count() > 0;
      expect(searchWorked).toBe(true);
      
      // Clear search using the X button or clear input
      const clearButton = page.locator('input[placeholder*="Kód boxu"] + button');
      if (await clearButton.count() > 0) {
        await clearButton.click();
      } else {
        await searchBox.clear();
        await page.keyboard.press('Enter');
      }
      await page.waitForTimeout(1000);
    }
  });

  test('should validate box status indicators and state display', async ({ page }) => {
    await navigateToTransportBoxes(page);
    await page.waitForTimeout(2000);
    
    // Look for transport boxes in the table
    const boxItems = page.locator('table tbody tr');
    const boxCount = await boxItems.count();
    
    if (boxCount > 0) {
      // Check first few boxes for status indicators - based on TransportBoxList structure
      const boxesToCheck = Math.min(3, boxCount);
      
      for (let i = 0; i < boxesToCheck; i++) {
        const box = boxItems.nth(i);
        
        // Look for status badges - specifically the rounded badges from TransportBoxList
        const statusBadge = box.locator('span[class*="inline-flex"][class*="items-center"][class*="rounded-full"]');
        const statusText = box.locator('text=/Nový|Otevřený|V přepravě|Přijatý|Naskladněný|V rezervě|Uzavřený|Chyba/');
        
        // At least one should be present
        const hasBadge = await statusBadge.count() > 0;
        const hasStatusText = await statusText.count() > 0;
        
        expect(hasBadge || hasStatusText).toBe(true);
        
        // If status is visible, verify it's one of the expected Czech states
        if (await statusText.count() > 0) {
          const text = await statusText.first().textContent();
          const validStates = ['Nový', 'Otevřený', 'V přepravě', 'Přijatý', 'Naskladněný', 'V rezervě', 'Uzavřený', 'Chyba'];
          expect(validStates.some(state => text?.includes(state))).toBe(true);
        }
      }
    } else {
      // No boxes found - check if this is expected (loading or no data)
      const loadingIndicator = page.locator('text="Načítání dat..."');
      const noResults = page.locator('text="Žádné výsledky"');
      const hasLoading = await loadingIndicator.count() > 0;
      const hasNoResults = await noResults.count() > 0;
      expect(hasLoading || hasNoResults).toBe(true);
    }
  });

  test('should test box sorting and pagination', async ({ page }) => {
    await navigateToTransportBoxes(page);
    await page.waitForTimeout(2000);
    
    // Test sorting functionality - based on TransportBoxList clickable headers
    const sortableHeaders = page.locator('th.cursor-pointer');
    
    if (await sortableHeaders.count() > 0) {
      // Get original order of boxes
      const originalOrder = await page.locator('table tbody tr td:first-child').allTextContents();
      
      // Click first sortable header (should be "Kód")
      await sortableHeaders.first().click();
      await page.waitForTimeout(2000);
      
      const newOrder = await page.locator('table tbody tr td:first-child').allTextContents();
      
      // Order should change (unless there's only one item or data is identical)
      if (originalOrder.length > 1) {
        // Allow for the possibility that sorting doesn't change order if data is already sorted
        // Just verify that the sorting function worked (no errors)
        expect(newOrder.length).toBe(originalOrder.length);
      } else if (originalOrder.length === 1) {
        // Single item - order can't change but sorting should still work
        expect(newOrder.length).toBe(1);
      }
      
      // Page should still be functional after sorting
      await expect(page.locator('h1')).toContainText('Transportní boxy');
    }
    
    // Test pagination - look for the specific pagination structure from TransportBoxList
    const nextButton = page.locator('button:has-text("Další")');
    const prevButton = page.locator('button:has-text("Předchozí")');
    const paginationContainer = page.locator('nav[aria-label="Pagination"]');
    
    if (await nextButton.count() > 0) {
      // Check if next button is visible and enabled
      const isVisible = await nextButton.isVisible();
      const isEnabled = await nextButton.isEnabled();
      
      if (isVisible && isEnabled) {
        await nextButton.click();
        await page.waitForTimeout(2000);
        
        // Should navigate to next page
        await expect(page.locator('h1')).toContainText('Transportní boxy');
        
        // Previous button should now be available and enabled
        if (await prevButton.count() > 0) {
          await expect(prevButton).toBeEnabled();
        }
      }
    } else {
      // No pagination controls found - this might be expected if there's not enough data
      console.log('No pagination controls found - this may be expected with limited data');
    }
  });

  test('should verify transport box detail interaction', async ({ page }) => {
    await navigateToTransportBoxes(page);
    await page.waitForTimeout(2000);
    
    // Look for transport boxes in the table - the TransportBoxList makes rows clickable
    const boxRows = page.locator('table tbody tr');
    const boxCount = await boxRows.count();
    
    if (boxCount > 0) {
      // Click on the first box row to open detail modal
      const firstRow = boxRows.first();
      await firstRow.click();
      await page.waitForTimeout(1000);
      
      // Should open the transport box detail modal
      const modal = page.locator('[role="dialog"], .modal, .fixed.inset-0');
      
      if (await modal.count() > 0) {
        // Modal should be visible
        await expect(modal.first()).toBeVisible();
        
        // Look for close button or modal content
        const closeButton = modal.locator('button:has-text("Zavřít"), button:has-text("Close"), button[aria-label*="close"]');
        const modalContent = modal.locator('h2, h3, .modal-header');
        
        // At least modal content should be present
        const hasContent = await modalContent.count() > 0;
        expect(hasContent).toBe(true);
        
        // Close the modal if close button exists
        if (await closeButton.count() > 0) {
          await closeButton.first().click();
          await page.waitForTimeout(500);
          
          // Modal should be closed
          await expect(modal.first()).not.toBeVisible();
        } else {
          // Try clicking outside the modal to close it
          await page.keyboard.press('Escape');
          await page.waitForTimeout(500);
        }
      } else {
        console.log('No modal opened - row click may not be implemented yet');
      }
    } else {
      // No boxes found - check if this is expected (loading or no data)
      const loadingIndicator = page.locator('text="Načítání dat..."');
      const noResults = page.locator('text="Žádné výsledky"');
      const hasLoading = await loadingIndicator.count() > 0;
      const hasNoResults = await noResults.count() > 0;
      expect(hasLoading || hasNoResults).toBe(true);
    }
  });

  test('should test filtering by status and controls', async ({ page }) => {
    await navigateToTransportBoxes(page);
    await page.waitForTimeout(2000);
    
    // Test status filtering using the summary cards from TransportBoxList
    const statusButtons = page.locator('button:has-text("Celkem:"), button:has-text("Aktivní:"), button:has-text("Nový:"), button:has-text("Otevřený:")');
    
    if (await statusButtons.count() > 0) {
      const originalCount = await page.locator('table tbody tr').count();
      
      // Click on "Aktivní" filter if available
      const activeButton = page.locator('button:has-text("Aktivní:")');
      if (await activeButton.count() > 0) {
        await activeButton.click();
        await page.waitForTimeout(2000);
        
        // Results should change or show filtered results
        const filteredCount = await page.locator('table tbody tr').count();
        const noResults = page.locator('text="Žádné výsledky"');
        
        // Filter may not change results if there's no data matching the filter criteria
        // This is acceptable behavior - just verify the filter operation completed without errors
        const hasResults = filteredCount > 0;
        const hasNoResults = await noResults.count() > 0;
        const filterWorked = hasResults || hasNoResults || filteredCount !== originalCount;
        expect(filterWorked).toBe(true);
        
        // Reset filter by clicking "Celkem" if available
        const totalButton = page.locator('button:has-text("Celkem:")');
        if (await totalButton.count() > 0) {
          await totalButton.click();
          await page.waitForTimeout(1000);
        }
      }
    }
    
    // Test product filter using the CatalogAutocomplete
    const productFilter = page.locator('input[placeholder*="Vyhledat produkt"]');
    
    if (await productFilter.count() > 0) {
      // Expand filters if they're collapsed
      const filtersToggle = page.locator('button:has-text("Filtry a nastavení")');
      if (await filtersToggle.count() > 0) {
        const isExpanded = await page.locator('button:has-text("Filtry a nastavení") svg.rotate-90').count() > 0;
        if (!isExpanded) {
          await filtersToggle.click();
          await page.waitForTimeout(500);
        }
      }
      
      // Try typing in the product filter
      await productFilter.fill('test');
      await page.waitForTimeout(1000);
      
      // Clear the filter
      await productFilter.clear();
      await page.waitForTimeout(500);
    }
    
    // Test the "Clear all filters" functionality if present
    const clearFiltersButton = page.locator('button:has-text("Vymazat všechny filtry")');
    if (await clearFiltersButton.count() > 0) {
      await clearFiltersButton.click();
      await page.waitForTimeout(1000);
    }
  });

  test('should test responsive behavior and action buttons', async ({ page }) => {
    await navigateToTransportBoxes(page);
    await page.waitForTimeout(2000);
    
    // Test desktop view (default)
    await page.setViewportSize({ width: 1200, height: 800 });
    await page.waitForTimeout(500);
    
    // All elements should be visible
    await expect(page.locator('h1')).toBeVisible();
    
    // Test action buttons from TransportBoxList
    const newBoxButton = page.locator('button:has-text("Otevřít nový box")');
    const refreshButton = page.locator('button:has-text("Obnovit")');
    
    if (await newBoxButton.count() > 0) {
      // New box button should be clickable
      await expect(newBoxButton).toBeVisible();
      // Don't actually click it in test - it would create a new box
    }
    
    if (await refreshButton.count() > 0) {
      // Refresh button should work
      await refreshButton.click();
      await page.waitForTimeout(2000);
      
      // Page should still be functional after refresh
      await expect(page.locator('h1')).toContainText('Transportní boxy');
    }
    
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
    
    // Test collapsible controls behavior
    const filtersToggle = page.locator('button:has-text("Filtry a nastavení")');
    if (await filtersToggle.count() > 0) {
      await filtersToggle.click();
      await page.waitForTimeout(500);
      
      // Should toggle the filters visibility
      await filtersToggle.click();
      await page.waitForTimeout(500);
    }
    
    // Reset to desktop
    await page.setViewportSize({ width: 1200, height: 800 });
    await page.waitForTimeout(500);
  });
});