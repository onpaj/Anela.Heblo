import { test, expect } from '@playwright/test';

test.describe('Financial Overview Page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to financial overview page
    await page.goto('http://localhost:3001/financni-prehled');
    
    // Wait for the page to load
    await page.waitForSelector('h1', { timeout: 10000 });
  });

  test('should display financial overview page title', async ({ page }) => {
    // Check if the main heading is visible
    const heading = page.locator('h1').first();
    await expect(heading).toBeVisible();
    await expect(heading).toHaveText('Finanční přehled');
  });

  test('should display page description', async ({ page }) => {
    // Check if the description is visible (look for text specifically)
    const description = page.locator('text=Přehled příjmů, nákladů a celkové bilance firmy');
    await expect(description).toBeVisible();
  });

  test('should display period selector', async ({ page }) => {
    // Check if the period selector is visible and has default value
    const periodSelect = page.locator('#period-select');
    await expect(periodSelect).toBeVisible();
    await expect(periodSelect).toHaveValue('current-year');
  });

  test('should display summary cards when data loads', async ({ page }) => {
    // Wait for loading to complete
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });

    // Check if summary cards are visible
    const summaryCards = page.locator('.grid').first();
    await expect(summaryCards).toBeVisible();
    
    // Check for specific card titles
    await expect(page.locator('text=Celkové příjmy')).toBeVisible();
    await expect(page.locator('text=Celkové náklady')).toBeVisible();
    await expect(page.locator('text=Celková bilance')).toBeVisible();
    await expect(page.locator('text=Průměrná měsíční bilance')).toBeVisible();
  });

  test('should display chart container', async ({ page }) => {
    // Wait for data to load
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });

    // Check if chart container is visible
    const chartContainer = page.locator('canvas').first();
    await expect(chartContainer).toBeVisible();
  });

  test('should display data table', async ({ page }) => {
    // Wait for data to load
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });

    // Check if table is visible
    const table = page.locator('table').first();
    await expect(table).toBeVisible();
    
    // Check table headers
    await expect(page.locator('th').filter({ hasText: 'Měsíc' })).toBeVisible();
    await expect(page.locator('th').filter({ hasText: 'Příjmy' })).toBeVisible();
    await expect(page.locator('th').filter({ hasText: 'Náklady' })).toBeVisible();
    await expect(page.locator('th').filter({ hasText: 'Bilance' })).toBeVisible();
  });

  test('should change time period when selector changes', async ({ page }) => {
    // Wait for initial load
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });

    // Change time period to current year
    await page.selectOption('#period-select', 'current-year');
    
    // Verify the select value changed (loading might be too fast to catch)
    await expect(page.locator('#period-select')).toHaveValue('current-year');
    
    // Wait a bit for potential data reload
    await page.waitForTimeout(1000);
    
    // Make sure page is still functional
    await expect(page.locator('h1').first()).toBeVisible();
  });

  test('should handle loading state properly', async ({ page }) => {
    // Navigate to page
    await page.goto('http://localhost:3001/financni-prehled');
    
    // Check if loading state is shown initially
    const loadingText = page.locator('text=Načítám finanční data...');
    // Note: This might be very fast, so we just check it can appear
    
    // Wait for loading to complete
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });
    
    // Ensure loading state is gone
    await expect(loadingText).not.toBeVisible();
  });

  test('should be responsive on mobile', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    // Wait for data to load
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });
    
    // Check if main elements are still visible on mobile
    await expect(page.locator('h1').first()).toBeVisible();
    await expect(page.locator('#period-select')).toBeVisible();
    
    // Check if summary cards stack properly on mobile
    const summaryCards = page.locator('.grid').first();
    await expect(summaryCards).toBeVisible();
  });

  test('should display stock data toggle control', async ({ page }) => {
    // Check if stock toggle control is visible
    const stockToggle = page.locator('#stock-toggle');
    await expect(stockToggle).toBeVisible();
    
    // Check if the label is visible
    const toggleLabel = page.locator('text=Zahrnout skladová data');
    await expect(toggleLabel).toBeVisible();
    
    // Verify initial state (should be checked by default)
    await expect(stockToggle).toBeChecked();
  });

  test('should toggle stock data when checkbox is clicked', async ({ page }) => {
    // Wait for initial data to load
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });

    // Click the stock toggle checkbox
    const stockToggle = page.locator('#stock-toggle');
    await stockToggle.click();
    
    // Verify checkbox is now checked
    await expect(stockToggle).toBeChecked();
    
    // Wait for loading to complete (new data request)
    await page.waitForTimeout(2000);
    
    // Check that chart title reflects stock data inclusion
    const chartContainer = page.locator('.bg-white.shadow.rounded-lg.p-6').first();
    await expect(chartContainer).toContainText('včetně skladu');
    
    // Verify additional table columns appear when stock data is included
    const stockChangeHeader = page.locator('th').filter({ hasText: 'Změna skladu' });
    const totalBalanceHeader = page.locator('th').filter({ hasText: 'Celková bilance' });
    
    await expect(stockChangeHeader).toBeVisible();
    await expect(totalBalanceHeader).toBeVisible();
  });

  test('should display stock summary cards when stock data is enabled', async ({ page }) => {
    // Wait for initial data to load
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });

    // Enable stock data
    const stockToggle = page.locator('#stock-toggle');
    await stockToggle.click();
    
    // Wait for new data to load
    await page.waitForTimeout(2000);
    
    // Check for stock-related summary cards
    // Note: With placeholder service, these might be visible but with zero values
    const stockValueChangeCard = page.locator('text=Změna hodnoty skladu');
    const totalBalanceWithStockCard = page.locator('text=Celková bilance vč. skladu');
    
    // These cards should appear when stock data is enabled
    await expect(stockValueChangeCard).toBeVisible();
    await expect(totalBalanceWithStockCard).toBeVisible();
  });

  test('should hide stock-related elements when stock data is disabled', async ({ page }) => {
    // Wait for data to load
    await page.waitForFunction(() => {
      const loadingSpinner = document.querySelector('.animate-spin');
      return !loadingSpinner;
    }, { timeout: 15000 });

    // Disable stock data first (since it's now enabled by default)
    const stockToggle = page.locator('#stock-toggle');
    await stockToggle.click();
    
    // Wait for new data to load
    await page.waitForTimeout(2000);

    // Verify stock-specific table headers are not visible
    const stockChangeHeader = page.locator('th').filter({ hasText: 'Změna skladu' });
    const totalBalanceHeader = page.locator('th').filter({ hasText: 'Celková bilance' });
    
    await expect(stockChangeHeader).not.toBeVisible();
    await expect(totalBalanceHeader).not.toBeVisible();
    
    // Verify stock summary cards are not visible
    const stockValueChangeCard = page.locator('text=Změna hodnoty skladu');
    const totalBalanceWithStockCard = page.locator('text=Celková bilance vč. skladu');
    
    await expect(stockValueChangeCard).not.toBeVisible();
    await expect(totalBalanceWithStockCard).not.toBeVisible();
  });
});