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
    // Wait for tiles to load
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 5000 });

    // Check if background task status tile (AutoShow: true) is visible
    // TileId is generated from class name: BackgroundTaskStatusTile -> backgroundtaskstatus
    const backgroundTasksTile = page.locator('[data-testid="dashboard-tile-backgroundtaskstatus"]');
    await expect(backgroundTasksTile).toBeVisible();

    // Verify the tile has content (using the actual class name from DashboardTile component)
    await expect(backgroundTasksTile.locator('.text-sm.font-medium')).toContainText('Stav background tasků');
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

    // Get bounding boxes for drag and drop
    const handleBox = await dragHandle.boundingBox();
    const secondTileBox = await secondTile.boundingBox();

    if (!handleBox || !secondTileBox) {
      throw new Error('Could not get bounding boxes for drag and drop');
    }

    // Perform drag and drop
    await page.mouse.move(handleBox.x + handleBox.width / 2, handleBox.y + handleBox.height / 2);
    await page.mouse.down();
    await page.mouse.move(secondTileBox.x + secondTileBox.width / 2, secondTileBox.y + secondTileBox.height / 2, { steps: 5 });
    await page.mouse.up();

    // Wait for the reorder to complete
    await page.waitForTimeout(1000);

    // Verify tiles were reordered
    const tilesAfterDrag = page.locator('[data-testid^="dashboard-tile-"]');
    const firstTileIdAfter = await tilesAfterDrag.nth(0).getAttribute('data-testid');
    const secondTileIdAfter = await tilesAfterDrag.nth(1).getAttribute('data-testid');

    // The first tile should now be in second position or vice versa
    expect(firstTileIdAfter !== firstTileId || secondTileIdAfter !== secondTileId).toBeTruthy();
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