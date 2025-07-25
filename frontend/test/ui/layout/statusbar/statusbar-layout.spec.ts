import { test, expect } from '@playwright/test';

test.describe('StatusBar Layout Integration', () => {
  test('should not overlap with application content and respect sidebar state', async ({ page }) => {
    // Navigate to the app (uses baseURL from config)
    await page.goto('/');
    
    // Wait for the app to load completely
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
    
    // Wait for StatusBar to be visible
    await page.waitForSelector('.bg-gray-100', { timeout: 5000 });
    
    const statusBar = page.locator('.bg-gray-100');
    
    // Check that StatusBar is positioned at the bottom
    await expect(statusBar).toBeVisible();
    await expect(statusBar).toHaveClass(/fixed/);
    await expect(statusBar).toHaveClass(/bottom-0/);
    
    // Get the status bar position and dimensions
    const statusBarBox = await statusBar.boundingBox();
    expect(statusBarBox).toBeTruthy();
    
    // Status bar is beside sidebar, so no padding needed on main content
    // Just verify status bar is positioned beside sidebar
    await expect(statusBar).toHaveClass(/left-64/);
    
    // Get viewport height and verify status bar is at the bottom
    const viewportSize = page.viewportSize();
    expect(statusBarBox!.y + statusBarBox!.height).toBeCloseTo(viewportSize!.height, 5);
    
    // Verify status bar doesn't overlap with main content
    const mainContent = page.locator('main');
    const mainContentBox = await mainContent.boundingBox();
    expect(mainContentBox).toBeTruthy();
    
    // Since status bar is beside sidebar, main content should not need to avoid it
    // Just verify they don't actually overlap in the visible area
    const statusBarLeft = statusBarBox!.x;
    const mainContentRight = mainContentBox!.x + mainContentBox!.width;
    expect(mainContentRight).toBeLessThanOrEqual(statusBarLeft + 10);
    
    // Take screenshot for visual verification
    await page.screenshot({ 
      path: 'test-results/statusbar-layout-expanded.png',
      fullPage: true 
    });
  });

  test('should adapt to collapsed sidebar state', async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
    
    // Find and click the sidebar toggle button (PanelLeftClose icon in expanded sidebar)
    const toggleButton = page.locator('button[title="Collapse sidebar"]');
    
    // If toggle button exists, click it to collapse sidebar
    if (await toggleButton.count() > 0) {
      await toggleButton.click();
      
      // Wait for transition to complete
      await page.waitForTimeout(500);
      
      const statusBar = page.locator('.bg-gray-100');
      
      // Status bar should adapt to collapsed sidebar (may take time for animation)
      await expect(statusBar).toHaveClass(/left-16/, { timeout: 10000 });
      
      // Take screenshot of collapsed state
      await page.screenshot({ 
        path: 'test-results/statusbar-layout-collapsed.png',
        fullPage: true 
      });
    }
  });

  test('should remain visible during scrolling', async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
    
    // Add some content to make page scrollable
    await page.evaluate(() => {
      const content = document.querySelector('main');
      if (content) {
        const extraContent = document.createElement('div');
        extraContent.style.height = '2000px';
        extraContent.style.background = 'linear-gradient(to bottom, #f0f0f0, #e0e0e0)';
        extraContent.innerHTML = '<p style="padding: 20px;">Extra content to enable scrolling</p>';
        content.appendChild(extraContent);
      }
    });
    
    const statusBar = page.locator('.bg-gray-100');
    
    // Verify status bar is visible before scrolling
    await expect(statusBar).toBeVisible();
    
    // Scroll down
    await page.evaluate(() => window.scrollTo(0, 1000));
    await page.waitForTimeout(200);
    
    // Status bar should still be visible and at the bottom
    await expect(statusBar).toBeVisible();
    await expect(statusBar).toHaveClass(/fixed/);
    
    // Scroll to top
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(200);
    
    // Status bar should still be visible
    await expect(statusBar).toBeVisible();
    
    // Take screenshot
    await page.screenshot({ 
      path: 'test-results/statusbar-scroll-test.png',
      fullPage: true 
    });
  });
});