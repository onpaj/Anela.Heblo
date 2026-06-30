import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

const BASE_URL =
  process.env.PLAYWRIGHT_FRONTEND_URL ||
  process.env.PLAYWRIGHT_BASE_URL ||
  'https://heblo.stg.anela.cz';

// The /manufacturing/orders page defaults to the weekly calendar (no table rows); force the grid
// view and filter by state so the first row is guaranteed to be in the wanted state. The state
// <select> option values are the raw enum strings (Draft, Planned, SemiProductManufactured,
// Completed, Cancelled), independent of the Czech labels.
async function gotoOrdersGridFilteredByState(
  page: import('@playwright/test').Page,
  stateValue: string,
): Promise<void> {
  await navigateToApp(page);
  await page.goto(`${BASE_URL}/manufacturing/orders?view=grid`);
  await page.waitForLoadState('domcontentloaded');
  // The filter panel is collapsed by default.
  await page.getByRole('button', { name: 'Filtry' }).click();
  await page.locator('select').first().selectOption({ value: stateValue });
  await page.getByRole('button', { name: 'Použít filtry' }).click();
  await expect(
    page.locator('tr').filter({ hasText: /MO-/ }).first(),
  ).toBeVisible({ timeout: 15000 });
}

test.describe('Manufacture Protocol PDF', () => {
  test('should show Tisk protokolu button for a completed order and open PDF', async ({ page }) => {
    await gotoOrdersGridFilteredByState(page, 'Completed');

    // Every visible row is now a completed order — open the first one.
    await page.locator('tr').filter({ hasText: /MO-/ }).first().click();
    await page.waitForLoadState('domcontentloaded');

    const printButton = page.getByTitle('Tisknout protokol výroby');
    await expect(printButton).toBeVisible({ timeout: 10000 });

    // Clicking fetches /api/manufactureorder/{id}/protocol.pdf and opens it as a blob in a new tab.
    // The blob popup is opened with noopener, so assert on the PDF network response instead.
    const [pdfResponse] = await Promise.all([
      page.waitForResponse((r) => /\/protocol\.pdf/.test(r.url()), { timeout: 30000 }),
      printButton.click(),
    ]);
    expect(pdfResponse.status()).toBe(200);
    expect(pdfResponse.headers()['content-type']).toContain('pdf');
  });

  test('should NOT show Tisk protokolu button for a non-completed order', async ({ page }) => {
    await gotoOrdersGridFilteredByState(page, 'Draft');

    // Every visible row is now a Draft order — open the first one.
    await page.locator('tr').filter({ hasText: /MO-/ }).first().click();
    await page.waitForLoadState('domcontentloaded');

    const printButton = page.getByTitle('Tisknout protokol výroby');
    await expect(printButton).not.toBeVisible({ timeout: 5000 });
  });
});
