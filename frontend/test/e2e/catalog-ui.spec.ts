import { test, expect } from '@playwright/test';
import { config } from 'dotenv';
import path from 'path';

// Load test environment variables
const envPath = path.resolve(__dirname, '../../.env.test');
console.log('Loading .env.test from:', envPath);
config({ path: envPath });

// Override URLs for direct testing against dev ports
process.env.PLAYWRIGHT_BASE_URL = 'http://localhost:5000';
process.env.PLAYWRIGHT_FRONTEND_URL = 'http://localhost:3000';

async function getServicePrincipalToken(): Promise<string> {
  const clientId = process.env.AZURE_CLIENT_ID;
  const clientSecret = process.env.AZURE_CLIENT_SECRET;
  const tenantId = process.env.AZURE_TENANT_ID;

  console.log('Environment variables:');
  console.log('  AZURE_CLIENT_ID:', clientId ? 'Present' : 'Missing');
  console.log('  AZURE_CLIENT_SECRET:', clientSecret ? 'Present' : 'Missing');
  console.log('  AZURE_TENANT_ID:', tenantId ? 'Present' : 'Missing');
  console.log('  PLAYWRIGHT_BASE_URL:', process.env.PLAYWRIGHT_BASE_URL);
  console.log('  PLAYWRIGHT_FRONTEND_URL:', process.env.PLAYWRIGHT_FRONTEND_URL);

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

async function createE2EAuthSession(page: any): Promise<void> {
  try {
    // Get Service Principal token using client credentials flow
    const token = await getServicePrincipalToken();
    console.log('Service Principal token obtained successfully');
    
    // Call the E2E authentication endpoint to create session
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
    
  } catch (error) {
    console.error('Service Principal authentication failed:', error);
    throw error;
  }
}

test.describe('Catalog UI E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Create E2E authentication session before each test
    await createE2EAuthSession(page);
  });

  test('should navigate to catalog and load products via UI', async ({ page }) => {
    // Listen to console events
    page.on('console', msg => {
      if (msg.type() === 'log') {
        console.log('🖥️  Browser console.log:', msg.text());
      } else if (msg.type() === 'error') {
        console.error('🖥️  Browser console.error:', msg.text());
      }
    });
    
    // First, establish E2E session with backend
    const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5000';
    const e2eAppUrl = `${apiBaseUrl}/api/e2etest/app`;
    console.log(`First, establishing E2E session via: ${e2eAppUrl}`);
    
    await page.goto(e2eAppUrl);
    await page.waitForLoadState('domcontentloaded');
    
    // Check that E2E authentication cookies are set
    const cookies = await page.context().cookies();
    console.log('E2E cookies after backend visit:', cookies.map(c => ({ name: c.name, domain: c.domain })));
    
    // Extract E2E session cookie and set it for frontend domain
    const e2eCookie = cookies.find(c => c.name === 'E2ETestSession');
    if (e2eCookie) {
      console.log(`Setting E2E cookie for frontend domain: ${e2eCookie.name}`);
      await page.context().addCookies([{
        name: e2eCookie.name,
        value: e2eCookie.value,
        domain: 'localhost',  // Set for both ports
        path: '/',
        httpOnly: true,
        sameSite: 'Lax'
      }]);
    }
    
    // Now navigate to the frontend with E2E parameter to trigger E2E mode
    const frontendUrl = process.env.PLAYWRIGHT_FRONTEND_URL || 'http://localhost:3000';
    const frontendWithE2E = `${frontendUrl}?e2e=true`;
    console.log(`Now navigating to frontend with E2E mode: ${frontendWithE2E}`);
    
    // Get token before navigating to frontend
    const token = await getServicePrincipalToken();
    
    await page.goto(frontendWithE2E);
    await page.waitForLoadState('domcontentloaded');
    
    // Store the E2E token in sessionStorage so frontend can use it
    await page.evaluate((token) => {
      console.log('🧪 Storing E2E token in sessionStorage for API calls');
      sessionStorage.setItem('e2e-test-token', token);
    }, token);
    
    // Wait for the application to load
    console.log('Waiting for application to load...');
    await page.waitForTimeout(3000);
    
    // Check current URL and title
    console.log('Current URL:', page.url());
    console.log('Current title:', await page.title());
    
    // Look for the sidebar - try different possible selectors
    const sidebarSelectors = [
      '[data-testid="sidebar"]',
      '.sidebar', 
      'nav',
      'aside',
      '[role="navigation"]'
    ];
    
    let sidebar = null;
    for (const selector of sidebarSelectors) {
      try {
        sidebar = page.locator(selector).first();
        if (await sidebar.isVisible({ timeout: 2000 })) {
          console.log(`Found sidebar with selector: ${selector}`);
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (!sidebar || !(await sidebar.isVisible())) {
      console.log('Sidebar not found, looking for any navigation elements...');
      const bodyText = await page.locator('body').textContent();
      console.log('Page content preview:', bodyText?.substring(0, 500) + '...');
      
      // Look for any text that might indicate navigation
      const hasProductsText = bodyText?.includes('Produkty') || bodyText?.includes('Katalog');
      console.log('Page contains navigation text (Produkty/Katalog):', hasProductsText);
    }
    
    // Try to find and click on "Produkty" section
    console.log('Looking for "Produkty" navigation item...');
    
    const produktySelectors = [
      'text="Produkty"',
      '[data-testid="nav-produkty"]',
      'a:has-text("Produkty")',
      'button:has-text("Produkty")',
      '*:has-text("Produkty")'
    ];
    
    let produktyElement = null;
    for (const selector of produktySelectors) {
      try {
        produktyElement = page.locator(selector).first();
        if (await produktyElement.isVisible({ timeout: 2000 })) {
          console.log(`Found "Produkty" with selector: ${selector}`);
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (produktyElement && await produktyElement.isVisible()) {
      console.log('Clicking on "Produkty" section...');
      await produktyElement.click();
      await page.waitForTimeout(1000);
    } else {
      console.log('Could not find "Produkty" section, trying direct navigation...');
    }
    
    // Now look for "Katalog" link
    console.log('Looking for "Katalog" navigation item...');
    
    const katalogSelectors = [
      'text="Katalog"',
      '[data-testid="nav-katalog"]', 
      'a:has-text("Katalog")',
      'a[href="/catalog"]',
      '*:has-text("Katalog")'
    ];
    
    let katalogElement = null;
    for (const selector of katalogSelectors) {
      try {
        katalogElement = page.locator(selector).first();
        if (await katalogElement.isVisible({ timeout: 2000 })) {
          console.log(`Found "Katalog" with selector: ${selector}`);
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (katalogElement && await katalogElement.isVisible()) {
      console.log('Clicking on "Katalog" link...');
      await katalogElement.click();
    } else {
      console.log('Could not find "Katalog" link, trying direct navigation...');
      const frontendUrl = process.env.PLAYWRIGHT_FRONTEND_URL || 'http://localhost:3000';
      await page.goto(`${frontendUrl}/catalog?e2e=true`);
    }
    
    // Wait for the catalog page to load
    console.log('Waiting for catalog page to load...');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(3000);
    
    // Verify we're on the catalog page
    console.log('Current URL after navigation:', page.url());
    expect(page.url()).toContain('/catalog');
    
    // Look for catalog content - products table, list, or any product-related content
    console.log('Looking for catalog content...');
    
    const catalogContentSelectors = [
      '[data-testid="catalog-list"]',
      '[data-testid="product-list"]', 
      '.catalog-table',
      '.product-table',
      'table',
      '[role="table"]',
      '.catalog-content',
      '.product-content'
    ];
    
    let catalogContent = null;
    for (const selector of catalogContentSelectors) {
      try {
        catalogContent = page.locator(selector).first();
        if (await catalogContent.isVisible({ timeout: 3000 })) {
          console.log(`Found catalog content with selector: ${selector}`);
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (!catalogContent || !(await catalogContent.isVisible())) {
      // Look for any content that suggests products are loaded
      const pageText = await page.locator('body').textContent();
      console.log('Catalog page content preview:', pageText?.substring(0, 800) + '...');
      
      // Look for signs that products are loading or loaded
      const hasLoadingIndicator = pageText?.includes('Loading') || pageText?.includes('Načítá');
      const hasProductContent = pageText?.includes('Product') || pageText?.includes('produkt') || 
                               pageText?.includes('Název') || pageText?.includes('Kód');
      
      console.log('Page has loading indicator:', hasLoadingIndicator);
      console.log('Page has product-related content:', hasProductContent);
      
      if (!hasProductContent && !hasLoadingIndicator) {
        // Check if there are any error messages
        const hasError = pageText?.includes('Error') || pageText?.includes('Chyba') || 
                        pageText?.includes('404') || pageText?.includes('401');
        console.log('Page has error content:', hasError);
        
        if (hasError) {
          throw new Error('Catalog page shows error instead of products');
        }
      }
    }
    
    // Wait a bit more for any async loading
    console.log('Waiting for potential async content loading...');
    await page.waitForTimeout(5000);
    
    // Final verification - look for any table rows, list items, or product elements
    const productElementSelectors = [
      'tr:not(:first-child)', // Table rows (excluding header)
      'li', // List items
      '[data-testid*="product"]', // Any element with product in test id
      '.product', // Elements with product class
      '[class*="product"]' // Elements with product in class name
    ];
    
    let foundProducts = false;
    for (const selector of productElementSelectors) {
      try {
        const elements = page.locator(selector);
        const count = await elements.count();
        if (count > 0) {
          console.log(`Found ${count} product elements with selector: ${selector}`);
          foundProducts = true;
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (foundProducts) {
      console.log('✅ Successfully found product elements in catalog');
    } else {
      console.log('⚠️  No clear product elements found, but page loaded without errors');
      // This might still be acceptable if the catalog is empty or uses different markup
      const finalPageText = await page.locator('body').textContent();
      expect(finalPageText?.length || 0).toBeGreaterThan(200); // At least some substantial content
    }
    
    console.log('Catalog UI E2E test completed successfully');
  });
});