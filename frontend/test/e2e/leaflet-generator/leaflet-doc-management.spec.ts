import { test, expect } from '@playwright/test';
import * as path from 'path';
import { navigateToApp } from '../helpers/e2e-auth-helper';
import { waitForLoadingComplete } from '../helpers/wait-helpers';

const LEAFLET_GENERATOR_PATH = '/leaflet-generator';

/**
 * Leaflet document management E2E tests.
 *
 * These tests verify the tabbed UI for leaflet document management:
 * - Dokumenty tab shows the document table
 * - Nahrát soubor tab (marketing_reader only) allows file upload and subsequent
 *   document appearance and deletion in Dokumenty tab
 */
test.describe('Leaflet document management', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    const baseUrl =
      process.env.PLAYWRIGHT_FRONTEND_URL ||
      process.env.PLAYWRIGHT_BASE_URL ||
      'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}${LEAFLET_GENERATOR_PATH}`);
    await page.waitForLoadState('domcontentloaded');
    await waitForLoadingComplete(page);
  });

  test('page heading and tab navigation are visible', async ({ page }) => {
    await expect(
      page.getByRole('heading', { name: 'Generátor letáků' }),
    ).toBeVisible({ timeout: 10_000 });

    await expect(page.getByRole('button', { name: 'Generovat' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Dokumenty' })).toBeVisible();
  });

  test('Dokumenty tab shows document table', async ({ page }) => {
    await page.getByRole('button', { name: 'Dokumenty' }).click();
    await waitForLoadingComplete(page);

    // The filter bar should be visible (it's always rendered, even with 0 docs)
    const filterOrTable = page.locator('table, [placeholder="Název souboru..."]').first();
    await expect(filterOrTable).toBeVisible({ timeout: 15_000 });
  });

});

/**
 * Upload flow tests require a user with the `marketing_reader` role.
 * The E2E test user is a service principal that does not have this role,
 * so these tests cannot run against staging with the current credentials.
 * To enable: provision a test user account with `marketing_reader` role
 * and configure separate E2E credentials for it.
 */
test.describe.skip('Leaflet upload flow (requires marketing_reader role)', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    const baseUrl =
      process.env.PLAYWRIGHT_FRONTEND_URL ||
      process.env.PLAYWRIGHT_BASE_URL ||
      'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}${LEAFLET_GENERATOR_PATH}`);
    await page.waitForLoadState('domcontentloaded');
    await waitForLoadingComplete(page);
  });

  test('upload tab is visible for marketing_reader users', async ({ page }) => {
    await expect(page.getByRole('button', { name: 'Nahrát soubor' })).toBeVisible({ timeout: 10_000 });
  });

  test('upload flow: upload file and verify it appears in Dokumenty tab, then delete it', async ({ page }) => {
    const fixturePath = path.resolve(__dirname, '..', 'fixtures', 'sample.pdf');

    const uploadTabButton = page.getByRole('button', { name: 'Nahrát soubor' });
    await expect(uploadTabButton).toBeVisible({ timeout: 10_000 });
    await uploadTabButton.click();
    await waitForLoadingComplete(page);

    const dropZone = page.getByTestId('drop-zone');
    await expect(dropZone).toBeVisible({ timeout: 5_000 });

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(fixturePath);

    await expect(page.getByText('sample.pdf')).toBeVisible({ timeout: 5_000 });

    await page.getByRole('button', { name: /Nahrát vše/i }).click();

    await expect(page.getByText('sample.pdf')).not.toBeVisible({ timeout: 30_000 });

    await page.getByRole('button', { name: 'Dokumenty' }).click();
    await waitForLoadingComplete(page);

    await expect(page.getByText('sample.pdf')).toBeVisible({ timeout: 15_000 });

    const deleteButton = page
      .getByRole('row', { name: /sample\.pdf/i })
      .getByTitle('Smazat dokument');
    await deleteButton.click();

    await expect(page.getByText('Smazat dokument?')).toBeVisible();
    await page.getByRole('button', { name: 'Smazat' }).click();

    await expect(page.getByText('sample.pdf')).not.toBeVisible({ timeout: 15_000 });
  });
});
