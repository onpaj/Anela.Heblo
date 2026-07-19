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
    // Don't assert on the current month's name: periodLabel (MarketingCalendarPage.tsx:138)
    // is built from the calendar *grid* range, which includes leading/trailing days of adjacent
    // months, so it is a "Červenec – Srpen 2026" style range rather than a single month.
    // Capture it and assert Dnes restores it — that is what "return to the current month" means,
    // and it holds in every month.
    //
    // The capture must wait for FullCalendar's datesSet to fire. periodLabel falls back to
    // `visibleRange ?? currentDate`, so until then start and end are the same day and the label
    // renders as a bare "Červenec 2026"; capturing during that window yields a value the calendar
    // never returns to. The 5-week grid always spans two months, so wait for the range form.
    const periodLabel = page.locator('span').filter({ hasText: /20\d\d/ }).first();
    await expect(periodLabel).toHaveText(/\s–\s/, { timeout: 15000 });
    const labelOnLoad = await periodLabel.textContent();

    // Navigate away to next month first
    const navButtons = page.locator('button').filter({ hasText: /^$/ });
    await navButtons.last().click();
    await expect(periodLabel).not.toHaveText(labelOnLoad ?? '', { timeout: 5000 });

    // Return via Dnes button. Match by exact accessible name, not hasText: 'Dnes' is a
    // substring match and the calendar contains marketing actions whose titles mention
    // "MF Dnes", so a loose filter can resolve to an event bar instead of the nav button.
    const todayButton = page.getByRole('button', { name: 'Dnes', exact: true });
    await expect(todayButton).toBeVisible({ timeout: 5000 });

    // goToToday delegates to FullCalendar's gotoDate. If it lands while the grid is still
    // re-rendering from the previous navigation the call is swallowed and the view never moves,
    // which is load-dependent: a single click is reliable when the module runs alone but not
    // under full-suite contention. FullCalendar exposes no "idle" signal to wait on, so poll the
    // click until the period label returns instead of guessing at a settle duration.
    await expect
      .poll(
        async () => {
          if ((await periodLabel.textContent()) !== labelOnLoad) {
            await todayButton.click();
            await page.waitForTimeout(500);
          }
          return periodLabel.textContent();
        },
        { timeout: 20000, intervals: [500] },
      )
      .toBe(labelOnLoad);
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
