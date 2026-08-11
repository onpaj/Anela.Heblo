import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

// Runs against deployed staging (MindMaps:UseStubUpdater=true there), so the
// generated node is deterministic: "Porada: <subject>".
test.describe('Mind maps', () => {
  // mind-elixir renders each topic as a <me-tpc> element carrying data-nodeid.
  const nodes = (page: import('@playwright/test').Page) => page.locator('me-tpc');

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
      await expect(nodes(page)).toHaveCount(1);

      // Attach the first available meeting — fixtures policy: throw, never skip.
      // The side panel opens on the "Uzel" tab by default; the attach button
      // lives on the "Porady" tab. The tabs are plain <button> elements (no
      // role="tab"/"tablist" is set on them), so they carry the implicit
      // "button" role rather than "tab".
      await page.getByRole('button', { name: 'Porady' }).click();
      await page.getByTestId('mindmap-attach-button').click();
      const options = page.getByTestId('mindmap-attach-option');
      // Locator.count() is a one-shot snapshot — it does not auto-wait. Right after
      // the click, the dialog is still in its "Načítání..." loading state (the
      // useMeetingTasksList() fetch is in flight) and no mindmap-attach-option
      // elements exist yet, so counting immediately would read 0 even when meetings
      // exist. Wait for the fetch to settle into one of its three terminal render
      // states first — the first option attaching, the empty-state message, or the
      // error message — then count. Whichever state is real resolves quickly; the
      // other two simply time out, which Promise.race tolerates via the catch below.
      await Promise.race([
        options.first().waitFor({ state: 'attached', timeout: 15000 }),
        page.getByText('Žádné další porady k připojení').waitFor({ state: 'visible', timeout: 15000 }),
        page.getByText('Nepodařilo se načíst porady').waitFor({ state: 'visible', timeout: 15000 }),
      ]).catch(() => {
        // None of the terminal states appeared within budget — fall through to the
        // count-based throw below, which reports the (still) empty state.
      });
      if ((await options.count()) === 0) {
        throw new Error(
          'No meeting transcripts available on staging — seed at least one meeting (docs/testing/test-data-fixtures.md)',
        );
      }
      await options.first().click();

      // Stub updater runs in background: attach → cache invalidation → refetch →
      // Hangfire picks up the job → stub runs → save → the frontend's 3s status
      // poll → refetch. The node count is the assertion that actually observes the
      // pipeline having run, so it gets the generous budget. A freshly created map
      // is already Idle/"Aktuální" *before* any meeting is attached, so asserting
      // the status text first would resolve immediately without ever waiting for
      // the update — leaving the real bottleneck (the node count) to fall back to
      // Playwright's default 5000ms `expect` timeout (there is no `expect.timeout`
      // override in playwright.config.ts), which staging round-trip latency would
      // blow through. Deliberately not asserting the transient "Aktualizuje se…"
      // state on the way through: the poll is 3s and the stub is fast, so that
      // state can legitimately be skipped entirely, and asserting it would just
      // trade this flake for a different one.
      await expect(nodes(page)).toHaveCount(2, { timeout: 60000 });
      // Secondary check, now that the pipeline is known to have completed — also
      // generous in case the badge's own re-render trails the node list by a beat.
      await expect(page.getByTestId('mindmap-status-badge')).toHaveText('Aktuální', {
        timeout: 60000,
      });

      // Rename the generated node → auto-lock on save
      const generatedNode = nodes(page).filter({ hasText: 'Porada:' });
      await generatedNode.click();
      const titleInput = page.getByTestId('mindmap-panel-title-input');
      await titleInput.fill('Ručně upravený uzel');
      // The field only reports its value on blur — clicking straight to Save would
      // blur it too, but doing it explicitly keeps the failure mode obvious.
      await titleInput.blur();
      await page.getByTestId('mindmap-save-button').click();
      await expect(nodes(page).filter({ hasText: 'Ručně upravený uzel' })).toContainText('🔒', {
        timeout: 15000,
      });
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
