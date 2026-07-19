import { test, expect, type Page } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

const SCAN_INPUT_LABEL = 'Sken čísla objednávky';

/**
 * The scan input is disabled until a packing user (balič) is selected —
 * see BaleniPacking.tsx `loading={scanMutation.isPending || !current}`.
 * The picker opens automatically on mount when nothing is stored, so every
 * test has to pick an operator before the input becomes usable.
 */
async function selectPackingUser(page: Page): Promise<void> {
  const pickerHeading = page.getByRole('heading', { name: 'Kdo balí?' });
  await expect(pickerHeading).toBeVisible({ timeout: 30000 });

  const userButton = page.locator('div.fixed.inset-0 div.grid button').first();
  await expect(userButton).toBeVisible({ timeout: 15000 });
  await userButton.click();

  await expect(pickerHeading).toBeHidden();
}

test.describe('Balení — packing screen', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    await page.goto('/baleni/baleni');
    await selectPackingUser(page);
  });

  test('shows the empty state and scan input', async ({ page }) => {
    await expect(page.getByText('Naskenujte číslo objednávky')).toBeVisible();
    await expect(page.getByRole('textbox', { name: SCAN_INPUT_LABEL })).toBeFocused();
  });

  test('shows a not-found message for an unknown order code', async ({ page }) => {
    const input = page.getByRole('textbox', { name: SCAN_INPUT_LABEL });
    await input.fill('00000000');
    await input.press('Enter');

    await expect(page.getByText('Objednávka nenalezena')).toBeVisible({ timeout: 15000 });
  });

  // NOTE: three further tests ("Vytvořit zásilku" button, existing-shipment warning,
  // multi-package label confirmation) were removed here. They were written against the
  // pre-4ae8dda4a PackingShipmentCreator, where the operator pressed a "Vytvořit zásilku"
  // button after scanning. Since #1502 moved shipment orchestration to the backend, the
  // scan call itself creates or returns the shipment and that button no longer exists.
  //
  // Restoring equivalent coverage requires scanning a real staging order, which issues a
  // live Shoptet shipment-creation call (no sandbox — see CLAUDE.md), so it is deliberately
  // left to the component tests in src/components/baleni/__tests__/ instead.
});
