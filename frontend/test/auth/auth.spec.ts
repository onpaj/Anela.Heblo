import { test, expect } from '@playwright/test';

test.describe('Authentication Flow', () => {
  test('should redirect to Microsoft login when not authenticated', async ({ page }) => {
    // Navigate to the application
    await page.goto('http://localhost:3000');

    // Wait for the authentication redirect to complete
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    // Verify we're on Microsoft's login page
    expect(page.url()).toContain('login.microsoftonline.com');
    expect(page.url()).toContain('oauth2/v2.0/authorize');

    // Check that we see Microsoft's login page
    await expect(page).toHaveTitle('Sign in to your account');

    // Verify the login form elements are present
    const emailInput = page.locator('input[type="email"], input[name="loginfmt"]');
    await expect(emailInput.first()).toBeVisible();

    // Take a screenshot for verification
    await page.screenshot({ path: 'test-results/microsoft-login-page.png' });
  });

  test('should have correct OAuth parameters in redirect URL', async ({ page }) => {
    // Navigate to the application
    await page.goto('http://localhost:3000');

    // Wait for the authentication redirect
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    const currentUrl = page.url();
    
    // Verify OAuth parameters
    expect(currentUrl).toContain('client_id=87193df0-3128-44d2-8673-580e97631a07');
    expect(currentUrl).toContain('scope=User.Read%20openid%20profile%20offline_access');
    expect(currentUrl).toContain('response_type=code');
    expect(currentUrl).toContain('redirect_uri=http%3A%2F%2Flocalhost%3A3000');

    // Verify tenant ID
    expect(currentUrl).toContain('31fd4df1-b9c0-4abd-a4b0-0e1aceaabe9a');
  });

  test('should prevent access to application without authentication', async ({ page }) => {
    // Try to navigate directly to the application
    await page.goto('http://localhost:3000');

    // Wait for redirect
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    // Verify we cannot access the main application
    expect(page.url()).not.toContain('localhost:3000');
    
    // Try to go back to the app
    await page.goto('http://localhost:3000');
    
    // Should redirect again
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });
    
    // Take a screenshot
    await page.screenshot({ path: 'test-results/authentication-required.png' });
  });

  test('should handle authentication flow initiation correctly', async ({ page }) => {
    // Navigate to the application  
    await page.goto('http://localhost:3000');

    // The page should immediately redirect to Microsoft login
    // Wait for either the redirect or a loading state (if visible briefly)
    await Promise.race([
      page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 5000 }),
      page.waitForSelector('.animate-spin', { timeout: 1000 }).catch(() => {})
    ]);

    // After any brief loading, we should be on Microsoft's login page
    await page.waitForURL('**/oauth2/v2.0/authorize**', { timeout: 10000 });

    // Verify we're on the correct authentication endpoint
    expect(page.url()).toContain('login.microsoftonline.com');
    expect(page.url()).toContain('oauth2/v2.0/authorize');
  });
});