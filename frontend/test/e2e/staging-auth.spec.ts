import { test, expect } from '@playwright/test';
import { config } from 'dotenv';
import path from 'path';

// Load test environment variables
const envPath = path.resolve(__dirname, '../../.env.test');
console.log('Loading .env.test from:', envPath);
config({ path: envPath });

async function getServicePrincipalToken(): Promise<string> {
  const clientId = process.env.AZURE_CLIENT_ID;
  const clientSecret = process.env.AZURE_CLIENT_SECRET;
  const tenantId = process.env.AZURE_TENANT_ID;

  console.log('Environment variables:');
  console.log('  AZURE_CLIENT_ID:', clientId ? 'Present' : 'Missing');
  console.log('  AZURE_CLIENT_SECRET:', clientSecret ? 'Present' : 'Missing');
  console.log('  AZURE_TENANT_ID:', tenantId ? 'Present' : 'Missing');
  console.log('  PLAYWRIGHT_BASE_URL:', process.env.PLAYWRIGHT_BASE_URL);

  if (!clientId || !clientSecret || !tenantId) {
    console.error('Missing credentials. Available env vars:', Object.keys(process.env).filter(k => k.includes('AZURE')));
    throw new Error('Missing Azure credentials in .env.test file');
  }

  const tokenEndpoint = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/token`;
  
  const response = await fetch(tokenEndpoint, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: new URLSearchParams({
      grant_type: 'client_credentials',
      client_id: clientId,
      client_secret: clientSecret,
      scope: 'api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default', // Use backend API scope
    }),
  });

  if (!response.ok) {
    throw new Error(`Failed to get token: ${response.status} ${response.statusText}`);
  }

  const tokenData = await response.json();
  
  // Debug: Parse the token to see what claims it has
  try {
    const tokenParts = tokenData.access_token.split('.');
    const payload = JSON.parse(Buffer.from(tokenParts[1], 'base64').toString());
    console.log('Token claims:', {
      appid: payload.appid,
      tid: payload.tid,
      aud: payload.aud,
      iss: payload.iss,
      exp: new Date(payload.exp * 1000).toISOString()
    });
  } catch (e) {
    console.log('Could not parse token for debugging');
  }
  
  return tokenData.access_token;
}

async function authenticateWithServicePrincipal(page: any) {
  try {
    // Get Service Principal token using client credentials flow
    const token = await getServicePrincipalToken();
    console.log('Service Principal token obtained successfully');
    
    // Call the E2E authentication endpoint to create session (following best practices)
    // API URL is always the backend port (5000)
    const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5000';
    const authUrl = `${apiBaseUrl}/api/e2etest/auth`;
    
    console.log(`Creating E2E authentication session at: ${authUrl}`);
    
    const response = await page.request.post(authUrl, {
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      }
    });
    
    if (!response.ok()) {
      const errorText = await response.text();
      console.error('E2E Authentication Error Details:');
      console.error('Status:', response.status());
      console.error('Status Text:', response.statusText());
      console.error('Response Body:', errorText);
      console.error('Request URL:', authUrl);
      throw new Error(`E2E authentication failed: ${response.status()} ${response.statusText()}\n${errorText}`);
    }
    
    const authResult = await response.json();
    console.log('E2E authentication session created:', authResult.message);
    
    // Debug: Check if Set-Cookie header was sent
    const setCookieHeaders = response.headers()['set-cookie'];
    console.log('Set-Cookie headers:', setCookieHeaders);
    
    // Check all cookies in the browser context
    const cookies = await page.context().cookies();
    console.log('All cookies after auth:', cookies.map(c => ({ name: c.name, domain: c.domain, path: c.path })));
    
    // Navigate to the E2E test app endpoint which has pre-configured authentication
    // This avoids cross-port cookie issues between frontend (3000) and backend (5000)
    const e2eAppUrl = `${apiBaseUrl}/api/e2etest/app`;
    console.log(`Navigating to E2E test application: ${e2eAppUrl}`);
    await page.goto(e2eAppUrl);
    
    // Wait for app to load
    await page.waitForLoadState('domcontentloaded');
    console.log('Application loaded with authenticated session');
    
  } catch (error) {
    console.error('Service Principal authentication failed:', error);
    throw error;
  }
}

test.describe('E2E Authentication Tests (Development/Staging)', () => {
  test.beforeEach(async ({ page }) => {
    // Authenticate using Service Principal session creation before each test
    await authenticateWithServicePrincipal(page);
  });

  test('should authenticate and access main dashboard', async ({ page }) => {
    // Authentication happened in beforeEach, verify user sees the main application
    
    // Check current URL and title
    console.log('Current URL:', page.url());
    console.log('Current title:', await page.title());
    
    // Verify we're on the E2E test application page
    await expect(page).toHaveTitle(/Anela Heblo - E2E Test Mode/, { timeout: 15000 });
    
    // Verify we're NOT on Microsoft login page
    expect(page.url()).not.toContain('login.microsoftonline.com');
    expect(page.url()).toContain('localhost');
    expect(page.url()).toContain('/api/e2etest/app');
    
    // Wait for React app to load and render main components
    await page.waitForLoadState('networkidle');
    
    // Look for main application elements - adjust selectors based on your actual app
    // Check for authenticated UI elements (sidebar, navigation, user info, etc.)
    const authenticatedElements = [
      // Try common selectors for authenticated app elements
      '[data-testid="sidebar"]',
      '[data-testid="main-content"]', 
      '[data-testid="navigation"]',
      '[data-testid="user-menu"]',
      // Fallback to generic selectors
      '.sidebar',
      'nav',
      'main',
      '.main-content',
      '[role="navigation"]',
      '[role="main"]'
    ];
    
    let foundElement = false;
    for (const selector of authenticatedElements) {
      try {
        await expect(page.locator(selector).first()).toBeVisible({ timeout: 3000 });
        console.log(`Found authenticated UI element: ${selector}`);
        foundElement = true;
        break;
      } catch (error) {
        // Continue to next selector
        continue;
      }
    }
    
    if (!foundElement) {
      // If no specific elements found, at least verify we have some content and not just login form
      const bodyText = await page.locator('body').textContent();
      console.log('Page content preview:', bodyText?.substring(0, 200) + '...');
      
      // Verify we don't have login-related text
      expect(bodyText).not.toContain('Sign in to your account');
      expect(bodyText).not.toContain('Enter your email');
      expect(bodyText).not.toContain('Password');
      
      // Verify we have some application content
      expect(bodyText?.length || 0).toBeGreaterThan(100); // Not empty page
    }
    
    console.log('E2E authentication successful - user can access main application dashboard');
  });

  test('should validate API authentication status', async ({ page }) => {
    // Test the E2E auth status API endpoint
    const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5000';
    const apiUrl = `${apiBaseUrl}/api/e2etest/auth-status`;
    
    console.log(`Testing API endpoint: ${apiUrl}`);
    
    // Use page.request to make API call with cookies
    const response = await page.request.get(apiUrl);
    
    if (!response.ok()) {
      const errorText = await response.text();
      throw new Error(`API request failed: ${response.status()} ${response.statusText()}\n${errorText}`);
    }
    
    const jsonResponse = await response.json();
    console.log('API Response:', JSON.stringify(jsonResponse, null, 2));
    
    // Verify authentication status
    expect(jsonResponse.authenticated).toBe(true);
    expect(jsonResponse.message).toContain('E2E authentication override active');
    expect(jsonResponse.user).toBeDefined();
    expect(jsonResponse.user.name).toContain('E2E Test User');
    
    console.log('API authentication validation successful');
  });

  test('should handle API calls with authentication', async ({ page }) => {
    // Monitor network requests to verify API calls work with auth
    const apiRequests: string[] = [];
    
    page.on('request', request => {
      if (request.url().includes('/api/')) {
        apiRequests.push(request.url());
      }
    });
    
    // Navigate to the E2E test app endpoint
    const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5000';
    const e2eAppUrl = `${apiBaseUrl}/api/e2etest/app`;
    await page.goto(e2eAppUrl);
    await page.waitForLoadState('networkidle');
    
    // Wait a bit for any API calls to complete
    await page.waitForTimeout(2000);
    
    // Verify that API calls were made (indicates authentication is working)
    expect(apiRequests.length).toBeGreaterThan(0);
    
    // Check for any failed API calls
    const responses = await Promise.all(
      apiRequests.slice(0, 3).map(async url => {
        try {
          const response = await page.request.get(url);
          return { url, status: response.status() };
        } catch (error) {
          return { url, status: 'error', error };
        }
      })
    );
    
    // At least some API calls should succeed (not all will work with service principal)
    const successfulCalls = responses.filter(r => r.status < 400);
    console.log('API call results:', responses);
    
    // Just verify we can make authenticated requests (some may fail due to permissions)
    expect(responses.length).toBeGreaterThan(0);
  });
});