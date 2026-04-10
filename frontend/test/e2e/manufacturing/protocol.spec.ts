import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Manufacture Protocol PDF', () => {
  test('should show Tisk protokolu button for a completed order and open PDF', async ({
    page,
    context,
  }) => {
    await navigateToApp(page);

    // Navigate to manufacturing orders list and filter by Completed state
    await page.goto('/manufacturing/orders');
    await page.waitForLoadState('networkidle');

    // Apply Completed filter – the filter dropdown label may vary; try common selectors
    const stateSelect = page.locator('select').first();
    if (await stateSelect.isVisible()) {
      await stateSelect.selectOption('Completed');
      await page.waitForLoadState('networkidle');
    }

    // Find a row that contains the Completed state badge
    const completedRow = page
      .locator('tr, [role="row"]')
      .filter({ hasText: /Completed|Dokončeno/i })
      .first();

    const rowCount = await completedRow.count();
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No completed manufacture order found in staging. ' +
          'Create at least one completed manufacture order first.'
      );
    }

    // Click the row to open the detail
    await completedRow.click();
    await page.waitForLoadState('networkidle');

    // Verify "Tisk protokolu" button is visible in the detail panel/page
    const printButton = page.getByTitle('Tisknout protokol výroby');
    await expect(printButton).toBeVisible({ timeout: 10000 });

    // Click the button and wait for a new tab/popup to open
    const [newPage] = await Promise.all([
      context.waitForEvent('page'),
      printButton.click(),
    ]);

    // The new tab should open with a blob URL
    await newPage.waitForLoadState();
    const newPageUrl = newPage.url();
    expect(newPageUrl).toMatch(/^blob:/);
  });

  test('should NOT show Tisk protokolu button for a non-completed order', async ({ page }) => {
    await navigateToApp(page);

    await page.goto('/manufacturing/orders');
    await page.waitForLoadState('networkidle');

    // Filter for Draft orders
    const stateSelect = page.locator('select').first();
    if (await stateSelect.isVisible()) {
      await stateSelect.selectOption('Draft');
      await page.waitForLoadState('networkidle');
    }

    const draftRow = page
      .locator('tr, [role="row"]')
      .filter({ hasText: /Draft|Návrh/i })
      .first();

    const rowCount = await draftRow.count();
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No draft manufacture order found in staging. ' +
          'Create at least one draft manufacture order first.'
      );
    }

    await draftRow.click();
    await page.waitForLoadState('networkidle');

    // "Tisk protokolu" must not be present for a non-completed order
    const printButton = page.getByTitle('Tisknout protokol výroby');
    await expect(printButton).not.toBeVisible({ timeout: 5000 });
  });
});
