import { test, expect } from '@playwright/test';

test.describe('Comprehensive Layout Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
  });

  test('topbar should be positioned correctly with user profile', async ({ page }) => {
    // Check topbar is fixed at top
    const topbar = page.locator('header');
    await expect(topbar).toBeVisible();
    await expect(topbar).toHaveCSS('position', 'fixed');
    await expect(topbar).toHaveCSS('top', '0px');
    await expect(topbar).toHaveCSS('z-index', '50');
    await expect(topbar).toHaveCSS('height', '65px');

    // Check app logo is present
    const appLogo = topbar.locator('.bg-primary-blue.rounded');
    await expect(appLogo).toBeVisible();
    await expect(appLogo).toContainText('AH');

    // Check user profile is in topbar (right side)
    const userProfile = topbar.locator('[class*="rounded-full"]').first();
    await expect(userProfile).toBeVisible();

    // Verify no search or settings buttons in topbar
    await expect(topbar.locator('input[type="text"]')).not.toBeVisible();
    await expect(topbar.locator('[class*="Settings"]')).not.toBeVisible();
  });

  test('sidebar should be positioned below topbar with toggle at bottom', async ({ page }) => {
    // Check sidebar positioning - find the fixed sidebar container
    const sidebar = page.locator('div.fixed.top-16.left-0.z-40.bottom-0');
    await expect(sidebar).toBeVisible();
    await expect(sidebar).toHaveCSS('position', 'fixed');
    await expect(sidebar).toHaveCSS('top', '64px'); // Below 64px topbar
    await expect(sidebar).toHaveCSS('bottom', '0px');
    await expect(sidebar).toHaveCSS('z-index', '40');

    // Check toggle button is at bottom of sidebar
    const toggleButton = sidebar.locator('button[title*="sidebar"]');
    await expect(toggleButton).toBeVisible();
    
    // Verify toggle button is in bottom section with border-top
    const toggleContainer = toggleButton.locator('..');
    await expect(toggleContainer).toHaveClass(/border-t/);
  });

  test('sidebar collapse/expand functionality', async ({ page }) => {
    const sidebar = page.locator('div.fixed.top-16.left-0.z-40.bottom-0');
    const toggleButton = sidebar.locator('button[title*="sidebar"]');
    const mainContent = page.locator('div.transition-all').filter({ hasText: 'Weather Forecast' });

    // Initially sidebar should be expanded (256px)
    await expect(sidebar).toHaveCSS('width', '256px');
    await expect(mainContent).toHaveClass(/md:pl-64/);

    // Click to collapse
    await toggleButton.click();
    await expect(sidebar).toHaveCSS('width', '64px');
    await expect(mainContent).toHaveClass(/md:pl-16/);

    // Click to expand
    await toggleButton.click();
    await expect(sidebar).toHaveCSS('width', '256px');
    await expect(mainContent).toHaveClass(/md:pl-64/);
  });

  test('sidebar auto-expand on collapsed item click', async ({ page }) => {
    const sidebar = page.locator('div.fixed.top-16.left-0.z-40.bottom-0');
    const toggleButton = sidebar.locator('button[title*="sidebar"]');

    // Collapse sidebar first
    await toggleButton.click();
    await expect(sidebar).toHaveCSS('width', '64px');

    // Click on a collapsed navigation item
    const collapsedNavItem = sidebar.locator('nav button').first();
    await collapsedNavItem.click();

    // Sidebar should auto-expand
    await expect(sidebar).toHaveCSS('width', '256px');
  });

  test('status bar should be positioned correctly', async ({ page }) => {
    const statusBar = page.locator('div.fixed.bottom-0').filter({ hasText: /v\d+\.\d+\.\d+/ });
    
    // Check positioning
    await expect(statusBar).toBeVisible();
    await expect(statusBar).toHaveCSS('position', 'fixed');
    await expect(statusBar).toHaveCSS('bottom', '0px');
    await expect(statusBar).toHaveCSS('height', '24px');
    await expect(statusBar).toHaveCSS('z-index', '10');

    // On desktop, status bar should be beside sidebar (not full width)
    const viewport = page.viewportSize();
    if (viewport && viewport.width >= 768) {
      await expect(statusBar).toHaveCSS('left', /^(64|256)px$/);
    }

    // Check content
    await expect(statusBar).toContainText(/v\d+\.\d+\.\d+/); // Version
    await expect(statusBar).toContainText(/Development|Test|Production|Automation/); // Environment
    await expect(statusBar).toContainText('API:'); // API endpoint
  });

  test('main content area should have correct spacing', async ({ page }) => {
    const mainContent = page.locator('main');
    const contentArea = page.locator('div.transition-all.pt-16.pb-6');

    // Check top offset (below 64px topbar)
    await expect(contentArea).toHaveCSS('padding-top', '64px');
    
    // Check bottom offset (above 24px status bar)
    await expect(contentArea).toHaveCSS('padding-bottom', '24px');

    // Check content padding - get the first p-6 which is the main content wrapper
    const contentWrapper = mainContent.locator('.p-6').first();
    await expect(contentWrapper).toHaveCSS('padding', '24px');

    // Check max width constraint
    const innerContent = contentWrapper.locator('.max-w-7xl');
    await expect(innerContent).toBeVisible();
  });

  test('mobile hamburger menu should work', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });

    // Check hamburger menu is visible
    const hamburger = page.locator('header button').first();
    await expect(hamburger).toBeVisible();

    // Sidebar should be hidden initially on mobile
    const sidebar = page.locator('div.fixed.top-16.left-0.z-40.bottom-0');
    await expect(sidebar).toHaveCSS('transform', 'matrix(1, 0, 0, 1, -256, 0)'); // translateX(-100%)

    // Click hamburger to open sidebar
    await hamburger.click();
    
    // Check overlay is visible
    const overlay = page.locator('.fixed.inset-0.bg-gray-600');
    await expect(overlay).toBeVisible();

    // Sidebar should slide in
    await expect(sidebar).toHaveCSS('transform', 'matrix(1, 0, 0, 1, 0, 0)'); // translateX(0)
  });

  test('user profile dropdown should work in topbar', async ({ page }) => {
    const topbar = page.locator('header');
    const userProfileButton = topbar.locator('button').filter({ has: page.locator('[class*="rounded-full"]') }).first();

    // Click user profile to open dropdown
    await userProfileButton.click();

    // Check dropdown menu is visible
    const dropdown = page.locator('[class*="absolute"][class*="bg-primary-white"]');
    await expect(dropdown).toBeVisible();

    // Check sign out button exists
    const signOutButton = dropdown.locator('button').filter({ hasText: 'Sign out' });
    await expect(signOutButton).toBeVisible();
  });
});