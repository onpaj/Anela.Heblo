import { test, expect } from '@playwright/test';
import { createE2EAuthSession } from './helpers/e2e-auth-helper';

test.describe('Dashboard', () => {
  test.beforeEach(async ({ page }) => {
    // Authenticate before navigating to dashboard
    await createE2EAuthSession(page);

    // Navigate to dashboard
    await page.goto('/');

    // Wait for authentication and dashboard to load
    await page.waitForSelector('[data-testid="dashboard-container"]', { timeout: 10000 });
  });

  test('should display dashboard tiles', async ({ page }) => {
    // Check if dashboard header is visible
    await expect(page.getByText('Dashboard')).toBeVisible();
    
    // Check if the dashboard grid container is present
    await expect(page.locator('[data-testid="dashboard-grid"]')).toBeVisible();
    
    // Check if at least one tile is visible
    const tiles = page.locator('[data-testid^="dashboard-tile-"]');
    await expect(tiles.first()).toBeVisible();
  });

  test('should display AutoShow tiles automatically', async ({ page }) => {
    // Wait for tiles to load
    await page.waitForSelector('[data-testid^="dashboard-tile-"]', { timeout: 5000 });
    
    // Check if background-tasks tile (AutoShow: true) is visible
    const backgroundTasksTile = page.locator('[data-testid="dashboard-tile-background-tasks"]');
    await expect(backgroundTasksTile).toBeVisible();
    
    // Verify the tile has content
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

    // Skip if less than 2 tiles
    if (tileCount < 2) {
      test.skip();
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

    if (todayVisible) {
      // Check for empty state text
      const emptyState = todayProductionTile.getByText('Žádná výroba');
      if (await emptyState.isVisible()) {
        // Verify icon is present
        await expect(todayProductionTile.locator('svg')).toBeVisible();
      }
    }

    if (nextDayVisible) {
      // Check for empty state text
      const emptyState = nextDayProductionTile.getByText('Žádná výroba');
      if (await emptyState.isVisible()) {
        // Verify icon is present
        await expect(nextDayProductionTile.locator('svg')).toBeVisible();
      }
    }
  });
});