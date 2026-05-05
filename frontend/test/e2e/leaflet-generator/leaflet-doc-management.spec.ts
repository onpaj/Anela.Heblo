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
 * - Nahrát soubor tab (leaflet_manager only) allows file upload and subsequent
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

  test('upload flow works for leaflet_manager users', async ({ page }) => {
    // Skip gracefully if the upload tab is not visible (non-manager user)
    const uploadTabButton = page.getByRole('button', { name: 'Nahrát soubor' });
    const isUploadTabVisible = await uploadTabButton.isVisible({ timeout: 3_000 }).catch(() => false);

    if (!isUploadTabVisible) {
      test.skip();
      return;
    }

    // Find a small fixture PDF to upload. Use path relative to the test directory.
    const fixturePath = path.resolve(__dirname, '..', 'fixtures', 'sample.pdf');

    // Verify fixture exists — throw (not skip) if missing per project rules
    const fs = await import('fs');
    if (!fs.existsSync(fixturePath)) {
      throw new Error(
        `Required E2E fixture missing: ${fixturePath}. ` +
        `Add a small sample.pdf to frontend/test/e2e/fixtures/ to enable upload tests.`,
      );
    }

    // Switch to upload tab
    await uploadTabButton.click();
    await waitForLoadingComplete(page);

    const dropZone = page.getByTestId('drop-zone');
    await expect(dropZone).toBeVisible({ timeout: 5_000 });

    // Upload the file via the hidden input
    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(fixturePath);

    // Verify file appears in queue
    await expect(page.getByText('sample.pdf')).toBeVisible({ timeout: 5_000 });

    // Click upload
    await page.getByRole('button', { name: /Nahrát vše/i }).click();

    // Wait for the file to be removed from the queue (upload succeeded)
    await expect(page.getByText('sample.pdf')).not.toBeVisible({ timeout: 30_000 });

    // Switch to Dokumenty tab and verify the uploaded file appears
    await page.getByRole('button', { name: 'Dokumenty' }).click();
    await waitForLoadingComplete(page);

    const filenameCell = page.getByText('sample.pdf');
    await expect(filenameCell).toBeVisible({ timeout: 15_000 });

    // Delete the uploaded document
    const deleteButton = page
      .getByRole('row', { name: /sample\.pdf/i })
      .getByTitle('Smazat dokument');
    await deleteButton.click();

    // Confirm deletion
    await expect(page.getByText('Smazat dokument?')).toBeVisible();
    await page.getByRole('button', { name: 'Smazat' }).click();

    // Row should disappear
    await expect(page.getByText('sample.pdf')).not.toBeVisible({ timeout: 15_000 });
  });
});
