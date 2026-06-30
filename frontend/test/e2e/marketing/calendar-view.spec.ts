import { test, expect } from '@playwright/test';
import { navigateToMarketingCalendar } from '../helpers/e2e-auth-helper';

test.describe('Marketing Calendar — Calendar View', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToMarketingCalendar(page);

    // Ensure calendar view is active (it is the default, but be explicit)
    const calendarToggle = page.locator('button').filter({ hasText: '5 týdnů' }).first();
    await calendarToggle.click();
    await expect(calendarToggle).toHaveClass(/bg-indigo-600/);
  });

  test('should render day-of-week header row', async ({ page }) => {
    // FullCalendar (cs locale) renders weekday column headers with full Czech day names
    const expectedHeaders = ['pondělí', 'úterý', 'středa', 'čtvrtek', 'pátek', 'sobota', 'neděle'];
    for (const day of expectedHeaders) {
      await expect(page.getByRole('columnheader', { name: day }).first()).toBeVisible({ timeout: 10000 });
    }
  });

  test('should render at least one calendar day cell', async ({ page }) => {
    // Wait for loading to finish
    await expect(page.locator('text=Načítání...').first()).not.toBeVisible({ timeout: 15000 });

    // The calendar grid renders day cells as grid cells
    const dayCell = page.getByRole('gridcell').first();
    await expect(dayCell).toBeVisible({ timeout: 10000 });
  });

  test('should navigate to previous month', async ({ page }) => {
    // The period label shows the visible month/range with the year, e.g. "Červen – Červenec 2026".
    // (The 5-week grid spans parts of two months, so the label is a start–end range, not a single
    // month name — assert the label changes after navigating rather than guessing the exact text.)
    const periodLabel = page.locator('span').filter({ hasText: /20\d\d/ }).first();
    await expect(periodLabel).toBeVisible({ timeout: 10000 });
    const labelBefore = await periodLabel.textContent();

    // CalendarNavigation renders the prev button with accessible name "Předchozí"
    await page.getByRole('button', { name: 'Předchozí' }).click();

    // Navigating to the previous period updates the visible period label
    await expect(periodLabel).not.toHaveText(labelBefore ?? '', { timeout: 5000 });
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
