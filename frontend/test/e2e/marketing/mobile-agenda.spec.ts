import { test, expect } from '@playwright/test';
import { navigateToMarketingCalendar } from '../helpers/e2e-auth-helper';

test.describe('Marketing Calendar — Mobile Agenda View', () => {
  test.use({ viewport: { width: 375, height: 812 } });

  test.beforeEach(async ({ page }) => {
    await navigateToMarketingCalendar(page);
  });

  test('renders the mobile agenda view, not the desktop calendar grid', async ({ page }) => {
    // MobileAgendaView uses the short heading "Kalendář" (desktop uses "Marketingový kalendář")
    await expect(page.locator('h1.mobile-agenda__title')).toHaveText('Kalendář', { timeout: 10000 });
    // Desktop month calendar must NOT be present
    await expect(page.locator('[data-testid="marketing-month-calendar"]')).not.toBeVisible();
    // Desktop view toggle buttons must NOT be present
    await expect(page.locator('button').filter({ hasText: '5 týdnů' })).not.toBeVisible();
  });

  test('renders at least one day section header', async ({ page }) => {
    await expect(page.locator('.agenda-day-group__header').first()).toBeVisible({ timeout: 10000 });
  });

  test('loading spinner resolves and 14 day sections appear', async ({ page }) => {
    await expect(page.locator('.agenda-day-group').first()).toBeVisible({ timeout: 15000 });
    const dayGroups = page.locator('.agenda-day-group');
    await expect(dayGroups).toHaveCount(14, { timeout: 10000 });
  });

  test('prev navigation shifts the window back', async ({ page }) => {
    await expect(page.locator('.agenda-day-group').first()).toBeVisible({ timeout: 15000 });

    const firstHeaderBefore = await page.locator('.agenda-day-group__header').first().textContent();

    await page.locator('button[aria-label="Předchozí"]').click();

    await expect(page.locator('.agenda-day-group').first()).toBeVisible({ timeout: 10000 });

    const firstHeaderAfter = await page.locator('.agenda-day-group__header').first().textContent();
    expect(firstHeaderAfter).not.toBe(firstHeaderBefore);
  });

  test('+ button opens create modal with Vytvořit submit', async ({ page }) => {
    await page.locator('button[aria-label="Nová akce"]').click();
    await expect(page.locator('text=Nová marketingová akce')).toBeVisible({ timeout: 5000 });
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible();
    await expect(page.locator('button').filter({ hasText: 'Smazat' })).not.toBeVisible();
  });

  test('create modal dismisses via Zrušit', async ({ page }) => {
    await page.locator('button[aria-label="Nová akce"]').click();
    await expect(page.locator('text=Nová marketingová akce')).toBeVisible({ timeout: 5000 });
    await page.locator('button').filter({ hasText: 'Zrušit' }).first().click();
    await expect(page.locator('text=Nová marketingová akce')).not.toBeVisible({ timeout: 3000 });
  });

  test('tapping an event card opens the edit modal', async ({ page }) => {
    await expect(page.locator('.agenda-day-group').first()).toBeVisible({ timeout: 15000 });

    const cards = page.locator('.agenda-event-card');
    const count = await cards.count();

    if (count === 0) {
      console.log('No events in current 14-day window — skipping card tap test');
      return;
    }

    await cards.first().click();
    await expect(page.locator('button').filter({ hasText: 'Uložit' }).first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('button').filter({ hasText: 'Smazat' }).first()).toBeVisible();
  });

  test('resizing to desktop width shows the grid, not the agenda', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    // Wait for React to re-render
    await page.waitForTimeout(300);
    // Desktop calendar grid present; mobile agenda heading gone
    await expect(page.locator('button').filter({ hasText: '5 týdnů' })).toBeVisible({ timeout: 5000 });
    await expect(page.locator('h1').filter({ hasText: 'Marketingový kalendář' })).toBeVisible();
  });
});
