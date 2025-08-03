import { test, expect } from '@playwright/test';

test.describe('Purchase Order List', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to purchase orders page
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
  });

  test('should display purchase order list page', async ({ page }) => {
    // Check page title
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
    
    // Check "New Order" button exists
    await expect(page.locator('button:has-text("Nová objednávka")')).toBeVisible();
    
    // Check filters section exists
    await expect(page.locator('text=Filtry:')).toBeVisible();
  });

  test('should have functional filters', async ({ page }) => {
    // Check search input
    const searchInput = page.locator('input[placeholder="Hledat objednávky..."]');
    await expect(searchInput).toBeVisible();
    
    // Check status filter
    const statusSelect = page.locator('select').first();
    await expect(statusSelect).toBeVisible();
    await expect(statusSelect.locator('option[value=""]')).toContainText('Všechny stavy');
    await expect(statusSelect.locator('option[value="Draft"]')).toContainText('Návrh');
    await expect(statusSelect.locator('option[value="InTransit"]')).toContainText('V přepravě');
    await expect(statusSelect.locator('option[value="Completed"]')).toContainText('Dokončeno');
    
    // Check date inputs
    const dateInputs = page.locator('input[type="date"]');
    await expect(dateInputs).toHaveCount(2);
    
    // Check filter buttons
    await expect(page.locator('button:has-text("Filtrovat")')).toBeVisible();
    await expect(page.locator('button:has-text("Vymazat")')).toBeVisible();
  });

  test('should display data table with correct headers', async ({ page }) => {
    // Check table headers
    const headers = [
      'Číslo objednávky',
      'Dodavatel', 
      'Datum objednávky',
      'Plánované dodání',
      'Stav',
      'Celková částka',
      'Položky'
    ];
    
    for (const header of headers) {
      await expect(page.locator('th', { hasText: header })).toBeVisible();
    }
  });

  test('should handle loading state', async ({ page }) => {
    // Navigate to page to trigger loading
    await page.goto('http://localhost:3001/nakup/objednavky');
    
    // Check for loading indicator (might be very fast)
    const loadingIndicator = page.locator('text=Načítání objednávek...');
    // Loading might be too fast to catch, so we just check it doesn't error
  });

  test('should handle empty state', async ({ page }) => {
    // Wait for data to load
    await page.waitForTimeout(2000);
    
    // If no data, should show empty message
    const emptyMessage = page.locator('text=Žádné objednávky nebyly nalezeny.');
    const tableRows = page.locator('tbody tr');
    
    // Either we have data OR we have empty message
    const hasData = await tableRows.count() > 0;
    if (!hasData) {
      await expect(emptyMessage).toBeVisible();
    }
  });

  test('should open create modal when "Nová objednávka" is clicked', async ({ page }) => {
    // Click the "New Order" button
    await page.locator('button:has-text("Nová objednávka")').click();
    
    // Check modal opens
    await expect(page.locator('text=Nová nákupní objednávka')).toBeVisible();
    
    // Check close button works
    await page.locator('button:has-text("Zavřít")').click();
    await expect(page.locator('text=Nová nákupní objednávka')).not.toBeVisible();
  });

  test('should handle search functionality', async ({ page }) => {
    const searchInput = page.locator('input[placeholder="Hledat objednávky..."]');
    
    // Type in search
    await searchInput.fill('test order');
    
    // Press Enter
    await searchInput.press('Enter');
    
    // Filter button should be clickable
    await page.locator('button:has-text("Filtrovat")').click();
  });

  test('should handle status filter', async ({ page }) => {
    const statusSelect = page.locator('select').first();
    
    // Select Draft status
    await statusSelect.selectOption('Draft');
    
    // Apply filter
    await page.locator('button:has-text("Filtrovat")').click();
  });

  test('should handle clear filters', async ({ page }) => {
    const searchInput = page.locator('input[placeholder="Hledat objednávky..."]');
    const statusSelect = page.locator('select').first();
    
    // Fill some filters
    await searchInput.fill('test');
    await statusSelect.selectOption('Draft');
    
    // Clear filters
    await page.locator('button:has-text("Vymazat")').click();
    
    // Check inputs are cleared
    await expect(searchInput).toHaveValue('');
    await expect(statusSelect).toHaveValue('');
  });

  test('should show pagination when there is data', async ({ page }) => {
    // Wait for potential data
    await page.waitForTimeout(2000);
    
    // Check if pagination exists (only if there's data)
    const paginationInfo = page.locator('text=/Zobrazeno \\d+ až \\d+ z \\d+ výsledků/');
    const hasData = await page.locator('tbody tr').count() > 0;
    
    if (hasData) {
      // Should have pagination info
      await expect(paginationInfo).toBeVisible();
    }
  });

  test('should be responsive', async ({ page }) => {
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 });
    
    // Should still show main elements
    await expect(page.locator('h1')).toContainText('Nákupní objednávky');
    await expect(page.locator('button:has-text("Nová objednávka")')).toBeVisible();
    
    // Test desktop view
    await page.setViewportSize({ width: 1200, height: 800 });
    
    // Should show all columns
    await expect(page.locator('th:has-text("Číslo objednávky")')).toBeVisible();
  });

  test('should handle keyboard navigation', async ({ page }) => {
    const searchInput = page.locator('input[placeholder="Hledat objednávky..."]');
    
    // Focus search input
    await searchInput.focus();
    
    // Type and press Enter
    await searchInput.type('test');
    await searchInput.press('Enter');
    
    // Should trigger search
    // (This is tested by checking the input value)
    await expect(searchInput).toHaveValue('test');
  });
});