import { test, expect } from '@playwright/test';

test.describe('Dynamic Port Redirect', () => {
  test('should redirect to correct port 3001', async ({ page }) => {
    // Navigate to the application on port 3001
    await page.goto('http://localhost:3001');

    // Wait for the authentication redirect
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    const currentUrl = page.url();
    
    // Verify that the redirect URI points to port 3001 (not 3000)
    expect(currentUrl).toContain('redirect_uri=http%3A%2F%2Flocalhost%3A3001');
    
    // Make sure it's NOT pointing to port 3000
    expect(currentUrl).not.toContain('redirect_uri=http%3A%2F%2Flocalhost%3A3000');

    // Take a screenshot for verification
    await page.screenshot({ path: 'test-results/port-3001-redirect.png' });
  });

  test('should redirect to correct port 3000', async ({ page }) => {
    // Start server on port 3000 first
    await page.goto('http://localhost:3000');

    // Wait for the authentication redirect
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    const currentUrl = page.url();
    
    // Verify that the redirect URI points to port 3000
    expect(currentUrl).toContain('redirect_uri=http%3A%2F%2Flocalhost%3A3000');

    // Take a screenshot for verification
    await page.screenshot({ path: 'test-results/port-3000-redirect.png' });
  });

  test('should work on any port dynamically', async ({ page }) => {
    // Test port 3001
    await page.goto('http://localhost:3001');
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    let currentUrl = page.url();
    expect(currentUrl).toContain('redirect_uri=http%3A%2F%2Flocalhost%3A3001');

    // Navigate back and test that the origin is correctly detected
    await page.goto('http://localhost:3001');
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    currentUrl = page.url();
    expect(currentUrl).toContain('redirect_uri=http%3A%2F%2Flocalhost%3A3001');
  });
});