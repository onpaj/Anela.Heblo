import { test, expect } from '@playwright/test';

test.describe('Mock Authentication', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the application
    await page.goto('http://localhost:3000');
    await page.waitForLoadState('domcontentloaded');
  });

  test('should use mock authentication in development mode', async ({ page }) => {
    // Wait for authentication to complete (mock auth has short delay)
    await page.waitForTimeout(500);

    // Check that we're not redirected to Microsoft login
    expect(page.url()).toBe('http://localhost:3000/');
    expect(page.url()).not.toContain('login.microsoftonline.com');

    // Check that the main application content is visible (not login screen)
    await expect(page.locator('h1:has-text("Weather Forecast")')).toBeVisible();
    
    // Take screenshot to verify we're on the main app page
    await page.screenshot({ path: 'test-results/mock-auth-main-page.png' });
  });

  test('should display mock user information in user profile', async ({ page }) => {
    // Wait for authentication to complete
    await page.waitForTimeout(500);

    // Look for user profile component in sidebar (should show initials)
    const userProfileButton = page.locator('button:has([class*="bg-indigo-600"])').first();
    await expect(userProfileButton).toBeVisible();

    // Check that mock user initials are displayed (MU for Mock User)
    await expect(userProfileButton.locator('span:has-text("MU")')).toBeVisible();

    // Click on user profile to open menu
    await userProfileButton.click();
    
    // Wait for menu to appear
    await page.waitForTimeout(200);

    // Verify mock user data is displayed in the menu (be more specific to avoid strict mode violations)
    await expect(page.locator('[class*="bg-white"] p:has-text("Mock User")').first()).toBeVisible();
    await expect(page.locator('[class*="bg-white"] p:has-text("mock.user@example.com")').first()).toBeVisible();
    
    // Check for admin role badge
    await expect(page.locator('[class*="bg-indigo-100"]:has-text("admin")')).toBeVisible();

    // Check for last login timestamp
    await expect(page.locator('text=Last login:')).toBeVisible();

    // Take screenshot of user profile menu
    await page.screenshot({ path: 'test-results/mock-auth-user-profile.png' });
  });

  test('should provide mock access token functionality', async ({ page }) => {
    // Wait for authentication to complete
    await page.waitForTimeout(500);

    // Check that authentication state is properly set by evaluating window object
    const authState = await page.evaluate(() => {
      // Check if mock auth is working by trying to access sessionStorage
      // UserStorage uses sessionStorage with key 'anela_heblo_user_info'
      const storedUser = sessionStorage.getItem('anela_heblo_user_info');
      return {
        hasStoredUser: !!storedUser,
        storedUserData: storedUser ? JSON.parse(storedUser) : null
      };
    });

    // Verify that mock user data is stored
    expect(authState.hasStoredUser).toBeTruthy();
    expect(authState.storedUserData.name).toBe('Mock User');
    expect(authState.storedUserData.email).toBe('mock.user@example.com');
    expect(authState.storedUserData.initials).toBe('MU');
    expect(authState.storedUserData.roles).toContain('admin');
  });

  test('should allow mock user logout functionality', async ({ page }) => {
    // Wait for authentication to complete
    await page.waitForTimeout(500);

    // Open user profile menu
    const userProfileButton = page.locator('button:has([class*="bg-indigo-600"])').first();
    await userProfileButton.click();
    await page.waitForTimeout(200);

    // Click logout button
    const logoutButton = page.locator('button:has-text("Sign out")');
    await expect(logoutButton).toBeVisible();
    await logoutButton.click();

    // Wait for logout to complete
    await page.waitForTimeout(500);

    // Check that user is logged out - should see sign in button
    await expect(page.locator('button:has-text("Sign in")')).toBeVisible();
    
    // Verify that mock user data is cleared from sessionStorage
    const authStateAfterLogout = await page.evaluate(() => {
      const storedUser = sessionStorage.getItem('anela_heblo_user_info');
      return {
        hasStoredUser: !!storedUser,
        storedUserData: storedUser ? JSON.parse(storedUser) : null
      };
    });

    expect(authStateAfterLogout.hasStoredUser).toBeFalsy();

    // Take screenshot of logged out state
    await page.screenshot({ path: 'test-results/mock-auth-logged-out.png' });
  });

  test('should re-authenticate mock user after logout', async ({ page }) => {
    // Wait for initial authentication
    await page.waitForTimeout(500);

    // Logout first
    const userProfileButton = page.locator('button:has([class*="bg-indigo-600"])').first();
    await userProfileButton.click();
    await page.waitForTimeout(200);
    
    const logoutButton = page.locator('button:has-text("Sign out")');
    await logoutButton.click();
    await page.waitForTimeout(500);

    // Now try to login again
    const signInButton = page.locator('button:has-text("Sign in")');
    await expect(signInButton).toBeVisible();
    await signInButton.click();

    // Wait for mock login to complete
    await page.waitForTimeout(500);

    // Check that user is authenticated again
    const userProfileButtonAfterLogin = page.locator('button:has([class*="bg-indigo-600"])').first();
    await expect(userProfileButtonAfterLogin).toBeVisible();
    await expect(userProfileButtonAfterLogin.locator('span:has-text("MU")')).toBeVisible();

    // Verify weather page is accessible again
    await expect(page.locator('h1:has-text("Weather Forecast")')).toBeVisible();

    // Take screenshot of re-authenticated state
    await page.screenshot({ path: 'test-results/mock-auth-re-authenticated.png' });
  });

  test('should display environment-specific mock auth indicators', async ({ page }) => {
    // Wait for authentication to complete
    await page.waitForTimeout(500);

    // Check browser console for mock auth indicators
    const consoleLogs: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'log' || msg.type() === 'warn' || msg.type() === 'info') {
        consoleLogs.push(msg.text());
      }
    });

    // Reload page to capture console logs
    await page.reload();
    await page.waitForTimeout(1000);

    // Open user profile to trigger any auth-related logging
    const userProfileButton = page.locator('button:has([class*="bg-indigo-600"])').first();
    await userProfileButton.click();
    await page.waitForTimeout(200);

    // Verify that mock auth is working (should see mock user data, be more specific)
    await expect(page.locator('[class*="bg-white"] p:has-text("Mock User")').first()).toBeVisible();
    
    console.log('Console logs captured:', consoleLogs);
    
    // Take final screenshot showing mock auth is active
    await page.screenshot({ path: 'test-results/mock-auth-environment-check.png' });
  });

  test('should handle rapid authentication state changes', async ({ page }) => {
    // Test that mock auth handles quick login/logout cycles without errors
    await page.waitForTimeout(500);

    for (let i = 0; i < 3; i++) {
      // Logout
      const userProfileButton = page.locator('button:has([class*="bg-indigo-600"])').first();
      await userProfileButton.click();
      await page.waitForTimeout(100);
      
      const logoutButton = page.locator('button:has-text("Sign out")');
      await logoutButton.click();
      await page.waitForTimeout(200);

      // Login again
      const signInButton = page.locator('button:has-text("Sign in")');
      await signInButton.click();
      await page.waitForTimeout(300);

      // Verify state
      const userProfileAfterLogin = page.locator('button:has([class*="bg-indigo-600"])').first();
      await expect(userProfileAfterLogin).toBeVisible();
    }

    // Final verification - should still be logged in
    await expect(page.locator('h1:has-text("Weather Forecast")')).toBeVisible();
    
    // Take screenshot of final state
    await page.screenshot({ path: 'test-results/mock-auth-rapid-changes.png' });
  });
});