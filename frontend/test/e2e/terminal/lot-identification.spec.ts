import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';
import { materialContainerFixtures } from '../fixtures/test-data';

// Pad a suffix into a valid Mxxxxxxxx format (M + 8 digits)
const makeCode = (prefix: string, suffix: string): string => {
  const digits = (prefix.replace(/^M/, '') + suffix).slice(0, 8).padEnd(8, '0');
  return 'M' + digits;
};

test.describe('Terminal — Identifikace šarže', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    await page.goto('/terminal/lot-identification');
  });

  test('freeform receive: scan two containers for the same material+lot', async ({ page }) => {
    const ts = Date.now().toString().slice(-5);
    const codes = [
      makeCode(materialContainerFixtures.codePrefix, ts + '1'),
      makeCode(materialContainerFixtures.codePrefix, ts + '2'),
    ];
    const lot = `${materialContainerFixtures.lotCode}-${ts}`;

    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');

    await page.getByRole('textbox').fill(lot);
    await page.getByRole('textbox').press('Enter');

    for (const code of codes) {
      await page.getByRole('textbox').fill(code);
      await page.getByRole('textbox').press('Enter');
      await expect(page.getByText(/Uloženo/)).toBeVisible();
    }

    await expect(page.getByText(/Naskenováno:\s*2/)).toBeVisible();
  });

  test('duplicate code shows the already-assigned message', async ({ page }) => {
    const ts = Date.now().toString().slice(-5);
    const code = makeCode(materialContainerFixtures.codePrefix, ts + '5');
    const lot = `DUP-LOT-${ts}`;

    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill(lot);
    await page.getByRole('textbox').press('Enter');

    // First scan — should succeed
    await page.getByRole('textbox').fill(code);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByText(/Uloženo/)).toBeVisible();

    // Second scan of same code — should report conflict
    await page.getByRole('textbox').fill(code);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByRole('alert')).toContainText(/je již přiřazen/);
  });

  test('invalid code format is rejected client-side', async ({ page }) => {
    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill('SOMELOT');
    await page.getByRole('textbox').press('Enter');

    await page.getByRole('textbox').fill('BADCODE');
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByRole('alert')).toContainText(/Neplatný formát/);
  });

  test('last-used lot pre-fills on next visit for same material', async ({ page }) => {
    const ts = Date.now().toString().slice(-5);
    const lot = `LASTUSED-${ts}`;
    const code = makeCode('M998', ts + '1');

    // First visit — record a container against the lot
    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill(lot);
    await page.getByRole('textbox').press('Enter');
    await page.getByRole('textbox').fill(code);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByText(/Uloženo/)).toBeVisible();

    // Second visit — lot field should be pre-filled with the last-used lot
    await page.goto('/terminal/lot-identification');
    await page.getByTestId('lot-id-tile-freeform').click();
    await page.getByRole('textbox').fill(materialContainerFixtures.materialCode);
    await page.getByRole('textbox').press('Enter');
    await expect(page.getByRole('textbox')).toHaveValue(lot);
  });
});
