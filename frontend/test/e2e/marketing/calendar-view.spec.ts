import { test, expect } from '@playwright/test';
import { navigateToMarketingCalendar } from '../helpers/e2e-auth-helper';

test.describe('Marketing Calendar — Calendar View', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToMarketingCalendar(page);

    // Ensure calendar view is active (it is the default, but be explicit)
    const calendarToggle = page.locator('button').filter({ hasText: 'Kalendář' }).first();
    await calendarToggle.click();
    await expect(calendarToggle).toHaveClass(/bg-indigo-600/);
  });

  test('should render day-of-week header row', async ({ page }) => {
    // Calendar grid must show Czech weekday abbreviations Mon–Sun
    const expectedHeaders = ['Po', 'Út', 'St', 'Čt', 'Pá', 'So', 'Ne'];
    for (const day of expectedHeaders) {
      await expect(page.locator(`text="${day}"`).first()).toBeVisible({ timeout: 10000 });
    }
  });

  test('should render at least one calendar day cell', async ({ page }) => {
    // Wait for loading to finish
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });

    // Day numbers 1–31 appear in the calendar grid
    const dayOne = page.locator('text="1"').first();
    await expect(dayOne).toBeVisible({ timeout: 10000 });
  });

  test('should navigate to previous month', async ({ page }) => {
    const czechMonths = [
      'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
      'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
    ];

    const now = new Date();
    const prevMonthIndex = now.getMonth() === 0 ? 11 : now.getMonth() - 1;
    const prevMonthName = czechMonths[prevMonthIndex];
    const prevYear = now.getMonth() === 0 ? now.getFullYear() - 1 : now.getFullYear();

    // CalendarNavigation renders prev/next as icon buttons; locate by position in nav row
    // The period label sits between the navigation buttons — click the first button before it
    const periodLabel = page.locator(`text=/${czechMonths[now.getMonth()]}/`).first();
    await expect(periodLabel).toBeVisible({ timeout: 10000 });

    // Prev button is the first sibling button before the period label
    const navButtons = page.locator('button').filter({ hasText: /^$/ }); // icon-only buttons
    await navButtons.first().click();

    await expect(page.locator(`text=/${prevMonthName}/`).first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator(`text=/${prevYear}/`).first()).toBeVisible();
  });

  test('should navigate to next month', async ({ page }) => {
    const czechMonths = [
      'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
      'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
    ];

    const now = new Date();
    const nextMonthIndex = (now.getMonth() + 1) % 12;
    const nextMonthName = czechMonths[nextMonthIndex];
    const nextYear = now.getMonth() === 11 ? now.getFullYear() + 1 : now.getFullYear();

    // Next button follows the period label — it's the last icon-only button in the nav row
    const navButtons = page.locator('button').filter({ hasText: /^$/ });
    await navButtons.last().click();

    await expect(page.locator(`text=/${nextMonthName}/`).first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator(`text=/${nextYear}/`).first()).toBeVisible();
  });

  test('should return to current month when clicking Dnes', async ({ page }) => {
    const czechMonths = [
      'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
      'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
    ];
    const currentMonthName = czechMonths[new Date().getMonth()];

    // Navigate away to next month first
    const navButtons = page.locator('button').filter({ hasText: /^$/ });
    await navButtons.last().click();

    // Return via Dnes button
    const todayButton = page.locator('button').filter({ hasText: 'Dnes' }).first();
    await expect(todayButton).toBeVisible({ timeout: 5000 });
    await todayButton.click();

    await expect(page.locator(`text=/${currentMonthName}/`).first()).toBeVisible({ timeout: 5000 });
  });

  test('should open edit modal when clicking an event bar', async ({ page }) => {
    // Event bars are only present when there are marketing actions in the current month.
    // Locate by background-color style (MarketingEventBar sets inline bg colour by action type).
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });

    const eventBars = page.locator('[style*="background-color"]').filter({ hasText: /.+/ });
    const count = await eventBars.count();

    if (count === 0) {
      console.log('No event bars found in current month — skipping click test');
      return;
    }

    await eventBars.first().click();

    // Edit modal opens — Uložit and Zrušit buttons appear
    await expect(page.locator('button').filter({ hasText: 'Uložit' }).first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('button').filter({ hasText: 'Zrušit' }).first()).toBeVisible();
  });

  test('should close edit modal via Zrušit', async ({ page }) => {
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });

    const eventBars = page.locator('[style*="background-color"]').filter({ hasText: /.+/ });
    const count = await eventBars.count();

    if (count === 0) {
      console.log('No event bars found — skipping close-modal test');
      return;
    }

    await eventBars.first().click();

    const cancelButton = page.locator('button').filter({ hasText: 'Zrušit' }).first();
    await expect(cancelButton).toBeVisible({ timeout: 5000 });
    await cancelButton.click();

    // Modal dismissed — Zrušit button no longer visible
    await expect(page.locator('button').filter({ hasText: 'Zrušit' }).first()).not.toBeVisible({ timeout: 3000 });
  });
});
