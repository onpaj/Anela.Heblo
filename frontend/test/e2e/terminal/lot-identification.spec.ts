import { test, expect, Page } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';
import { materialContainerFixtures } from '../fixtures/test-data';
import {
  seedUnassignedContainers,
  getContainerByCode,
  discardContainers,
} from '../helpers/material-container-helpers';

/**
 * Terminal — Identifikace šarže (receive materials with lots), driven at an iPhone "Max"
 * viewport (see the `terminal` project in playwright.config.ts).
 *
 * Two receive paths are covered end to end:
 *  - PO flow:      materials received against a predefined in-transit purchase order.
 *  - Freeform flow: materials received with no link to any purchase order.
 *
 * Container codes are provisioned per run via `print-labels` (physical print skipped on Staging
 * by the `is-label-printing-enabled` flag) and discarded afterwards, so runs are repeatable and
 * leave no residual Assigned data.
 */

const SCAN = 'test-results/terminal-ux';

// The receive screen shows the lot and container inputs at the same time, so target each by its
// accessible name (ScanInput sets aria-label from its label).
const LOT_LABEL = 'Šarže (z etikety dodavatele)';
const SCAN_LABEL = 'Kód kontejneru (Mxxxxxxxx)';

// Track container ids created during a test so afterEach can discard them.
let createdIds: number[] = [];

test.describe('Terminal — Identifikace šarže', () => {
  test.beforeEach(async ({ page }) => {
    createdIds = [];
    await navigateToApp(page);
    await page.goto('/terminal/lot-identification');
    await dismissChangelogToaster(page);
  });

  test.afterEach(async ({ page }) => {
    await discardContainers(page, createdIds);
  });

  test('PO flow: receive several materials from one order, then finish the order', async ({ page }, testInfo) => {
    const seeded = await seedUnassignedContainers(page, 2);
    createdIds.push(...seeded.map((c) => c.id));
    const suffix = uniqueSuffix();

    // Home → "Příjem podle objednávky"
    await page.getByTestId('lot-id-tile-po').click();

    // Pick the first in-transit purchase order (fail clearly if none exist in staging).
    const poLinks = page.locator('a[href^="/terminal/lot-identification/po/"]');
    await expect(
      poLinks.first(),
      'No in-transit purchase orders available in staging — cannot exercise the PO receive flow',
    ).toBeVisible();
    await shot(page, testInfo, `${SCAN}/po-01-pick-order.png`);
    await poLinks.first().click();

    // The order's material list (fail clearly if the order has no lines).
    const lineLinks = page.locator('a[href*="/line/"]');
    await expect(
      lineLinks.first(),
      'Selected purchase order has no lines — cannot pick a material to receive',
    ).toBeVisible();
    await shot(page, testInfo, `${SCAN}/po-02-material-list.png`);
    const lineCount = await lineLinks.count();

    // Receive each seeded container against a material line, returning to the order in between
    // to prove the operator can keep receiving more materials from the same order.
    const lots: string[] = [];
    for (let i = 0; i < seeded.length; i++) {
      const lot = `E2E-PO-${suffix}-${i}`;
      lots.push(lot);
      // Pick a line — a distinct one per iteration when the order has several.
      await lineLinks.nth(Math.min(i, lineCount - 1)).click();
      // The common receive screen opens with the material locked and the lot field active.
      await expect(page.getByTestId('receive-material-code')).toBeVisible();
      const lotBox = page.getByRole('textbox', { name: LOT_LABEL });
      await lotBox.fill(lot);
      await lotBox.press('Enter');
      await scanCodes(page, [seeded[i].code]);
      await shot(page, testInfo, `${SCAN}/po-03-scanned-${i}.png`);
      // "Hotovo" returns to the order's material list to continue with the next material.
      await page.getByRole('button', { name: /Hotovo/ }).click();
      await expect(lineLinks.first()).toBeVisible();
    }

    // Finish the whole order from the material list — leave it in transit (non-destructive).
    await page.getByRole('button', { name: /Dokončit příjem objednávky/ }).click();
    await expect(page.getByRole('heading', { name: /Označit objednávku jako přijatou/ })).toBeVisible();
    await shot(page, testInfo, `${SCAN}/po-04-finish.png`);
    await page.getByRole('button', { name: /Ponechat ve stavu/ }).click();
    await expect(page.getByTestId('lot-id-tile-po')).toBeVisible();

    // Data assertion: each container is Assigned, carries its lot, AND is linked to a PO line.
    for (let i = 0; i < seeded.length; i++) {
      const container = await getContainerByCode(page, seeded[i].code);
      expect(container, `container ${seeded[i].code} should exist`).toBeTruthy();
      expect(container.status).toBe('Assigned');
      expect(container.lotCode).toBe(lots[i]);
      expect(
        container.purchaseOrderLineId,
        `container ${seeded[i].code} should be linked to a PO line`,
      ).toBeTruthy();
    }
  });

  test('Freeform flow: receive a material not on any purchase order', async ({ page }, testInfo) => {
    const seeded = await seedUnassignedContainers(page, 2);
    createdIds.push(...seeded.map((c) => c.id));
    const lot = `E2E-FREE-${uniqueSuffix()}`;

    // Home → "Volný příjem"
    await page.getByTestId('lot-id-tile-freeform').click();

    // Pick the material from the autocomplete (materials have no barcode to scan), then enter
    // the lot. The chosen code is read back from the same screen and used in the assertions.
    const material = await selectFreeformMaterial(page, materialContainerFixtures.materialCode);
    const lotBox = page.getByRole('textbox', { name: LOT_LABEL });
    await lotBox.fill(lot);
    await lotBox.press('Enter');

    // Scan both seeded container codes.
    await scanCodes(page, seeded.map((c) => c.code));
    await expect(page.getByText(/Naskenováno:\s*2/)).toBeVisible();
    await shot(page, testInfo, `${SCAN}/freeform-01-scanned.png`);

    // Finish — freeform resets in place, so the material combobox is back for the next receive.
    await page.getByRole('button', { name: /Hotovo/ }).click();
    await expect(page.locator('input[id^="react-select"]').first()).toBeVisible();

    // Data assertion: containers Assigned, with NO purchase-order line.
    for (const { code } of seeded) {
      const container = await getContainerByCode(page, code);
      expect(container, `container ${code} should exist`).toBeTruthy();
      expect(container.status).toBe('Assigned');
      expect(container.materialCode).toBe(material);
      expect(container.lotCode).toBe(lot);
      expect(container.purchaseOrderLineId ?? null).toBeNull();
    }
  });

  test('duplicate code shows the already-assigned message', async ({ page }) => {
    const [seeded] = await seedUnassignedContainers(page, 1);
    createdIds.push(seeded.id);
    const lot = `E2E-DUP-${uniqueSuffix()}`;

    await page.getByTestId('lot-id-tile-freeform').click();
    await selectFreeformMaterial(page, materialContainerFixtures.materialCode);
    const lotBox = page.getByRole('textbox', { name: LOT_LABEL });
    await lotBox.fill(lot);
    await lotBox.press('Enter');

    const scan = page.getByRole('textbox', { name: SCAN_LABEL });
    // First scan succeeds.
    await scan.fill(seeded.code);
    await scan.press('Enter');
    await expect(page.getByText(/Uloženo/)).toBeVisible();

    // Second scan of the same code reports the conflict.
    await scan.fill(seeded.code);
    await scan.press('Enter');
    await expect(page.getByRole('alert')).toContainText(/je již přiřazen/);
  });

  test('invalid code format is rejected client-side', async ({ page }) => {
    await page.getByTestId('lot-id-tile-freeform').click();
    await selectFreeformMaterial(page, materialContainerFixtures.materialCode);
    const lotBox = page.getByRole('textbox', { name: LOT_LABEL });
    await lotBox.fill('SOMELOT');
    await lotBox.press('Enter');

    // A malformed code never reaches the backend — the loop rejects it locally.
    const scan = page.getByRole('textbox', { name: SCAN_LABEL });
    await scan.fill('BADCODE');
    await scan.press('Enter');
    await expect(page.getByRole('alert')).toContainText(/Neplatný formát/);
  });

  test('last-used lot pre-fills on next visit for same material', async ({ page }) => {
    const [seeded] = await seedUnassignedContainers(page, 1);
    createdIds.push(seeded.id);
    const lot = `E2E-LAST-${uniqueSuffix()}`;

    // First visit — record a container against the lot.
    await page.getByTestId('lot-id-tile-freeform').click();
    const material = await selectFreeformMaterial(page, materialContainerFixtures.materialCode);
    const lotBox = page.getByRole('textbox', { name: LOT_LABEL });
    await lotBox.fill(lot);
    await lotBox.press('Enter');
    const scan = page.getByRole('textbox', { name: SCAN_LABEL });
    await scan.fill(seeded.code);
    await scan.press('Enter');
    await expect(page.getByText(/Uloženo/)).toBeVisible();

    // Second visit — the lot field pre-fills with the last-used lot for that material.
    await page.goto('/terminal/lot-identification');
    await dismissChangelogToaster(page);
    await page.getByTestId('lot-id-tile-freeform').click();
    await selectFreeformMaterial(page, material);
    await expect(page.getByRole('textbox', { name: LOT_LABEL })).toHaveValue(lot);
  });
});

// --- helpers ---------------------------------------------------------------

function uniqueSuffix(): string {
  // Date.now is fine in the test process (not a workflow script).
  return `${Date.now()}`.slice(-8);
}

/**
 * The global "Co je nové" changelog toaster (fixed top-right) overlaps the home tiles on a
 * phone viewport and would intercept the first tap. Dismiss it if it appears so the flow is
 * reliable on mobile.
 */
async function dismissChangelogToaster(page: Page): Promise<void> {
  const close = page.getByRole('button', { name: 'Zavřít' });
  if (await close.isVisible().catch(() => false)) {
    await close.click();
    await expect(close).toBeHidden();
  }
}

/**
 * Picks a material in the freeform flow via the catalog autocomplete (react-select): type the
 * query, choose the first match, and return the material code now shown (locked) on the receive
 * screen. Material selection and lot entry live on the same screen.
 */
async function selectFreeformMaterial(page: Page, query: string): Promise<string> {
  const combo = page.locator('input[id^="react-select"]').first();
  await combo.click();
  await combo.pressSequentially(query, { delay: 40 });
  const firstOption = page.getByRole('option').first();
  await expect(
    firstOption,
    `no material matched "${query}" in the catalog autocomplete`,
  ).toBeVisible();
  await firstOption.click();
  const codeEl = page.getByTestId('receive-material-code');
  await expect(codeEl).toBeVisible();
  return (await codeEl.innerText()).trim();
}

async function scanCodes(page: Page, codes: string[]): Promise<void> {
  const scan = page.getByRole('textbox', { name: SCAN_LABEL });
  for (let i = 0; i < codes.length; i++) {
    await scan.fill(codes[i]);
    await scan.press('Enter');
    // The running counter reflects successfully-saved boxes.
    await expect(page.getByText(new RegExp(`Naskenováno:\\s*${i + 1}`))).toBeVisible();
  }
}

async function shot(page: Page, testInfo: import('@playwright/test').TestInfo, path: string): Promise<void> {
  const buffer = await page.screenshot({ path });
  await testInfo.attach(path.split('/').pop() ?? 'screenshot', { body: buffer, contentType: 'image/png' });
}
