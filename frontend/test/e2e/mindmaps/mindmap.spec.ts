import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

// Runs against deployed staging (MindMaps:UseStubUpdater=true there), so the
// generated node is deterministic: "Porada: <subject>".
test.describe('Mind maps', () => {
  test('create map, attach meeting, stub generates node, rename locks it', async ({ page }) => {
    await navigateToApp(page);
    const mapName = `E2E mapa ${Date.now()}`;

    // Create
    await page.goto('/automation/mind-maps');
    await page.getByTestId('mindmap-create-button').click();
    await page.getByTestId('mindmap-name-input').fill(mapName);
    await page.getByTestId('mindmap-create-submit').click();

    // Everything from here on runs against a map that now exists on staging, so
    // any failure below must still fall through to cleanup — otherwise a failed
    // run leaves an orphaned map behind and repeated nightly runs accumulate data.
    try {
      // Lands on detail with just the root node
      await expect(page.getByTestId('mindmap-canvas')).toBeVisible({ timeout: 15000 });
      await expect(page.getByTestId('mindmap-node')).toHaveCount(1);

      // Attach the first available meeting — fixtures policy: throw, never skip.
      // The side panel opens on the "Uzel" tab by default; the attach button
      // lives on the "Porady" tab. The tabs are plain <button> elements (no
      // role="tab"/"tablist" is set on them), so they carry the implicit
      // "button" role rather than "tab".
      await page.getByRole('button', { name: 'Porady' }).click();
      await page.getByTestId('mindmap-attach-button').click();
      const options = page.getByTestId('mindmap-attach-option');
      if ((await options.count()) === 0) {
        throw new Error(
          'No meeting transcripts available on staging — seed at least one meeting (docs/testing/test-data-fixtures.md)',
        );
      }
      await options.first().click();

      // Stub updater runs in background; poll until status returns to Idle
      await expect(page.getByTestId('mindmap-status-badge')).toHaveText('Aktuální', {
        timeout: 60000,
      });
      await expect(page.getByTestId('mindmap-node')).toHaveCount(2);

      // Rename the generated node → auto-lock on save
      const generatedNode = page.getByTestId('mindmap-node').filter({ hasText: 'Porada:' });
      await generatedNode.dblclick();
      await page.getByTestId('mindmap-panel-title-input').fill('Ručně upravený uzel');
      await page.getByTestId('mindmap-save-button').click();
      await expect(
        page.getByTestId('mindmap-node').filter({ hasText: 'Ručně upravený uzel' })
          .getByTestId('mindmap-node-lock'),
      ).toBeVisible({ timeout: 15000 });
    } finally {
      // Cleanup. Guarded on the row actually existing so that a failure before the
      // map was even created (e.g. the create step above) doesn't turn into a second,
      // misleading failure here that masks the original one.
      await page.goto('/automation/mind-maps');
      const row = page.getByTestId('mindmap-row').filter({ hasText: mapName });
      if ((await row.count()) > 0) {
        page.once('dialog', (dialog) => dialog.accept());
        await row.getByRole('button').last().click();
        await expect(row).toHaveCount(0);
      }
    }
  });
});
