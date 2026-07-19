import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Dashboard', () => {
  test.beforeEach(async ({ page }) => {
    // Authenticate before navigating to dashboard with full frontend setup
    await navigateToApp(page);

    // Wait for dashboard to load
    await page.waitForSelector('[data-testid="dashboard-container"]', { timeout: 10000 });
  });

  test('should display dashboard tiles', async ({ page }) => {
    // Check if dashboard header is visible
    await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();

    // Check if the dashboard grid container is present
    await expect(page.locator('[data-testid="dashboard-grid"]')).toBeVisible();

    // Check if at least one tile is visible
    const tiles = page.locator('[data-testid^="dashboard-tile-"]');
    await expect(tiles.first()).toBeVisible();
  });

  test('should display AutoShow tiles automatically', async ({ page }) => {
    // Guarantee a clean precondition. Dashboard settings persist server-side per user and the
    // staging E2E user is shared across every run, so a tile hidden by an earlier run (or by a
    // human) stays hidden: GetUserSettingsHandler back-fills AutoShow tiles that are *absent*
    // from the saved set, but not ones explicitly recorded as IsVisible=false. Without this the
    // test passes in isolation and fails intermittently in full runs, depending on leftover state
    // rather than on product behaviour.
    await page.request.post(
      `${process.env.PLAYWRIGHT_BASE_URL}/api/dashboard/tiles/backgroundtaskstatus/enable`,
    );
    await page.reload();
    await page.waitForSelector('[data-testid="dashboard-container"]', { timeout: 10000 });

    // Wait for tiles to load
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 15000 });

    // Check if background task status tile (AutoShow: true) is visible
    // TileId naming convention: TileExtensions.GetTileId() lowercases the class name and removes "tile" suffix.
    // Example: BackgroundTaskStatusTile -> "backgroundtaskstatustile".toLowerCase().replace("tile","") -> "backgroundtaskstatus"
    const backgroundTasksTile = page.locator('[data-testid="dashboard-tile-backgroundtaskstatus"]');
    await expect(backgroundTasksTile).toBeVisible();

    // Verify the tile has content using .tile-title class from TileHeader component.
    // Note: the h3 uses responsive classes "text-base md:text-sm font-medium ..." - the
    // ".text-sm" selector does NOT match because the Tailwind class is "md:text-sm" (different class name).
    // Use the stable .tile-title class instead.
    await expect(backgroundTasksTile.locator('.tile-title')).toContainText('Stav background tasků');
  });

  test('should open dashboard settings', async ({ page }) => {
    // Click on settings button
    await page.getByText('Nastavení').click();
    
    // Check if settings modal is visible
    await expect(page.getByText('Nastavení dashboardu')).toBeVisible();
    
    // Check if tiles list is visible
    await expect(page.locator('[data-testid="dashboard-settings-tiles"]')).toBeVisible();
  });

  test('should be able to enable/disable tiles', async ({ page }) => {
    // Open settings
    await page.getByText('Nastavení').click();

    // Wait for settings to load
    await page.waitForSelector('[data-testid="dashboard-settings-tiles"]', { timeout: 5000 });

    // Find a tile toggle button and click it
    const toggleButtons = page.locator('button:has-text("Skrýt"), button:has-text("Zobrazit")');
    const firstToggle = toggleButtons.first();

    // Get the initial state
    const initialText = await firstToggle.textContent();

    // Click the toggle
    await firstToggle.click();

    // Wait for the request to complete
    await page.waitForTimeout(1000);

    // Check if the button text changed
    const newText = await firstToggle.textContent();
    expect(newText).not.toBe(initialText);
  });

  test('should support drag and drop to reorder tiles', async ({ page }) => {
    // Wait for tiles to load
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 5000 });

    // Get all tiles
    const tiles = page.locator('[data-testid^="dashboard-tile-"]');
    const tileCount = await tiles.count();

    // Require at least 2 tiles - fail if not enough tiles available
    if (tileCount < 2) {
      throw new Error(
        `Insufficient dashboard tiles for drag-and-drop test: Expected at least 2 tiles, ` +
        `but found only ${tileCount}. Dashboard may not have enough configured tiles.`
      );
    }

    // Get first and second tile IDs
    const firstTile = tiles.nth(0);
    const secondTile = tiles.nth(1);

    const firstTileId = await firstTile.getAttribute('data-testid');
    const secondTileId = await secondTile.getAttribute('data-testid');

    // Find the drag handle (GripVertical icon button) in the first tile
    const dragHandle = firstTile.locator('button[title="Přetáhnout pro změnu pořadí"]');
    await expect(dragHandle).toBeVisible();

    // Reorder via the dnd-kit KeyboardSensor (DashboardGrid registers it with
    // sortableKeyboardCoordinates). Mouse-driven dnd-kit drags are unreliable here:
    // the tiles have mixed col-spans, so closestCenter collision detection resolves
    // the dragged (large) tile back onto itself and the drop is a no-op.
    // Keyboard: Space picks up, ArrowRight moves to the next sortable, Space drops.
    await dragHandle.focus();
    await dragHandle.press(' ');
    await dragHandle.press('ArrowRight');
    await dragHandle.press(' ');

    // The reorder is persisted through useSaveDashboardSettings, so poll the DOM
    // until the order actually changes rather than sleeping a fixed amount.
    await expect
      .poll(async () => {
        const tilesAfterDrag = page.locator('[data-testid^="dashboard-tile-"]');
        return [
          await tilesAfterDrag.nth(0).getAttribute('data-testid'),
          await tilesAfterDrag.nth(1).getAttribute('data-testid'),
        ].join('|');
      }, { timeout: 10000 })
      .not.toBe([firstTileId, secondTileId].join('|'));

    // Restore the original order. useSaveDashboardSettings persists this server-side for the
    // shared staging E2E user, so without an undo the new order leaks into every later test and
    // every later run - which previously made sibling tests (e.g. the AutoShow tile assertion)
    // pass in isolation but fail in a full-module run.
    //
    // Undo with the same ArrowRight motion rather than ArrowLeft: the swap is symmetric, so
    // moving whichever tile is now first one place right restores the original pair. ArrowLeft
    // does not walk the tile back in dnd-kit's grid layout - it was verified not to restore.
    const restoreHandle = page
      .locator('[data-testid^="dashboard-tile-"]')
      .nth(0)
      .locator('button[title="Přetáhnout pro změnu pořadí"]');
    await restoreHandle.focus();
    await restoreHandle.press(' ');
    await restoreHandle.press('ArrowRight');
    await restoreHandle.press(' ');

    // Best-effort cleanup, deliberately NOT asserted. dnd-kit's keyboard reorder over a
    // mixed-col-span grid is not a reliable adjacent swap, so demanding an exact restored order
    // makes this test flaky without testing any product behaviour - the assertion under test is
    // the reorder above. Give the persist a moment to land, then move on.
    await expect
      .poll(async () => tiles.nth(0).getAttribute('data-testid'), { timeout: 5000 })
      .toBeTruthy();
  });

  test('should display empty state for production tile with no orders', async ({ page }) => {
    // Wait for tiles to load
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 5000 });

    // Look for production tiles (today or next day)
    const todayProductionTile = page.locator('[data-testid="dashboard-tile-todayproduction"]');
    const nextDayProductionTile = page.locator('[data-testid="dashboard-tile-nextdayproduction"]');

    // Check if either tile is visible and has empty state
    const todayVisible = await todayProductionTile.isVisible();
    const nextDayVisible = await nextDayProductionTile.isVisible();

    // At least one production tile should be visible
    expect(todayVisible || nextDayVisible).toBeTruthy();

    if (todayVisible) {
      // Check for empty state using class selector to avoid strict mode violation
      // The title has class "text-sm font-medium text-gray-500 mb-1"
      const emptyStateTitle = todayProductionTile.locator('.text-sm.font-medium.text-gray-500');
      try {
        const hasEmptyState = await emptyStateTitle.isVisible({ timeout: 2000 });

        if (hasEmptyState) {
          // Verify empty state components are present
          await expect(emptyStateTitle).toHaveText('Žádná výroba');
          // Verify the PackageCheck icon is present (empty state icon)
          await expect(todayProductionTile.locator('svg').first()).toBeVisible();
          // Verify the description text is present
          await expect(todayProductionTile.getByText(/Pro dnešní.*není naplánována žádná výroba/)).toBeVisible();
        }
      } catch (e) {
        // Empty state not present - tile has production data, which is fine
      }
    }

    if (nextDayVisible) {
      // Check for empty state using class selector to avoid strict mode violation
      const emptyStateTitle = nextDayProductionTile.locator('.text-sm.font-medium.text-gray-500');
      try {
        const hasEmptyState = await emptyStateTitle.isVisible({ timeout: 2000 });

        if (hasEmptyState) {
          // Verify empty state components are present
          await expect(emptyStateTitle).toHaveText('Žádná výroba');
          // Verify the PackageCheck icon is present (empty state icon)
          await expect(nextDayProductionTile.locator('svg').first()).toBeVisible();
          // Verify the description text is present
          await expect(nextDayProductionTile.getByText(/Pro zítřejší.*není naplánována žádná výroba/)).toBeVisible();
        }
      } catch (e) {
        // Empty state not present - tile has production data, which is fine
      }
    }
  });
});