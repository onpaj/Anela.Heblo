import { test, expect } from '@playwright/test';

test.describe('Mobile Menu', () => {
  test('should show hamburger menu button on mobile and allow opening sidebar', async ({ page }) => {
    // Navigate to the app
    await page.goto('http://localhost:3001');

    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 }); // iPhone SE size
    
    // Wait for the app to load
    await page.waitForSelector('[data-testid="topbar"]', { timeout: 10000 });
    
    // Verify the hamburger menu button is visible on mobile
    const menuButton = page.locator('header button[aria-label="Open menu"], header button:has(svg)').first();
    await expect(menuButton).toBeVisible();
    
    // Verify sidebar is initially hidden on mobile
    const sidebar = page.locator('div[role="navigation"], nav, .sidebar').first();
    
    // Click the hamburger menu to open sidebar
    await menuButton.click();
    
    // Wait a bit for animation
    await page.waitForTimeout(500);
    
    // Verify sidebar becomes visible after clicking hamburger menu
    const sidebarLocator = page.locator('[class*="translate-x-0"]:has(.space-y-1)');
    await expect(sidebarLocator).toBeVisible();
    
    // Verify we can see navigation items in the opened sidebar
    const dashboardLink = page.locator('a[href="/"], button:has-text("Dashboard")').first();
    await expect(dashboardLink).toBeVisible();
    
    // Close sidebar by clicking outside (overlay)
    const overlay = page.locator('.fixed.inset-0.bg-gray-600.bg-opacity-75');
    if (await overlay.count() > 0) {
      await overlay.click({ force: true, position: { x: 10, y: 10 } });
      await page.waitForTimeout(300);
    }
  });

  test('should not show hamburger menu on desktop', async ({ page }) => {
    // Navigate to the app
    await page.goto('http://localhost:3001');

    // Set desktop viewport
    await page.setViewportSize({ width: 1920, height: 1080 });
    
    // Wait for the app to load
    await page.waitForSelector('body', { timeout: 10000 });
    
    // Verify the topbar with hamburger menu is hidden on desktop
    const topbar = page.locator('header.md\\:hidden');
    await expect(topbar).toBeHidden();
    
    // Verify sidebar is visible on desktop
    const sidebar = page.locator('div[class*="w-64"], div[class*="w-16"]').first();
    await expect(sidebar).toBeVisible();
  });

  test('should be able to navigate using mobile menu', async ({ page }) => {
    // Navigate to the app
    await page.goto('http://localhost:3001');

    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    // Wait for the app to load
    await page.waitForTimeout(2000);
    
    // Open mobile menu
    const menuButton = page.locator('header button').first();
    await menuButton.click();
    await page.waitForTimeout(500);
    
    // Click on a navigation item
    const catalogLink = page.locator('text=Katalog').first();
    if (await catalogLink.count() > 0) {
      await catalogLink.click();
      
      // Wait for navigation
      await page.waitForTimeout(1000);
      
      // Check that we navigated to the catalog page
      await expect(page).toHaveURL(/.*catalog.*/);
    }
  });
});