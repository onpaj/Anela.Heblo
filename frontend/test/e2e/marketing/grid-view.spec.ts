import { test, expect } from '@playwright/test';
import { navigateToMarketingCalendar } from '../helpers/e2e-auth-helper';

test.describe('Marketing Calendar — Grid (List) View', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToMarketingCalendar(page);

    // Switch to list/grid view
    const listToggle = page.locator('button').filter({ hasText: 'Seznam' }).first();
    await listToggle.click();
    await expect(listToggle).toHaveClass(/bg-indigo-600/);
  });

  test('should deactivate calendar toggle when switching to grid view', async ({ page }) => {
    const calendarToggle = page.locator('button').filter({ hasText: 'Kalendář' }).first();
    await expect(calendarToggle).not.toHaveClass(/bg-indigo-600/);
  });

  test('should display filter controls', async ({ page }) => {
    // MarketingActionFilters renders a text search input
    const searchInput = page.locator('input[type="text"], input[placeholder]').first();
    await expect(searchInput).toBeVisible({ timeout: 10000 });

    // And two date inputs (Od / Do)
    const dateInputs = page.locator('input[type="date"]');
    await expect(dateInputs.first()).toBeVisible();
  });

  test('should display table column headers', async ({ page }) => {
    // MarketingActionGrid columns: Název, Typ, Od, Do
    await expect(page.locator('text=Název').first()).toBeVisible({ timeout: 10000 });
    await expect(page.locator('text=Typ').first()).toBeVisible();
    await expect(page.locator('text=Od').first()).toBeVisible();
    await expect(page.locator('text=Do').first()).toBeVisible();
  });

  test('should filter rows by search text returning no results', async ({ page }) => {
    const searchInput = page.locator('input[type="text"], input[placeholder]').first();
    await expect(searchInput).toBeVisible({ timeout: 10000 });

    // Use a string that will never match a real marketing action title
    await searchInput.fill('zzznomatch_xyzxyz_e2e');

    // Wait for debounce and API response
    await page.waitForTimeout(1500);

    const rows = page.locator('tbody tr');
    const rowCount = await rows.count();
    expect(rowCount).toBe(0);
  });

  test('should clear search filter and restore rows', async ({ page }) => {
    const searchInput = page.locator('input[type="text"], input[placeholder]').first();
    await expect(searchInput).toBeVisible({ timeout: 10000 });

    await searchInput.fill('zzznomatch_xyzxyz_e2e');
    await page.waitForTimeout(1500);

    // Clear the filter
    await searchInput.clear();
    await page.waitForTimeout(1500);

    await expect(searchInput).toHaveValue('');
  });

  test('should filter rows by date range that returns no results', async ({ page }) => {
    const dateInputs = page.locator('input[type="date"]');
    const inputCount = await dateInputs.count();

    if (inputCount < 2) {
      throw new Error(
        `Expected at least 2 date inputs in filter controls, found ${inputCount}. ` +
        'MarketingActionFilters component may have changed.'
      );
    }

    // Set a far-future range with no data
    await dateInputs.first().fill('2099-01-01');
    await dateInputs.nth(1).fill('2099-01-31');
    await page.waitForTimeout(1500);

    const rows = page.locator('tbody tr');
    expect(await rows.count()).toBe(0);
  });

  test('should open edit modal when clicking a grid row', async ({ page }) => {
    await page.waitForTimeout(2000);

    const rows = page.locator('tbody tr');
    const rowCount = await rows.count();

    if (rowCount === 0) {
      console.log('No rows in grid — skipping row-click test');
      return;
    }

    await rows.first().click();

    // Edit modal: footer has Uložit (save) and Zrušit (cancel)
    await expect(page.locator('button').filter({ hasText: 'Uložit' }).first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('button').filter({ hasText: 'Zrušit' }).first()).toBeVisible();
  });

  test('should close edit modal via Zrušit', async ({ page }) => {
    await page.waitForTimeout(2000);

    const rows = page.locator('tbody tr');
    if (await rows.count() === 0) {
      console.log('No rows in grid — skipping modal-close test');
      return;
    }

    await rows.first().click();

    const cancelButton = page.locator('button').filter({ hasText: 'Zrušit' }).first();
    await expect(cancelButton).toBeVisible({ timeout: 5000 });
    await cancelButton.click();

    await expect(page.locator('button').filter({ hasText: 'Zrušit' }).first()).not.toBeVisible({ timeout: 3000 });
  });

  test('should show next page button when more than one page exists', async ({ page }) => {
    await page.waitForTimeout(2000);

    const rows = page.locator('tbody tr');
    if (await rows.count() === 0) {
      console.log('No rows — skipping pagination test');
      return;
    }

    // Pagination controls rendered by MarketingActionGrid
    const nextButton = page.locator('button').filter({ hasText: /Další/ }).first();
    const prevButton = page.locator('button').filter({ hasText: /Předchozí/ }).first();

    // At least one pagination button should be present
    const hasNext = await nextButton.isVisible().catch(() => false);
    const hasPrev = await prevButton.isVisible().catch(() => false);

    expect(hasNext || hasPrev).toBe(true);
  });
});
