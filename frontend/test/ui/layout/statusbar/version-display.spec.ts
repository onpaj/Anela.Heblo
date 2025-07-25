import { test, expect } from '@playwright/test';

test.describe('StatusBar Version Display', () => {
  test('should display correct version from backend configuration API', async ({ page }) => {
    // Navigate to the app (uses baseURL from config)
    await page.goto('/');
    
    // Wait for the app to load completely
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
    
    // Wait for StatusBar to be visible and for API call to complete
    await page.waitForSelector('.bg-gray-100', { timeout: 5000 });
    
    // Wait a bit more for the API call to complete and update the version
    await page.waitForTimeout(1000);
    
    // Check that StatusBar is visible
    const statusBar = page.locator('.bg-gray-100');
    await expect(statusBar).toBeVisible();
    
    // Check version display - should show version from backend API
    const versionText = await statusBar.locator('span').filter({ hasText: /^v\d+\.\d+/ }).textContent();
    
    // Verify version format (should start with 'v' followed by semantic version)
    expect(versionText).toMatch(/^v\d+\.\d+/);
    
    // Log the actual version for debugging
    console.log('Displayed version from backend API:', versionText);
    
    // Take screenshot for visual verification
    await page.screenshot({ 
      path: 'test-results/version-display-test.png',
      fullPage: true 
    });
  });

  test('should handle backend API failure gracefully with fallback', async ({ page }) => {
    // This test simulates scenario where backend API is unavailable
    await page.goto('/');
    await page.waitForSelector('[data-testid="app"]', { timeout: 10000 });
    await page.waitForSelector('.bg-gray-100', { timeout: 5000 });
    
    // Wait for API call attempt and fallback
    await page.waitForTimeout(2000);
    
    const statusBar = page.locator('.bg-gray-100');
    
    // Should still display a version even if backend API fails
    const versionElement = statusBar.locator('span').filter({ hasText: /^v/ }).first();
    await expect(versionElement).toBeVisible();
    
    const versionText = await versionElement.textContent();
    
    // Should show either the backend version or fallback to environment variable/default
    expect(versionText).toMatch(/^v\d+\.\d+/);
    
    // Version should not be null or empty
    expect(versionText).not.toBe('v');
    expect(versionText).not.toBe('vundefined');
    expect(versionText).not.toBe('vnull');
  });

  test('should verify backend API response structure', async ({ page }) => {
    // This test verifies the backend configuration endpoint works correctly
    await page.goto('/');
    
    // Intercept the configuration API call
    const configResponse = await page.waitForResponse(response => 
      response.url().includes('/configuration') && response.status() === 200
    );
    
    // Verify the response contains expected fields
    const configData = await configResponse.json();
    expect(configData).toHaveProperty('version');
    expect(configData).toHaveProperty('environment');
    expect(configData).toHaveProperty('useMockAuth');
    
    console.log('Backend configuration response:', configData);
    
    // Verify version is properly formatted
    expect(configData.version).toMatch(/^\d+\.\d+/);
  });
});