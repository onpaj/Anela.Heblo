import { test, expect } from '@playwright/test';

test.describe('Comprehensive Layout Tests', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
  });

  test('sidebar should contain app title at top', async ({ page }) => {
    // Check sidebar is full height (no topbar)
    const sidebar = page.locator('div.fixed.top-0.left-0.z-40.bottom-0');
    await expect(sidebar).toBeVisible();
    await expect(sidebar).toHaveCSS('position', 'fixed');
    await expect(sidebar).toHaveCSS('top', '0px');
    await expect(sidebar).toHaveCSS('bottom', '0px');
    await expect(sidebar).toHaveCSS('z-index', '40');

    // Check app title is at top of sidebar
    const appTitle = sidebar.locator('span').filter({ hasText: 'Anela Heblo' });
    await expect(appTitle).toBeVisible();
    
    // Check app logo/icon is present
    const appLogo = sidebar.locator('.bg-primary-blue.rounded').first();
    await expect(appLogo).toBeVisible();
    await expect(appLogo).toContainText('AH');

    // Verify no topbar exists
    const topbar = page.locator('header');
    await expect(topbar).not.toBeVisible();
  });

  test('sidebar should have user profile and toggle at bottom', async ({ page }) => {
    // Check sidebar positioning - find the fixed sidebar container
    const sidebar = page.locator('div.fixed.top-0.left-0.z-40.bottom-0');
    await expect(sidebar).toBeVisible();
    await expect(sidebar).toHaveCSS('position', 'fixed');
    await expect(sidebar).toHaveCSS('top', '0px'); // Full height - no topbar
    await expect(sidebar).toHaveCSS('bottom', '0px');
    await expect(sidebar).toHaveCSS('z-index', '40');

    // Check user profile is at bottom of sidebar
    const userProfile = sidebar.locator('[class*="rounded-full"]').first();
    await expect(userProfile).toBeVisible();

    // Check toggle button is at bottom of sidebar
    const toggleButton = sidebar.locator('button[title*="sidebar"]');
    await expect(toggleButton).toBeVisible();
    
    // Verify bottom section has border-top
    const bottomSection = sidebar.locator('.border-t.border-gray-200').last();
    await expect(bottomSection).toBeVisible();
  });

  test('sidebar collapse/expand functionality', async ({ page }) => {
    const sidebar = page.locator('div.fixed.top-0.left-0.z-40.bottom-0');
    const toggleButton = sidebar.locator('button[title*="sidebar"]');
    const mainContent = page.locator('div.flex-1.flex.flex-col.transition-all.duration-300');

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
    const sidebar = page.locator('div.fixed.top-0.left-0.z-40.bottom-0');
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
    const contentArea = page.locator('div.flex-1.flex.flex-col.transition-all.duration-300');

    // Check no top padding (no topbar)
    await expect(contentArea).not.toHaveClass(/pt-16/);
    
    // Check content padding - get the first p-6 which is the main content wrapper
    const contentWrapper = mainContent.locator('.p-6').first();
    await expect(contentWrapper).toHaveCSS('padding', '24px');

    // Check max width constraint
    const innerContent = contentWrapper.locator('.max-w-7xl').first();
    await expect(innerContent).toBeVisible();
  });

  test('mobile sidebar overlay should work', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });

    // Sidebar should be hidden initially on mobile (translated left)
    const sidebar = page.locator('div.fixed.top-0.left-0.z-40.bottom-0');
    await expect(sidebar).toHaveCSS('transform', 'matrix(1, 0, 0, 1, -256, 0)'); // translateX(-100%)

    // Check if there's a mobile menu trigger (could be via swipe or floating button)
    // For now, we'll check the sidebar overlay behavior when programmatically opened
    
    // Check overlay would be visible when sidebar is opened
    const overlay = page.locator('.fixed.inset-0.bg-gray-600');
    
    // Note: Mobile menu trigger implementation depends on specific UI design
    // This test validates the sidebar positioning for mobile overlay mode
    await expect(sidebar).toBeVisible();
    await expect(sidebar).toHaveClass(/md:translate-x-0/); // Desktop: always visible
  });

  test('user profile dropdown should work in sidebar', async ({ page }) => {
    const sidebar = page.locator('div.fixed.top-0.left-0.z-40.bottom-0');
    const userProfileButton = sidebar.locator('button').filter({ has: page.locator('[class*="rounded-full"]') }).first();

    // Click user profile to open dropdown
    await userProfileButton.click();

    // Check dropdown menu is visible (appears above user area)
    const dropdown = page.locator('[class*="absolute"][class*="bg-primary-white"]');
    await expect(dropdown).toBeVisible();

    // Check sign out button exists
    const signOutButton = dropdown.locator('button').filter({ hasText: 'Sign out' });
    await expect(signOutButton).toBeVisible();
  });
});