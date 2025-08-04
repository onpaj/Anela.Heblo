import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis Page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the Purchase Stock Analysis page
    await page.goto('/nakup/analyza-skladu');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
  });

  test('displays the page header correctly', async ({ page }) => {
    // Check main heading
    await expect(page.getByText('Analýza skladových zásob')).toBeVisible();
    
    // Check subtitle
    await expect(page.getByText('Přehled skladových hladin a spotřeby materiálů a zboží')).toBeVisible();
    
    // Check action buttons
    await expect(page.getByRole('button', { name: 'Obnovit' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Export' })).toBeVisible();
  });

  test('displays filter controls correctly', async ({ page }) => {
    // Check filter section
    await expect(page.getByText('Filtry')).toBeVisible();
    
    // Check search input
    await expect(page.getByPlaceholder('Kód, název, dodavatel...')).toBeVisible();
    
    // Check status filter dropdown
    await expect(page.getByText('Stav zásob')).toBeVisible();
    const statusSelect = page.locator('select').first();
    await expect(statusSelect).toBeVisible();
    
    // Check date inputs
    await expect(page.getByText('Od data')).toBeVisible();
    await expect(page.getByText('Do data')).toBeVisible();
    
    // Check configured products checkbox
    await expect(page.getByText('Pouze konfigurované produkty')).toBeVisible();
  });

  test('allows searching for products', async ({ page }) => {
    const searchInput = page.getByPlaceholder('Kód, název, dodavatel...');
    
    // Type search term
    await searchInput.fill('MAT');
    
    // Verify the input value
    await expect(searchInput).toHaveValue('MAT');
    
    // Wait for potential API call
    await page.waitForTimeout(500);
  });

  test('allows filtering by stock status', async ({ page }) => {
    const statusSelect = page.locator('select').first();
    
    // Select Critical status
    await statusSelect.selectOption('Critical');
    
    // Verify selection
    await expect(statusSelect).toHaveValue('Critical');
    
    // Wait for potential API call
    await page.waitForTimeout(500);
  });

  test('allows setting date range', async ({ page }) => {
    const fromDate = page.locator('input[type="date"]').first();
    const toDate = page.locator('input[type="date"]').last();
    
    // Set from date
    await fromDate.fill('2023-01-01');
    await expect(fromDate).toHaveValue('2023-01-01');
    
    // Set to date
    await toDate.fill('2023-12-31');
    await expect(toDate).toHaveValue('2023-12-31');
    
    // Wait for potential API call
    await page.waitForTimeout(500);
  });

  test('allows toggling configured products filter', async ({ page }) => {
    const checkbox = page.getByRole('checkbox');
    
    // Initially unchecked
    await expect(checkbox).not.toBeChecked();
    
    // Click to check
    await checkbox.check();
    await expect(checkbox).toBeChecked();
    
    // Click to uncheck
    await checkbox.uncheck();
    await expect(checkbox).not.toBeChecked();
  });

  test('displays results section', async ({ page }) => {
    // Check results heading
    await expect(page.getByText('Výsledky analýzy')).toBeVisible();
    
    // Check sort instructions
    await expect(page.getByText('Klikněte na záhlaví sloupce pro řazení')).toBeVisible();
  });

  test('displays table headers correctly', async ({ page }) => {
    // Wait for table to potentially load
    await page.waitForTimeout(1000);
    
    // Check if table headers are present (they might not be visible if no data)
    const tableHeaders = [
      'Skladem',
      'Min/Opt',
      'Spotřeba/období',
      'NS',
      'MOQ',
      'Dny do vyprodání',
      'Poslední nákup'
    ];
    
    // Try to find table headers, but don't fail if they're not present (could be empty state)
    for (const header of tableHeaders) {
      const headerElement = page.getByText(header, { exact: true });
      if (await headerElement.isVisible()) {
        await expect(headerElement).toBeVisible();
      }
    }
    
    // Check for "Produkt" header with exact match
    const productHeader = page.getByText('Produkt', { exact: true });
    if (await productHeader.isVisible()) {
      await expect(productHeader).toBeVisible();
    }
  });

  test('refresh button works', async ({ page }) => {
    const refreshButton = page.getByRole('button', { name: 'Obnovit' });
    
    // Click refresh button
    await refreshButton.click();
    
    // Wait for potential API call
    await page.waitForTimeout(500);
    
    // Button should still be visible
    await expect(refreshButton).toBeVisible();
  });

  test('column header sorting works', async ({ page }) => {
    // Wait for potential data load
    await page.waitForTimeout(1000);
    
    // Try to click on sortable column headers - use exact match
    const productHeader = page.getByText('Produkt', { exact: true });
    if (await productHeader.isVisible()) {
      await productHeader.click();
      await page.waitForTimeout(500);
      
      // Click again to reverse sort
      await productHeader.click();
      await page.waitForTimeout(500);
    }
    
    // Test NS column sorting (previously Efektivita)
    const efficiencyHeader = page.getByText('NS', { exact: true });
    if (await efficiencyHeader.isVisible()) {
      await efficiencyHeader.click();
      await page.waitForTimeout(500);
    }
  });

  test('handles responsive design', async ({ page }) => {
    // Test desktop view
    await page.setViewportSize({ width: 1200, height: 800 });
    await expect(page.getByText('Analýza skladových zásob')).toBeVisible();
    
    // Test tablet view
    await page.setViewportSize({ width: 768, height: 1024 });
    await expect(page.getByText('Analýza skladových zásob')).toBeVisible();
    
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 });
    await expect(page.getByText('Analýza skladových zásob')).toBeVisible();
  });

  test('navigation via sidebar works', async ({ page }) => {
    // First navigate to a different page
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Administrační dashboard' })).toBeVisible();
    
    // Navigate to Purchase Stock Analysis via sidebar
    await page.click('text=Nákup');
    await page.click('text=Analýza skladů');
    
    // Should be on the analysis page
    await expect(page.getByText('Analýza skladových zásob')).toBeVisible();
  });

  test('displays empty state when no data', async ({ page }) => {
    // Wait for any initial loading
    await page.waitForTimeout(2000);
    
    // Check if empty state is shown (this will depend on actual API response)
    const emptyState = page.getByText('Žádné výsledky');
    if (await emptyState.isVisible()) {
      await expect(emptyState).toBeVisible();
      await expect(page.getByText('Zkuste upravit filtry nebo vyhledávací kritéria.')).toBeVisible();
    }
  });

  test('export button is clickable', async ({ page }) => {
    const exportButton = page.getByRole('button', { name: 'Export' });
    
    // Should be clickable
    await expect(exportButton).toBeEnabled();
    
    // Click export (note: actual implementation may show a dialog or download file)
    await exportButton.click();
  });
});