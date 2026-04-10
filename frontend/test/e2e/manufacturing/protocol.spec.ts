import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

async function navigateToManufactureOrders(page: import('@playwright/test').Page) {
  await navigateToApp(page);
  await page.getByRole('button', { name: 'Výroba' }).click();
  await page.getByRole('link', { name: 'Zakázky' }).click();
  await page.waitForLoadState('domcontentloaded');
}

async function findAndClickRowByState(
  page: import('@playwright/test').Page,
  statePattern: RegExp,
  maxRows: number = 5,
): Promise<boolean> {
  const rows = page.locator('tr').filter({ hasText: /MO-/ });
  const count = await rows.count();
  const limit = Math.min(count, maxRows);

  for (let i = 0; i < limit; i++) {
    const row = rows.nth(i);
    const text = await row.textContent();
    if (statePattern.test(text ?? '')) {
      await row.click();
      await page.waitForLoadState('domcontentloaded');
      return true;
    }
  }

  return false;
}

test.describe('Manufacture Protocol PDF', () => {
  test('should show Tisk protokolu button for a completed order and open PDF', async ({
    page,
    context,
  }) => {
    await navigateToManufactureOrders(page);

    const found = await findAndClickRowByState(page, /Completed|Dokončeno/i);
    if (!found) {
      throw new Error(
        'Test data missing: No completed manufacture order found in the first 5 rows on staging. ' +
          'Create at least one completed manufacture order first.',
      );
    }

    const printButton = page.getByTitle('Tisknout protokol výroby');
    await expect(printButton).toBeVisible({ timeout: 10000 });

    const [newPage] = await Promise.all([context.waitForEvent('page'), printButton.click()]);

    await newPage.waitForLoadState();
    expect(newPage.url()).toMatch(/^blob:/);
  });

  test('should NOT show Tisk protokolu button for a non-completed order', async ({ page }) => {
    await navigateToManufactureOrders(page);

    const found = await findAndClickRowByState(page, /Draft|Návrh|Planned|Plánováno/i);
    if (!found) {
      throw new Error(
        'Test data missing: No non-completed manufacture order found in the first 5 rows on staging. ' +
          'Create at least one order in Draft or Planned state first.',
      );
    }

    const printButton = page.getByTitle('Tisknout protokol výroby');
    await expect(printButton).not.toBeVisible({ timeout: 5000 });
  });
});
