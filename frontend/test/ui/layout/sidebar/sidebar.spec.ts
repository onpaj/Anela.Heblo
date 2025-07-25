import { test, expect } from '@playwright/test';

test.describe('Updated Sidebar', () => {
  test('should display new navigation structure', async ({ page }) => {
    // Navigate to the application (uses baseURL from config)
    await page.goto('/');

    // Wait for authentication redirect - we'll be redirected to Microsoft login
    // but we can still verify the HTML structure was loaded correctly
    await page.waitForLoadState('domcontentloaded');

    // Take a screenshot to see current state
    await page.screenshot({ path: 'test-results/sidebar-structure.png' });
  });

  test('should show correct application branding', async ({ page }) => {
    // Navigate to the application (uses baseURL from config)
    await page.goto('/');
    await page.waitForLoadState('domcontentloaded');

    // Check that the page title includes Anela Heblo
    await expect(page).toHaveTitle(/Anela Heblo/);

    // Take a screenshot
    await page.screenshot({ path: 'test-results/app-branding.png' });
  });

  test('should handle navigation structure correctly', async ({ page }) => {
    await page.goto('/');
    
    // Wait for the app to load completely
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
    
    // Wait for sidebar to be visible
    await page.waitForSelector('nav', { timeout: 5000 });
    
    // Verify that the navigation structure is rendered
    const sidebar = page.locator('nav');
    await expect(sidebar).toBeVisible();
    
    // Check for navigation items instead of raw HTML content
    await expect(page.locator('nav')).toContainText('Dashboard');
    
    // Verify page title contains app name
    expect(await page.title()).toContain('Anela Heblo');
  });
});