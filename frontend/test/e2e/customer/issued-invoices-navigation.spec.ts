import { test, expect } from '@playwright/test';
import { navigateToIssuedInvoices } from '../helpers/e2e-auth-helper';
import { waitForLoadingComplete } from '../helpers/wait-helpers';

test.describe('IssuedInvoices - Navigation and Tab Switching', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);
  });

  test('Test 1: Page loads successfully with authentication', async ({ page }) => {
    // Verify URL
    expect(page.url()).toContain('/customer/issued-invoices');

    // Verify page title
    const pageTitle = page.locator('h1:has-text("Vydané faktury")');
    await expect(pageTitle).toBeVisible({ timeout: 10000 });

    // Verify Statistics tab is active by default
    const statisticsTab = page.locator('button:has-text("Statistiky")');
    await expect(statisticsTab).toHaveClass(/border-indigo-500/);

    // Verify content loaded (no errors)
    const errorMessage = page.locator('text=/Error|Chyba/i').first();
    await expect(errorMessage).not.toBeVisible();
  });

  test('Test 2: Tab switching works correctly (Statistics ↔ Grid)', async ({ page }) => {
    await waitForLoadingComplete(page);

    // Verify Statistics tab is active initially
    const statisticsTab = page.locator('button:has-text("Statistiky")');
    const gridTab = page.locator('button:has-text("Seznam")');

    await expect(statisticsTab).toHaveClass(/border-indigo-500/);
    await expect(gridTab).not.toHaveClass(/border-indigo-500/);

    // Switch to Grid tab
    await gridTab.click();
    await waitForLoadingComplete(page);

    // Verify Grid tab is now active
    await expect(gridTab).toHaveClass(/border-indigo-500/);
    await expect(statisticsTab).not.toHaveClass(/border-indigo-500/);

    // Verify grid content is visible
    const filterSection = page.locator('text="Filtry:"');
    await expect(filterSection).toBeVisible();

    // Switch back to Statistics tab
    await statisticsTab.click();
    await waitForLoadingComplete(page);

    // Verify Statistics tab is active again
    await expect(statisticsTab).toHaveClass(/border-indigo-500/);
    await expect(gridTab).not.toHaveClass(/border-indigo-500/);

    // Verify statistics content is visible
    const summaryCard = page.locator('text="Celkem faktur"');
    await expect(summaryCard).toBeVisible();
  });
});
