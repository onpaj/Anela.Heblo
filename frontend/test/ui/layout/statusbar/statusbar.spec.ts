import { test, expect } from '@playwright/test';

test.describe('StatusBar Component', () => {
  test('should display correct version and environment information', async ({ page }) => {
    // Navigate to the app (uses baseURL from config)
    await page.goto('/');
    
    // Wait for the app to load completely
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
    
    // Wait for StatusBar to be visible
    await page.waitForSelector('.bg-gray-100', { timeout: 5000 });
    
    // Check that StatusBar is visible
    const statusBar = page.locator('.bg-gray-100');
    await expect(statusBar).toBeVisible();
    
    // Check version display (should show version from backend API or fallback)
    await expect(statusBar).toContainText(/v\d+\.\d+/);
    
    // Check environment info - should show "Automation" in test environment and "Mock Auth"
    await expect(statusBar).toContainText('Automation');
    await expect(statusBar).toContainText('Mock Auth');
    
    // Check authentication type badge - should show "Mock Auth" with yellow background
    const authBadge = statusBar.locator('.bg-yellow-500');
    await expect(authBadge).toBeVisible();
    await expect(authBadge).toContainText('Mock Auth');
    await expect(authBadge).toHaveClass(/text-black/);
    
    // Check API host information - should show localhost:5001 (from automation runtime config)
    await expect(statusBar).toContainText('API: localhost:5001');
    
    // Take screenshot for visual verification
    await page.screenshot({ 
      path: 'test-results/statusbar-test.png',
      fullPage: true 
    });
  });

  test('should have correct styling according to layout definition', async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('.bg-gray-100', { timeout: 5000 });
    
    const statusBar = page.locator('.bg-gray-100');
    
    // Check status bar has light background as per layout_definition.md
    await expect(statusBar).toHaveClass(/bg-gray-100/);
    await expect(statusBar).toHaveClass(/border-t/);
    await expect(statusBar).toHaveClass(/border-gray-200/);
    
    // Check text styling - small, subdued gray text
    await expect(statusBar).toHaveClass(/text-xs/);
    await expect(statusBar).toHaveClass(/text-gray-600/);
    
    // Check height is 24px (h-6)
    await expect(statusBar).toHaveClass(/h-6/);
    
    // Check environment badge has correct styling for development (red background)
    const environmentBadge = statusBar.locator('.bg-red-600');
    await expect(environmentBadge).toBeVisible();
    await expect(environmentBadge).toHaveClass(/text-black/);
    
    // Check auth badge has correct styling for mock auth (yellow background)
    const authBadge = statusBar.locator('.bg-yellow-500');
    await expect(authBadge).toBeVisible();
    await expect(authBadge).toHaveClass(/text-black/);
  });

  test('should be positioned beside sidebar at bottom', async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('.bg-gray-100', { timeout: 5000 });
    
    const statusBar = page.locator('.bg-gray-100');
    
    // Check fixed positioning
    await expect(statusBar).toHaveClass(/fixed/);
    await expect(statusBar).toHaveClass(/bottom-0/);
    await expect(statusBar).toHaveClass(/right-0/);
    
    // Check left positioning beside expanded sidebar (256px)
    await expect(statusBar).toHaveClass(/left-64/);
    
    // Get the position of the status bar
    const statusBarBox = await statusBar.boundingBox();
    const viewportSize = page.viewportSize();
    
    // Status bar should be at the very bottom of viewport
    expect(statusBarBox!.y + statusBarBox!.height).toBeCloseTo(viewportSize!.height, 5);
  });
});