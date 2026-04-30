import { test, expect } from '@playwright/test';
import { navigateToApp, navigateToMarketingCalendar } from '../helpers/e2e-auth-helper';

test.describe('Marketing Calendar — Page Loading', () => {
  test('should navigate to marketing calendar via sidebar', async ({ page }) => {
    await navigateToApp(page);

    // Expand Marketing section in sidebar
    const marketingSection = page.locator('button').filter({ hasText: 'Marketing' }).first();
    await expect(marketingSection).toBeVisible({ timeout: 10000 });
    await marketingSection.click();

    // Click the Kalendář sub-item
    const calendarLink = page.locator('a[href="/marketing/calendar"], text="Kalendář"').first();
    await expect(calendarLink).toBeVisible({ timeout: 5000 });
    await calendarLink.click();

    await page.waitForLoadState('domcontentloaded');

    await expect(page.locator('h1').filter({ hasText: 'Marketingový kalendář' })).toBeVisible({ timeout: 10000 });
  });

  test('should display page heading and toolbar controls', async ({ page }) => {
    await navigateToMarketingCalendar(page);

    await expect(page.locator('h1').filter({ hasText: 'Marketingový kalendář' })).toBeVisible();

    // View toggle buttons
    await expect(page.locator('button').filter({ hasText: 'Kalendář' }).first()).toBeVisible();
    await expect(page.locator('button').filter({ hasText: 'Seznam' }).first()).toBeVisible();

    // New action button
    await expect(page.locator('button').filter({ hasText: 'Nová akce' })).toBeVisible();
  });

  test('should load calendar view by default', async ({ page }) => {
    await navigateToMarketingCalendar(page);

    // Calendar toggle should have active (indigo) styling
    const calendarToggle = page.locator('button').filter({ hasText: 'Kalendář' }).first();
    await expect(calendarToggle).toHaveClass(/bg-indigo-600/);

    // List toggle should not be active
    const listToggle = page.locator('button').filter({ hasText: 'Seznam' }).first();
    await expect(listToggle).not.toHaveClass(/bg-indigo-600/);
  });

  test('should display current month and year in calendar header', async ({ page }) => {
    await navigateToMarketingCalendar(page);

    const czechMonths = [
      'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
      'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
    ];
    const currentMonthName = czechMonths[new Date().getMonth()];
    const currentYear = new Date().getFullYear().toString();

    // Period label must contain current month name
    await expect(page.locator(`text=/${currentMonthName}/`).first()).toBeVisible({ timeout: 10000 });

    // Period label must contain current year
    await expect(page.locator(`text=/${currentYear}/`).first()).toBeVisible();
  });

  test('should finish loading without error state', async ({ page }) => {
    await navigateToMarketingCalendar(page);

    // Wait up to 15s for loading text to disappear
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });

    // Error state should not be shown
    await expect(page.locator('text=Chyba při načítání kalendáře.').first()).not.toBeVisible();
  });
});
