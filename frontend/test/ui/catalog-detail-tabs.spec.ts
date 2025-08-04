import { test, expect } from '@playwright/test';

test.describe('Catalog Detail Tabbed Interface', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the catalog page first
    await page.goto('/catalog');
    await page.waitForLoadState('domcontentloaded');
    
    // Wait for catalog data to load
    await page.waitForTimeout(3000);
  });

  test('should display tabbed interface when catalog detail is opened', async ({ page }) => {
    // Find the first product row and click on it to open detail
    const table = page.locator('table tbody');
    const firstRow = table.locator('tr').first();
    
    // Check if we have data to test with
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    // Click on the first product row to open detail modal
    await firstRow.click();
    
    // Wait for the modal to appear
    await page.waitForTimeout(1000);
    
    // Verify the modal is open
    const modal = page.locator('.fixed.inset-0.bg-black.bg-opacity-50');
    await expect(modal).toBeVisible();
    
    // Verify the tab navigation is present
    const tabContainer = page.locator('.flex.border-b.border-gray-200').nth(1); // Second instance (first is modal header)
    await expect(tabContainer).toBeVisible();
    
    // Verify both tabs are present
    const basicInfoTab = page.locator('button', { hasText: 'Základní informace' });
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    
    await expect(basicInfoTab).toBeVisible();
    await expect(purchaseHistoryTab).toBeVisible();
    
    // Verify the basic info tab is active by default
    await expect(basicInfoTab).toHaveClass(/border-indigo-500/);
    await expect(basicInfoTab).toHaveClass(/text-indigo-600/);
    
    // Take screenshot of the tabbed interface
    await page.screenshot({ path: 'test-results/catalog-detail-tabs-default.png' });
  });

  test('should display basic information in the first tab', async ({ page }) => {
    // Open first product detail
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Verify basic info tab is active
    const basicInfoTab = page.locator('button', { hasText: 'Základní informace' });
    await expect(basicInfoTab).toHaveClass(/border-indigo-500/);
    
    // Verify basic information sections are present
    await expect(page.locator('h3', { hasText: 'Základní informace' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Skladové zásoby' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Cenové informace' })).toBeVisible();
    await expect(page.locator('h3', { hasText: 'Vlastnosti produktu' })).toBeVisible();
    
    // Verify specific basic info fields
    await expect(page.locator('text=Typ produktu:')).toBeVisible();
    await expect(page.locator('text=Umístění:')).toBeVisible();
    await expect(page.locator('text=Min. objednávka:')).toBeVisible();
    await expect(page.locator('text=Min. výroba:')).toBeVisible();
    
    // Verify stock information
    await expect(page.locator('text=Dostupné:')).toBeVisible();
    await expect(page.locator('text=Shoptet:')).toBeVisible();
    await expect(page.locator('text=ABRA:')).toBeVisible();
    await expect(page.locator('text=Transport:')).toBeVisible();
    await expect(page.locator('text=Rezervované:')).toBeVisible();
    
    // Take screenshot of basic info tab
    await page.screenshot({ path: 'test-results/catalog-detail-basic-info-tab.png' });
  });

  test('should switch to purchase history tab and display table', async ({ page }) => {
    // Open first product detail
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Click on purchase history tab
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    
    // Wait for tab content to switch
    await page.waitForTimeout(500);
    
    // Verify purchase history tab is now active
    await expect(purchaseHistoryTab).toHaveClass(/border-indigo-500/);
    await expect(purchaseHistoryTab).toHaveClass(/text-indigo-600/);
    
    // Verify basic info tab is no longer active
    const basicInfoTab = page.locator('button', { hasText: 'Základní informace' });
    await expect(basicInfoTab).toHaveClass(/border-transparent/);
    
    // Check if purchase history content is displayed
    const historyTitle = page.locator('h3', { hasText: 'Historie nákupů' });
    
    // We should either see the history table or empty state
    const historyTable = page.locator('table').nth(1); // Second table (first is catalog list)
    const emptyState = page.locator('text=Žádná historie nákupů');
    
    const hasTable = await historyTable.isVisible();
    const hasEmptyState = await emptyState.isVisible();
    
    expect(hasTable || hasEmptyState).toBeTruthy();
    
    if (hasTable) {
      // Verify table headers
      await expect(page.locator('th', { hasText: 'Datum' })).toBeVisible();
      await expect(page.locator('th', { hasText: 'Dodavatel' })).toBeVisible();
      await expect(page.locator('th', { hasText: 'Množství' })).toBeVisible();
      await expect(page.locator('th', { hasText: 'Cena/ks' })).toBeVisible();
      await expect(page.locator('th', { hasText: 'Celkem' })).toBeVisible();
      await expect(page.locator('th', { hasText: 'Číslo dokladu' })).toBeVisible();
      
      // Verify summary section
      await expect(page.locator('text=Celkové nákupy')).toBeVisible();
      await expect(page.locator('text=Celková hodnota')).toBeVisible();
      await expect(page.locator('text=Průměrná cena')).toBeVisible();
      
      console.log('Purchase history table found with data');
    } else {
      // Verify empty state
      await expect(emptyState).toBeVisible();
      await expect(page.locator('text=Pro tento produkt není k dispozici historie nákupů')).toBeVisible();
      console.log('Empty purchase history state displayed');
    }
    
    // Take screenshot of purchase history tab
    await page.screenshot({ path: 'test-results/catalog-detail-purchase-history-tab.png' });
  });

  test('should switch between tabs maintaining state', async ({ page }) => {
    // Open first product detail
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Verify we start on basic info tab
    const basicInfoTab = page.locator('button', { hasText: 'Základní informace' });
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    
    await expect(basicInfoTab).toHaveClass(/border-indigo-500/);
    await expect(page.locator('h3', { hasText: 'Základní informace' })).toBeVisible();
    
    // Switch to purchase history tab
    await purchaseHistoryTab.click();
    await page.waitForTimeout(500);
    
    // Verify tab switched
    await expect(purchaseHistoryTab).toHaveClass(/border-indigo-500/);
    await expect(basicInfoTab).toHaveClass(/border-transparent/);
    
    // Verify content switched
    const historyContent = page.locator('h3', { hasText: 'Historie nákupů' });
    const basicInfoContent = page.locator('h3', { hasText: 'Základní informace' });
    
    // History should be visible, basic info should not
    await expect(historyContent.or(page.locator('text=Žádná historie nákupů'))).toBeVisible();
    await expect(basicInfoContent).not.toBeVisible();
    
    // Switch back to basic info tab
    await basicInfoTab.click();
    await page.waitForTimeout(500);
    
    // Verify we're back to basic info
    await expect(basicInfoTab).toHaveClass(/border-indigo-500/);
    await expect(purchaseHistoryTab).toHaveClass(/border-transparent/);
    await expect(page.locator('h3', { hasText: 'Základní informace' })).toBeVisible();
    
    // Take screenshot of tab switching
    await page.screenshot({ path: 'test-results/catalog-detail-tab-switching.png' });
  });

  test('should maintain chart visibility in right column', async ({ page }) => {
    // Open first product detail
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Verify chart section is visible on both tabs
    const chartSection = page.locator('text=za posledních 13 měsíců');
    const summarySection = page.locator('text=Celkové shrnutí');
    
    // Should be visible on basic info tab
    await expect(chartSection.first()).toBeVisible();
    await expect(summarySection).toBeVisible();
    
    // Switch to purchase history tab
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    await page.waitForTimeout(500);
    
    // Chart should still be visible
    await expect(chartSection.first()).toBeVisible();
    await expect(summarySection).toBeVisible();
    
    // Take screenshot showing right column persistence
    await page.screenshot({ path: 'test-results/catalog-detail-chart-persistence.png' });
  });

  test('should handle responsive layout with tabs', async ({ page }) => {
    // Open first product detail
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Test desktop view
    await page.setViewportSize({ width: 1200, height: 800 });
    await page.waitForTimeout(500);
    
    // Verify tabs are visible on desktop
    await expect(page.locator('button', { hasText: 'Základní informace' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'Historie nákupů' })).toBeVisible();
    
    await page.screenshot({ path: 'test-results/catalog-detail-tabs-desktop.png' });
    
    // Test tablet view
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.waitForTimeout(500);
    
    // Tabs should still be accessible
    await expect(page.locator('button', { hasText: 'Základní informace' })).toBeVisible();
    await expect(page.locator('button', { hasText: 'Historie nákupů' })).toBeVisible();
    
    await page.screenshot({ path: 'test-results/catalog-detail-tabs-tablet.png' });
    
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 });
    await page.waitForTimeout(500);
    
    // Tabs should be accessible on mobile
    await expect(page.locator('button', { hasText: 'Základní informace' })).toBeVisible();
    
    await page.screenshot({ path: 'test-results/catalog-detail-tabs-mobile.png' });
  });

  test('should close modal with Escape key and close button', async ({ page }) => {
    // Open first product detail
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Verify modal is open
    const modal = page.locator('.fixed.inset-0.bg-black.bg-opacity-50');
    await expect(modal).toBeVisible();
    
    // Test Escape key
    await page.keyboard.press('Escape');
    await page.waitForTimeout(500);
    
    // Modal should be closed
    await expect(modal).not.toBeVisible();
    
    // Open modal again
    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Test close button - look for the X close button in the modal header
    const closeButton = page.locator('.bg-white.rounded-lg.shadow-xl button').first(); // Close button in modal
    await closeButton.click();
    await page.waitForTimeout(500);
    
    // Modal should be closed
    await expect(modal).not.toBeVisible();
    
    // Take screenshot of closed state
    await page.screenshot({ path: 'test-results/catalog-detail-modal-closed.png' });
  });

  test('should handle keyboard navigation within tabs', async ({ page }) => {
    // Open first product detail
    const table = page.locator('table tbody');
    const rowCount = await table.locator('tr').count();
    if (rowCount === 0) {
      console.log('No catalog data available for testing');
      return;
    }

    await table.locator('tr').first().click();
    await page.waitForTimeout(1000);
    
    // Focus on first tab
    const basicInfoTab = page.locator('button', { hasText: 'Základní informace' });
    await basicInfoTab.focus();
    await expect(basicInfoTab).toBeFocused();
    
    // Navigate to second tab with Tab key
    await page.keyboard.press('Tab');
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await expect(purchaseHistoryTab).toBeFocused();
    
    // Activate tab with Enter key
    await page.keyboard.press('Enter');
    await page.waitForTimeout(500);
    
    // Verify tab was activated
    await expect(purchaseHistoryTab).toHaveClass(/border-indigo-500/);
    
    // Navigate back with Shift+Tab
    await page.keyboard.press('Shift+Tab');
    await expect(basicInfoTab).toBeFocused();
    
    // Activate with Space key
    await page.keyboard.press(' ');
    await page.waitForTimeout(500);
    
    // Verify tab was activated
    await expect(basicInfoTab).toHaveClass(/border-indigo-500/);
    
    await page.screenshot({ path: 'test-results/catalog-detail-keyboard-navigation.png' });
  });
});