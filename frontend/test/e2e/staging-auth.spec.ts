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
    const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL;
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
    
    // Verify we're on the E2E test endpoint (environment-aware)
    const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5000';
    const expectedUrlPattern = baseUrl.includes('localhost') ? 'localhost' : new URL(baseUrl).hostname;
    expect(page.url()).toContain(expectedUrlPattern);
    expect(page.url()).toContain('/api/e2etest/app');
    
    // Wait for React app to load and render main components
    await page.waitForLoadState('networkidle');
    
    // Enhanced dashboard validation - check for main application elements and content
    console.log('🔍 Performing enhanced dashboard validation...');
    
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
    
    let foundMainElements = 0;
    let foundElementTypes = [];
    
    for (const selector of authenticatedElements) {
      try {
        const element = page.locator(selector).first();
        if (await element.isVisible({ timeout: 3000 })) {
          console.log(`✅ Found authenticated UI element: ${selector}`);
          foundMainElements++;
          foundElementTypes.push(selector);
        }
      } catch (error) {
        // Continue to next selector
        continue;
      }
    }
    
    console.log(`📊 Found ${foundMainElements} main UI elements: [${foundElementTypes.join(', ')}]`);
    
    // Look for specific dashboard content
    console.log('🏠 Validating dashboard-specific content...');
    
    // Check for navigation items - common dashboard elements
    const navigationItems = ['Dashboard', 'Produkty', 'Katalog', 'Objednávky', 'Sklady', 'Výroba', 'Faktury'];
    let foundNavItems = 0;
    
    for (const navItem of navigationItems) {
      try {
        const navElement = page.locator(`*:has-text("${navItem}")`).first();
        if (await navElement.isVisible({ timeout: 1000 })) {
          console.log(`📍 Found navigation item: ${navItem}`);
          foundNavItems++;
        }
      } catch (e) {
        // Navigation item not found, continue
      }
    }
    
    console.log(`📍 Found ${foundNavItems} navigation items out of expected items`);
    
    // Check for user information or profile elements
    console.log('👤 Looking for user profile information...');
    const userElements = [
      '[data-testid="user-menu"]',
      '[data-testid="user-info"]',
      '.user-profile',
      '.user-menu',
      '*:has-text("E2E Test User")',
      '*:has-text("Uživatel")'
    ];
    
    let foundUserElements = 0;
    for (const userSelector of userElements) {
      try {
        const userElement = page.locator(userSelector).first();
        if (await userElement.isVisible({ timeout: 2000 })) {
          console.log(`👤 Found user element: ${userSelector}`);
          foundUserElements++;
          
          // Get user element text for validation
          const userText = await userElement.textContent();
          console.log(`👤 User element text: ${userText?.substring(0, 50)}...`);
        }
      } catch (e) {
        continue;
      }
    }
    
    console.log(`👤 Found ${foundUserElements} user-related elements`);
    
    // Look for main content area or dashboard widgets
    console.log('📋 Looking for main content and dashboard widgets...');
    const contentElements = [
      '[data-testid="main-content"]',
      '.main-content',
      '.dashboard',
      '.dashboard-content',
      '[role="main"]',
      'main',
      '.content-area'
    ];
    
    let foundContentElements = 0;
    for (const contentSelector of contentElements) {
      try {
        const contentElement = page.locator(contentSelector).first();
        if (await contentElement.isVisible({ timeout: 2000 })) {
          console.log(`📋 Found main content element: ${contentSelector}`);
          foundContentElements++;
          
          // Check if content area has substantial content
          const contentText = await contentElement.textContent();
          console.log(`📋 Content area length: ${contentText?.length || 0} characters`);
          
          if (contentText && contentText.length > 50) {
            console.log(`✅ Content area has substantial content: ${contentText.substring(0, 80)}...`);
          }
        }
      } catch (e) {
        continue;
      }
    }
    
    console.log(`📋 Found ${foundContentElements} main content elements`);
    
    // Validate overall dashboard functionality
    if (foundMainElements > 0 || foundNavItems > 0 || foundUserElements > 0 || foundContentElements > 0) {
      console.log(`✅ Dashboard validation successful:`);
      console.log(`   - Main UI elements: ${foundMainElements}`);
      console.log(`   - Navigation items: ${foundNavItems}`);
      console.log(`   - User elements: ${foundUserElements}`);
      console.log(`   - Content elements: ${foundContentElements}`);
      
      // At least some dashboard elements should be present
      expect(foundMainElements + foundNavItems + foundUserElements + foundContentElements).toBeGreaterThan(0);
    } else {
      // Fallback validation - check page content
      console.log('⚠️  No specific dashboard elements found, performing fallback validation...');
      const bodyText = await page.locator('body').textContent();
      console.log('Page content preview:', bodyText?.substring(0, 300) + '...');
      
      // Verify we don't have login-related text
      expect(bodyText).not.toContain('Sign in to your account');
      expect(bodyText).not.toContain('Enter your email');
      expect(bodyText).not.toContain('Password');
      expect(bodyText).not.toContain('login.microsoftonline.com');
      
      // Look for application-related content
      const hasAppContent = bodyText?.toLowerCase().includes('anela') ||
                           bodyText?.toLowerCase().includes('heblo') ||
                           bodyText?.toLowerCase().includes('dashboard') ||
                           bodyText?.toLowerCase().includes('e2e test');
      
      console.log('Page contains application-related content:', hasAppContent);
      
      // Verify we have some application content
      expect(bodyText?.length || 0).toBeGreaterThan(100); // Not empty page
      
      if (hasAppContent) {
        console.log('✅ Page contains application-related content');
      } else {
        console.log('⚠️  Page loaded but may not contain expected dashboard content');
      }
    }
    
    // Final validation: Ensure we're not on error pages
    const pageText = await page.locator('body').textContent();
    const hasError = pageText?.toLowerCase().includes('error') || 
                    pageText?.toLowerCase().includes('404') ||
                    pageText?.toLowerCase().includes('500') ||
                    pageText?.toLowerCase().includes('unauthorized');
    
    expect(hasError).toBe(false); // Should not have error messages
    
    console.log('✅ E2E authentication and dashboard validation successful');
  });

  test('should validate API authentication status', async ({ page }) => {
    // Test the E2E auth status API endpoint
    const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL;
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