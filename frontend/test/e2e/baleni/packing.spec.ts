import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Balení — packing screen', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    await page.goto('/baleni/baleni');
  });

  test('shows the empty state and scan input', async ({ page }) => {
    await expect(page.getByText('Naskenujte číslo objednávky')).toBeVisible();
    await expect(page.getByRole('textbox')).toBeFocused();
  });

  test('shows a not-found message for an unknown order code', async ({ page }) => {
    const input = page.getByRole('textbox');
    await input.fill('00000000');
    await input.press('Enter');

    await expect(page.getByText('Objednávka nenalezena')).toBeVisible({ timeout: 15000 });
  });
});
