import { test, expect } from '@playwright/test';
import { navigateToMarketingCalendar } from '../helpers/e2e-auth-helper';

// Action type labels as rendered in MarketingActionModal dropdown (Czech)
const ACTION_TYPE_OPTIONS = ['Sociální sítě', 'Událost', 'Email', 'PR', 'Fotografie', 'Ostatní'];

test.describe('Marketing Calendar — Create New Record', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToMarketingCalendar(page);
  });

  test('should open create modal when clicking Nová akce', async ({ page }) => {
    await page.locator('button').filter({ hasText: 'Nová akce' }).click();

    // Create mode shows Vytvořit (not Uložit)
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 5000 });
    await expect(page.locator('button').filter({ hasText: 'Zrušit' }).first()).toBeVisible();

    // Delete button must NOT appear in create mode
    await expect(page.locator('button').filter({ hasText: 'Smazat' }).first()).not.toBeVisible();
  });

  test('should display all required form fields', async ({ page }) => {
    await page.locator('button').filter({ hasText: 'Nová akce' }).click();
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 5000 });

    // Title input
    const titleInput = page.locator('input').filter({ hasNot: page.locator('[type="date"]') }).first();
    await expect(titleInput).toBeVisible();

    // Type dropdown
    await expect(page.locator('select').first()).toBeVisible();

    // Od / Do date inputs
    const dateInputs = page.locator('input[type="date"]');
    await expect(dateInputs.first()).toBeVisible();
    await expect(dateInputs.nth(1)).toBeVisible();

    // Description textarea
    await expect(page.locator('textarea').first()).toBeVisible();
  });

  test('should list all action type options in the dropdown', async ({ page }) => {
    await page.locator('button').filter({ hasText: 'Nová akce' }).click();
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 5000 });

    const typeSelect = page.locator('select').first();

    for (const label of ACTION_TYPE_OPTIONS) {
      await expect(typeSelect.locator(`option:has-text("${label}")`)).toHaveCount(1);
    }
  });

  test('should dismiss modal via Zrušit without saving', async ({ page }) => {
    await page.locator('button').filter({ hasText: 'Nová akce' }).click();
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 5000 });

    await page.locator('button').filter({ hasText: 'Zrušit' }).first().click();

    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).not.toBeVisible({ timeout: 3000 });
  });

  test('should keep modal open when submitting without a title', async ({ page }) => {
    await page.locator('button').filter({ hasText: 'Nová akce' }).click();
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 5000 });

    // Fill dates but leave title empty
    const dateInputs = page.locator('input[type="date"]');
    await dateInputs.first().fill('2099-06-01');
    await dateInputs.nth(1).fill('2099-06-30');

    await page.locator('button').filter({ hasText: 'Vytvořit' }).first().click();

    // Modal must remain open — title is required
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 3000 });
  });

  test('should create a new marketing action and verify it appears in grid view', async ({ page }) => {
    const uniqueTitle = `E2E Test Akce ${Date.now()}`;

    await page.locator('button').filter({ hasText: 'Nová akce' }).click();
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 5000 });

    // Fill title — first text input in the modal
    const titleInput = page.locator('input[type="text"]').first();
    await titleInput.fill(uniqueTitle);

    // Select action type
    await page.locator('select').first().selectOption({ label: 'Událost' });

    // Set date range (far future to avoid polluting visible calendar months)
    const dateInputs = page.locator('input[type="date"]');
    await dateInputs.first().fill('2099-06-01');
    await dateInputs.nth(1).fill('2099-06-30');

    // Submit
    await page.locator('button').filter({ hasText: 'Vytvořit' }).first().click();

    // Modal closes after successful creation
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).not.toBeVisible({ timeout: 10000 });

    // Switch to grid view to verify the record was persisted
    const listToggle = page.locator('button').filter({ hasText: 'Seznam' }).first();
    await listToggle.click();
    await expect(listToggle).toHaveClass(/bg-indigo-600/);

    // Search for the newly created action by title
    const searchInput = page.locator('input[type="text"]').first();
    await searchInput.fill(uniqueTitle);
    await page.waitForTimeout(1500);

    const matchingRow = page.locator('tbody tr').filter({ hasText: uniqueTitle });

    if (await matchingRow.count() === 0) {
      throw new Error(
        `Newly created action "${uniqueTitle}" not found in grid after creation. ` +
        'Either the API call failed or the grid is not refreshing after create.'
      );
    }

    await expect(matchingRow.first()).toBeVisible();
  });

  test('should create an action with all optional fields filled', async ({ page }) => {
    const uniqueTitle = `E2E Plný formulář ${Date.now()}`;

    await page.locator('button').filter({ hasText: 'Nová akce' }).click();
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).toBeVisible({ timeout: 5000 });

    // Title
    await page.locator('input[type="text"]').first().fill(uniqueTitle);

    // Type
    await page.locator('select').first().selectOption({ label: 'Email' });

    // Dates
    const dateInputs = page.locator('input[type="date"]');
    await dateInputs.first().fill('2099-07-01');
    await dateInputs.nth(1).fill('2099-07-15');

    // Description
    await page.locator('textarea').first().fill('E2E test description — generated by automated test');

    // Add a product code if the product input is present
    const productInput = page.locator('input[placeholder*="kód"], input[placeholder*="Kód"]').first();
    const hasProductInput = await productInput.isVisible({ timeout: 2000 }).catch(() => false);
    if (hasProductInput) {
      await productInput.fill('AKL001');
      const addButton = page.locator('button').filter({ hasText: 'Přidat' }).first();
      await addButton.click();
      // Badge for the added product should appear
      await expect(page.locator('text=AKL001').first()).toBeVisible({ timeout: 3000 });
    }

    // Submit
    await page.locator('button').filter({ hasText: 'Vytvořit' }).first().click();

    // Modal closes on success
    await expect(page.locator('button').filter({ hasText: 'Vytvořit' }).first()).not.toBeVisible({ timeout: 10000 });
  });
});
