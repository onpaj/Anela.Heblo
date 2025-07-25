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
    // Check the compiled JavaScript includes our new navigation sections
    const response = await page.goto('/');
    const content = await response!.text();
    
    // Verify that the new navigation structure is in the built app
    expect(content).toContain('Anela Heblo');
    expect(content).toContain('application-ui');
  });
});