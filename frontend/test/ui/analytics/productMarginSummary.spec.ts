import { test, expect } from '@playwright/test';

test.describe('Product Margin Summary Page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the analytics page
    await page.goto('/analytics/product-margin-summary');
  });

  test('should display page title and description', async ({ page }) => {
    // Check if the main heading is visible
    await expect(page.getByRole('heading', { name: 'Přehled marží produktů' })).toBeVisible();
    
    // Check if the description is visible
    await expect(page.getByText('Analýza celkové marže z prodeje produktů v čase')).toBeVisible();
  });

  test('should display grouping mode and time window selectors', async ({ page }) => {
    // Check if the grouping mode dropdown is visible
    const groupingModeSelect = page.locator('#grouping-mode');
    await expect(groupingModeSelect).toBeVisible();
    
    // Check if default grouping mode option is selected (Products = 0)
    await expect(groupingModeSelect).toHaveValue('0');
    
    // Check if all grouping mode options are available
    await expect(groupingModeSelect.locator('option')).toHaveText([
      'Jednotlivé produkty',
      'Rodiny produktů',
      'Kategorie produktů'
    ]);
    
    // Check if the time window dropdown is visible
    const timeWindowSelect = page.locator('#time-window');
    await expect(timeWindowSelect).toBeVisible();
    
    // Check if default option is selected
    await expect(timeWindowSelect).toHaveValue('current-year');
    
    // Check if all options are available
    await expect(timeWindowSelect.locator('option')).toHaveText([
      'Aktuální rok',
      'Aktuální + předchozí rok', 
      'Posledních 6 měsíců',
      'Posledních 12 měsíců',
      'Posledních 24 měsíců'
    ]);
  });

  test('should change grouping mode and time window and reload data', async ({ page }) => {
    // Wait for initial load
    await page.waitForLoadState('networkidle');
    
    // Change grouping mode
    const groupingModeSelect = page.locator('#grouping-mode');
    await groupingModeSelect.selectOption('1'); // ProductFamily
    
    // Verify the grouping mode selection changed
    await expect(groupingModeSelect).toHaveValue('1');
    
    // Change time window
    const timeWindowSelect = page.locator('#time-window');
    await timeWindowSelect.selectOption('last-6-months');
    
    // Verify the time window selection changed
    await expect(timeWindowSelect).toHaveValue('last-6-months');
    
    // Wait for potential API call to complete
    await page.waitForLoadState('networkidle');
  });

  test('should show loading state initially', async ({ page }) => {
    // On a fresh page load, we might catch the loading state
    await page.goto('/analytics/product-margin-summary', { waitUntil: 'domcontentloaded' });
    
    // The loading state might be very brief, so we check for either loading or content
    const loadingIndicator = page.getByText('Načítám data o marži produktů...');
    const chartContainer = page.locator('[data-testid="chart"], .chartjs-chart-bar');
    
    // Either loading should be visible or chart should load
    await expect(loadingIndicator.or(chartContainer)).toBeVisible({ timeout: 10000 });
  });

  test('should display chart when data is available', async ({ page }) => {
    // Wait for the page to load completely
    await page.waitForLoadState('networkidle');
    
    // Check if chart container exists (Chart.js creates canvas elements)
    const chartSection = page.locator('.flex-1.bg-white.shadow.rounded-lg');
    await expect(chartSection).toBeVisible();
    
    // Look for chart-related elements
    const chartArea = chartSection.locator('canvas, [data-testid="chart"]');
    await expect(chartArea).toBeVisible({ timeout: 15000 });
  });

  test('should show summary information when data is loaded', async ({ page }) => {
    // Wait for data to load
    await page.waitForLoadState('networkidle');
    
    // Wait a bit more for data to populate
    await page.waitForTimeout(2000);
    
    // Look for summary information container - should include the new "Zobrazených skupin" info
    const summarySection = page.locator('text=Celková marže:').or(page.locator('text=Období:')).or(page.locator('text=Zobrazených skupin:'));
    
    // Either summary should be visible or we should see empty state
    const emptyState = page.getByText('Žádná data o marži');
    await expect(summarySection.or(emptyState)).toBeVisible({ timeout: 10000 });
  });

  test('should be responsive and adapt to different screen sizes', async ({ page }) => {
    // Test mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    await page.waitForLoadState('networkidle');
    
    // Check if the layout adapts (chart container should still be visible)
    const chartContainer = page.locator('.flex-1.bg-white.shadow.rounded-lg');
    await expect(chartContainer).toBeVisible();
    
    // Test desktop viewport
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.waitForLoadState('networkidle');
    
    // Chart should still be visible
    await expect(chartContainer).toBeVisible();
  });

  test('should follow consistent page layout structure', async ({ page }) => {
    // Check main container structure
    const mainContainer = page.locator('.flex.flex-col.h-full.w-full').first();
    await expect(mainContainer).toBeVisible();
    
    // Check header section
    const headerSection = mainContainer.locator('.flex-shrink-0.mb-3').first();
    await expect(headerSection).toBeVisible();
    
    // Check controls section
    const controlsSection = mainContainer.locator('.flex-shrink-0.bg-white.shadow.rounded-lg').first();
    await expect(controlsSection).toBeVisible();
    
    // Check chart section
    const chartSection = mainContainer.locator('.flex-1.bg-white.shadow.rounded-lg');
    await expect(chartSection).toBeVisible();
  });

  test('should test all three grouping modes', async ({ page }) => {
    // Wait for initial load
    await page.waitForLoadState('networkidle');
    
    const groupingModeSelect = page.locator('#grouping-mode');
    
    // Test Products (default)
    await expect(groupingModeSelect).toHaveValue('0');
    await page.waitForLoadState('networkidle');
    
    // Test ProductFamily
    await groupingModeSelect.selectOption('1');
    await expect(groupingModeSelect).toHaveValue('1');
    await page.waitForLoadState('networkidle');
    
    // Test ProductCategory  
    await groupingModeSelect.selectOption('2');
    await expect(groupingModeSelect).toHaveValue('2');
    await page.waitForLoadState('networkidle');
    
    // Switch back to Products
    await groupingModeSelect.selectOption('0');
    await expect(groupingModeSelect).toHaveValue('0');
    await page.waitForLoadState('networkidle');
  });
});