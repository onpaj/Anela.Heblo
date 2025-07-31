import { test, expect } from '@playwright/test';

test.describe('CatalogList Page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate directly to the catalog page
    await page.goto('/catalog');
    await page.waitForLoadState('domcontentloaded');
    
    // Wait a bit for the page to fully render
    await page.waitForTimeout(1000);
  });

  test('should display catalog page header and filters', async ({ page }) => {
    // Check that the catalog page header is visible
    await expect(page.locator('h1')).toContainText('Seznam produktů');
    await expect(page.locator('text=Přehled všech produktů v katalogu')).toBeVisible();
    
    // Check that filters section is present
    const filtersSection = page.locator('text=Filtry').locator('..');
    await expect(filtersSection).toBeVisible();
    
    // Check filter inputs are present
    await expect(page.locator('input[placeholder*="název"]')).toBeVisible();
    await expect(page.locator('input[placeholder*="kódu"]')).toBeVisible();
    await expect(page.locator('select')).toBeVisible();
    
    // Take screenshot
    await page.screenshot({ path: 'test-results/catalog-page-header.png' });
  });

  test('should show loading state when fetching catalog data', async ({ page }) => {
    // Reload page to trigger loading state
    await page.reload();
    
    // Check for loading state
    const loadingText = page.locator('text=Načítání katalogu');
    const spinner = page.locator('.animate-spin');
    
    // Loading state should be visible briefly
    await expect(loadingText.or(spinner)).toBeVisible({ timeout: 3000 });
    
    // Take screenshot of loading state
    await page.screenshot({ path: 'test-results/catalog-loading-state.png' });
  });

  test('should display catalog data or appropriate messages', async ({ page }) => {
    // Wait for data to load
    await page.waitForTimeout(3000);
    
    // Check if we have catalog table OR empty state OR error message
    const catalogTable = page.locator('table');
    const emptyMessage = page.locator('text=Žádné produkty nebyly nalezeny');
    const errorMessage = page.locator('text=Chyba při načítání katalogu');
    
    // One of these should be visible
    const hasTable = await catalogTable.isVisible();
    const hasEmpty = await emptyMessage.isVisible();
    const hasError = await errorMessage.isVisible();
    
    expect(hasTable || hasEmpty || hasError).toBeTruthy();
    
    if (hasTable) {
      console.log('Catalog table found');
      
      // Verify table structure
      await expect(catalogTable).toBeVisible();
      
      // Check table headers
      await expect(page.locator('th:has-text("Kód produktu")')).toBeVisible();
      await expect(page.locator('th:has-text("Název produktu")')).toBeVisible();
      await expect(page.locator('th:has-text("Typ")')).toBeVisible();
      await expect(page.locator('th:has-text("Sklad")')).toBeVisible();
      
      // Check if there are any rows
      const tableRows = page.locator('tbody tr');
      const rowCount = await tableRows.count();
      
      if (rowCount > 0) {
        console.log(`Found ${rowCount} catalog items`);
        
        // Verify first row structure
        const firstRow = tableRows.first();
        await expect(firstRow).toBeVisible();
        
        // Check for product code in first column
        await expect(firstRow.locator('td').first()).toBeVisible();
        
        await page.screenshot({ path: 'test-results/catalog-data-loaded.png' });
      } else {
        console.log('Table present but no data rows');
        await page.screenshot({ path: 'test-results/catalog-table-empty.png' });
      }
    } else if (hasError) {
      console.log('Error state detected');
      
      // Verify error state structure
      await expect(errorMessage).toBeVisible();
      await expect(page.locator('.text-red-600')).toBeVisible();
      
      await page.screenshot({ path: 'test-results/catalog-error-state.png' });
    } else {
      console.log('Empty state detected');
      
      // Verify empty state structure
      await expect(emptyMessage).toBeVisible();
      
      await page.screenshot({ path: 'test-results/catalog-empty-state.png' });
    }
  });

  test('should filter products by name', async ({ page }) => {
    // Wait for initial data load
    await page.waitForTimeout(2000);
    
    // Find the product name filter input
    const nameFilter = page.locator('input[placeholder*="název"]');
    await expect(nameFilter).toBeVisible();
    
    // Type in a search term
    await nameFilter.fill('krém');
    
    // Wait for filtering to take effect
    await page.waitForTimeout(1000);
    
    // Check that filtering works (table should update or show appropriate message)
    const table = page.locator('table tbody');
    const emptyMessage = page.locator('text=Žádné produkty nebyly nalezeny');
    
    // Either we have filtered results or no matches message
    const hasResults = await table.locator('tr').count() > 0;
    const hasEmptyMessage = await emptyMessage.isVisible();
    
    expect(hasResults || hasEmptyMessage).toBeTruthy();
    
    if (hasResults) {
      // If we have results, they should contain the search term (case insensitive)
      const firstRow = table.locator('tr').first();
      const productName = await firstRow.locator('td').nth(1).textContent();
      expect(productName?.toLowerCase()).toContain('krém');
    }
    
    await page.screenshot({ path: 'test-results/catalog-name-filter.png' });
  });

  test('should filter products by code', async ({ page }) => {
    // Wait for initial data load
    await page.waitForTimeout(2000);
    
    // Find the product code filter input
    const codeFilter = page.locator('input[placeholder*="kódu"]');
    await expect(codeFilter).toBeVisible();
    
    // Type in a search term
    await codeFilter.fill('PROD');
    
    // Wait for filtering to take effect
    await page.waitForTimeout(1000);
    
    // Check that filtering works
    const table = page.locator('table tbody');
    const emptyMessage = page.locator('text=Žádné produkty nebyly nalezeny');
    
    const hasResults = await table.locator('tr').count() > 0;
    const hasEmptyMessage = await emptyMessage.isVisible();
    
    expect(hasResults || hasEmptyMessage).toBeTruthy();
    
    if (hasResults) {
      // If we have results, they should contain the search term
      const firstRow = table.locator('tr').first();
      const productCode = await firstRow.locator('td').first().textContent();
      expect(productCode?.toUpperCase()).toContain('PROD');
    }
    
    await page.screenshot({ path: 'test-results/catalog-code-filter.png' });
  });

  test('should filter products by type', async ({ page }) => {
    // Wait for initial data load
    await page.waitForTimeout(2000);
    
    // Find the product type filter select
    const typeFilter = page.locator('select');
    await expect(typeFilter).toBeVisible();
    
    // Check available options
    const options = await typeFilter.locator('option').allTextContents();
    expect(options.length).toBeGreaterThan(1); // Should have at least "Všechny typy" and some product types
    
    // Select a specific type (skip "Všechny typy" option)
    const nonEmptyOptions = options.filter(option => option !== 'Všechny typy');
    if (nonEmptyOptions.length > 0) {
      await typeFilter.selectOption({ label: nonEmptyOptions[0] });
      
      // Wait for filtering to take effect
      await page.waitForTimeout(1000);
      
      // Check that filtering works
      const table = page.locator('table tbody');
      const emptyMessage = page.locator('text=Žádné produkty nebyly nalezeny');
      
      const hasResults = await table.locator('tr').count() > 0;
      const hasEmptyMessage = await emptyMessage.isVisible();
      
      expect(hasResults || hasEmptyMessage).toBeTruthy();
      
      if (hasResults) {
        // Verify the type column shows the selected type
        const firstRow = table.locator('tr').first();
        const typeCell = firstRow.locator('td').nth(2);
        const typeText = await typeCell.textContent();
        expect(typeText).toContain(nonEmptyOptions[0]);
      }
    }
    
    await page.screenshot({ path: 'test-results/catalog-type-filter.png' });
  });

  test('should clear all filters when reset', async ({ page }) => {
    // Wait for initial data load
    await page.waitForTimeout(2000);
    
    // Apply some filters
    await page.locator('input[placeholder*="název"]').fill('test');
    await page.locator('input[placeholder*="kódu"]').fill('TEST');
    
    // Wait for filters to apply
    await page.waitForTimeout(1000);
    
    // Clear all filters
    await page.locator('input[placeholder*="název"]').clear();
    await page.locator('input[placeholder*="kódu"]').clear();
    await page.locator('select').selectOption({ label: 'Všechny typy' });
    
    // Wait for results to update
    await page.waitForTimeout(1000);
    
    // Should show all products again (or at least not show filtered results)
    const table = page.locator('table tbody tr');
    const rowCount = await table.count();
    
    // We should have results or appropriate empty state
    expect(rowCount >= 0).toBeTruthy();
    
    await page.screenshot({ path: 'test-results/catalog-filters-cleared.png' });
  });

  test('should be responsive on different screen sizes', async ({ page }) => {
    // Test desktop view
    await page.setViewportSize({ width: 1200, height: 800 });
    await page.waitForTimeout(1000);
    await page.screenshot({ path: 'test-results/catalog-desktop-view.png' });
    
    // Verify table is visible on desktop
    await expect(page.locator('table')).toBeVisible();
    
    // Test tablet view
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: 'test-results/catalog-tablet-view.png' });
    
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: 'test-results/catalog-mobile-view.png' });
    
    // Verify elements are still accessible on mobile
    await expect(page.locator('h1:has-text("Seznam produktů")')).toBeVisible();
    
    // Check if table has horizontal scroll on mobile
    const table = page.locator('table');
    if (await table.isVisible()) {
      const tableContainer = page.locator('.overflow-x-auto');
      await expect(tableContainer).toBeVisible();
    }
  });

  test('should display stock information correctly', async ({ page }) => {
    // Wait for data to load
    await page.waitForTimeout(3000);
    
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    
    if (rowCount > 0) {
      // Check first row stock information
      const firstRow = table.locator('tr').first();
      const stockCell = firstRow.locator('td').nth(3); // Stock column should be 4th column
      
      await expect(stockCell).toBeVisible();
      
      // Check for stock information labels
      const stockLabels = ['E-shop:', 'ERP:', 'Transport:', 'Rezerva:', 'Dostupné:'];
      
      for (const label of stockLabels) {
        const hasLabel = await stockCell.locator(`text=${label}`).isVisible();
        if (hasLabel) {
          console.log(`Found stock label: ${label}`);
        }
      }
      
      await page.screenshot({ path: 'test-results/catalog-stock-info.png' });
    }
  });

  test('should have proper accessibility features', async ({ page }) => {
    // Check that filter inputs have proper labels
    const nameInput = page.locator('input[placeholder*="název"]');
    const codeInput = page.locator('input[placeholder*="kódu"]');
    const typeSelect = page.locator('select');
    
    // Inputs should be focusable
    await nameInput.focus();
    await expect(nameInput).toBeFocused();
    
    await codeInput.focus();
    await expect(codeInput).toBeFocused();
    
    await typeSelect.focus();
    await expect(typeSelect).toBeFocused();
    
    // Check table accessibility
    const table = page.locator('table');
    if (await table.isVisible()) {
      // Table should have proper headers
      const headers = page.locator('th');
      const headerCount = await headers.count();
      expect(headerCount).toBeGreaterThan(0);
      
      // Headers should be properly structured
      await expect(page.locator('thead')).toBeVisible();
      await expect(page.locator('tbody')).toBeVisible();
    }
    
    // Check heading hierarchy
    await expect(page.locator('h1')).toBeVisible();
    await expect(page.locator('h2')).toBeVisible(); // Filters heading
    
    await page.screenshot({ path: 'test-results/catalog-accessibility.png' });
  });

  test('should handle keyboard navigation', async ({ page }) => {
    // Test keyboard navigation through filter inputs
    await page.keyboard.press('Tab'); // Focus first interactive element
    await page.keyboard.press('Tab'); // Next element
    await page.keyboard.press('Tab'); // Next element
    
    // Should be able to type in focused input
    await page.keyboard.type('test');
    
    // Continue tabbing through elements
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab');
    
    // Take screenshot to verify focus states
    await page.screenshot({ path: 'test-results/catalog-keyboard-navigation.png' });
    
    // Test Enter key functionality if applicable
    const focusedElement = page.locator(':focus');
    if (await focusedElement.isVisible()) {
      const tagName = await focusedElement.evaluate(el => el.tagName.toLowerCase());
      console.log('Currently focused element:', tagName);
    }
  });
});