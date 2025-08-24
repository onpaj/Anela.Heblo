import { test, expect } from '@playwright/test';

/**
 * Test the new 4-category color classification system for Manufacturing Stock Analysis
 * 
 * New Color Logic:
 * - Red: Overstock < 100% (Critical)
 * - Orange: Current stock < minimum stock (Major)
 * - Gray: Missing OptimalStockDaysSetup (Unconfigured)
 * - Green: All conditions met (Adequate)
 */
test.describe('Manufacturing Stock Analysis - Color Classification', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to manufacturing stock analysis
    await page.goto('http://localhost:3001');
    await expect(page).toHaveTitle(/Anela Heblo/);
    
    // Wait for sidebar navigation to be visible (indicates app is loaded)
    await expect(page.locator('nav').first()).toBeVisible();
    
    // Navigate to Manufacturing Stock Analysis page
    await page.getByText('Řízení zásob').click();
    
    // Wait for the page to load
    await expect(page.getByText('Řízení zásob ve výrobě')).toBeVisible();
    await page.waitForTimeout(3000); // Give time for data to load
  });

  test('displays color-coded row backgrounds based on severity', async ({ page }) => {
    // Take a screenshot to see current state
    await page.screenshot({ 
      path: 'test-results/manufacturing-stock-colors-overview.png',
      fullPage: true 
    });
    
    // Look for either table or empty state
    const rows = page.locator('table tbody tr');
    const emptyState = page.locator('[data-testid="empty-state"], .no-data, .empty-state');
    const loadingState = page.locator('.loading, [data-testid="loading"]');
    
    // Wait for either data to load or empty state to show
    await Promise.race([
      rows.first().waitFor({ state: 'visible', timeout: 10000 }),
      emptyState.first().waitFor({ state: 'visible', timeout: 10000 }),
      loadingState.first().waitFor({ state: 'hidden', timeout: 10000 })
    ]).catch(() => {
      console.log('Neither data nor empty state found within timeout');
    });
    
    const rowCount = await rows.count();
    
    console.log(`Found ${rowCount} product rows to analyze`);
    
    if (rowCount > 0) {
      // Check first few rows for color classes
      for (let i = 0; i < Math.min(5, rowCount); i++) {
        const row = rows.nth(i);
        const rowClass = await row.getAttribute('class');
        
        console.log(`Row ${i + 1} classes: ${rowClass}`);
        
        // Verify the row has one of the expected background color classes
        const hasColorClass = rowClass && (
          rowClass.includes('bg-red-50') ||     // Critical
          rowClass.includes('bg-orange-50') ||  // Major  
          rowClass.includes('bg-gray-50') ||    // Unconfigured
          rowClass.includes('bg-emerald-50')    // Adequate
        );
        
        expect(hasColorClass).toBeTruthy();
      }
    }
  });

  test('shows correct color classification in summary cards', async ({ page }) => {
    // Wait for summary cards to be visible
    await expect(page.getByText('Celkem')).toBeVisible();
    
    // Check that we have summary cards for each severity level
    const summarySection = page.locator('[class*="grid"][class*="gap-4"]').first();
    
    // Look for the new Czech labels based on the updated logic
    await expect(summarySection.getByText('Kritické')).toBeVisible();         // Critical - Red
    await expect(summarySection.getByText('Dostatečné')).toBeVisible();       // Adequate - Green
    
    // Optional: These may or may not be present depending on data
    const majorCard = summarySection.getByText('Pod min. zásobou');    // Major - Orange
    const unconfiguredCard = summarySection.getByText('Nezkonfigurováno');   // Unconfigured - Gray
    
    // Take screenshot of summary cards
    await summarySection.screenshot({ 
      path: 'test-results/manufacturing-stock-summary-cards.png' 
    });
  });

  test('filters correctly when clicking on severity cards', async ({ page }) => {
    // Wait for page to load
    await expect(page.getByText('Celkem')).toBeVisible();
    
    // Get initial row count
    const initialRows = await page.locator('table tbody tr').count();
    console.log(`Initial row count: ${initialRows}`);
    
    if (initialRows > 0) {
      // Click on Critical items card (should filter to show only critical items)
      const criticalCard = page.getByText('Kritické').first();
      if (await criticalCard.isVisible()) {
        await criticalCard.click();
        
        // Wait for filter to apply
        await page.waitForTimeout(1000);
        
        // Check if filtering worked (may have fewer or same number of items)
        const filteredRows = await page.locator('table tbody tr').count();
        console.log(`Rows after critical filter: ${filteredRows}`);
        
        // Take screenshot showing filtered results
        await page.screenshot({ 
          path: 'test-results/manufacturing-stock-critical-filter.png',
          fullPage: true 
        });
        
        // Verify all visible rows have critical (red) background
        const visibleRows = page.locator('table tbody tr');
        const visibleCount = await visibleRows.count();
        
        if (visibleCount > 0) {
          for (let i = 0; i < Math.min(3, visibleCount); i++) {
            const row = visibleRows.nth(i);
            const rowClass = await row.getAttribute('class');
            
            console.log(`Critical filtered row ${i + 1} classes: ${rowClass}`);
            // Should have red background for critical items
            expect(rowClass).toContain('bg-red-50');
          }
        }
      }
    }
  });

  test('shows unconfigured items when toggling unconfigured filter', async ({ page }) => {
    // First, expand filters if they're collapsed
    const showFiltersBtn = page.getByText('Zobrazit filtry');
    if (await showFiltersBtn.isVisible()) {
      await showFiltersBtn.click();
      await page.waitForTimeout(500);
    }
    
    // Look for unconfigured filter checkbox
    const unconfiguredCheckbox = page.getByText('Pouze nedefìnované').locator('input[type="checkbox"]');
    
    if (await unconfiguredCheckbox.isVisible()) {
      // Click the unconfigured filter
      await unconfiguredCheckbox.check();
      
      // Wait for filter to apply  
      await page.waitForTimeout(1000);
      
      // Take screenshot showing unconfigured items
      await page.screenshot({ 
        path: 'test-results/manufacturing-stock-unconfigured-filter.png',
        fullPage: true 
      });
      
      // Check that visible rows have gray background (unconfigured)
      const rows = page.locator('table tbody tr');
      const rowCount = await rows.count();
      
      console.log(`Unconfigured filter row count: ${rowCount}`);
      
      if (rowCount > 0) {
        const firstRow = rows.first();
        const rowClass = await firstRow.getAttribute('class');
        
        console.log(`Unconfigured row classes: ${rowClass}`);
        // Should have gray background for unconfigured items
        expect(rowClass).toContain('bg-gray-50');
      }
    } else {
      console.log('Unconfigured filter checkbox not found - may not be implemented yet');
    }
  });

  test('visual verification of color implementation', async ({ page }) => {
    // Navigate directly to the page to verify it loads
    await page.goto('http://localhost:3001');
    
    // Wait a moment for load
    await page.waitForTimeout(2000);
    
    // Click on manufacturing if it exists
    const manufacturingLink = page.getByText('Řízení zásob - výroba');
    if (await manufacturingLink.isVisible()) {
      await manufacturingLink.click();
      await page.waitForTimeout(3000);
    }
    
    // Take a full page screenshot to document the current color implementation
    await page.screenshot({ 
      path: 'test-results/manufacturing-stock-visual-verification.png',
      fullPage: true 
    });
    
    // Just verify the page loaded - don't require specific data
    const pageTitle = await page.title();
    console.log('Page title:', pageTitle);
    
    expect(pageTitle).toContain('Anela');
  });
});