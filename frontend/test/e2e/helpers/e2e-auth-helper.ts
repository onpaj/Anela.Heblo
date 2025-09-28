import { config } from 'dotenv';
import path from 'path';

// Load test environment variables from .env.test if it exists (local development)
// In CI, environment variables are provided directly
const envPath = path.resolve(__dirname, '../../../.env.test');
config({ path: envPath });

export async function getServicePrincipalToken(): Promise<string> {
  // For E2E tests, use E2E_* prefixed environment variables
  const clientId = process.env.E2E_CLIENT_ID || process.env.AZURE_CLIENT_ID;
  const clientSecret = process.env.E2E_CLIENT_SECRET || process.env.AZURE_CLIENT_SECRET;
  const tenantId = process.env.AZURE_TENANT_ID;

  if (!clientId || !clientSecret || !tenantId) {
    throw new Error('Missing Azure service principal credentials. Set E2E_CLIENT_ID, E2E_CLIENT_SECRET, and AZURE_TENANT_ID environment variables.');
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
      scope: 'api://8b34be89-f86f-422f-af40-7dbcd30cb66a/.default',
    }),
  });

  if (!response.ok) {
    throw new Error(`Failed to get token: ${response.status} ${response.statusText}`);
  }

  const tokenData = await response.json();
  return tokenData.access_token;
}


export async function createE2EAuthSession(page: any): Promise<void> {
  const maxRetries = 3;
  const retryDelay = 5000; // 5 seconds
  
  for (let attempt = 1; attempt <= maxRetries; attempt++) {
    try {
      console.log(`🔐 E2E authentication attempt ${attempt}/${maxRetries}...`);
      const token = await getServicePrincipalToken();
      const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL;
      const authUrl = `${apiBaseUrl}/api/e2etest/auth`;
      
      const response = await page.request.post(authUrl, {
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        timeout: 90000 // 90 seconds timeout for auth calls
      });
      
      if (!response.ok()) {
        const errorText = await response.text();
        throw new Error(`E2E authentication failed: ${response.status()} ${response.statusText()}\n${errorText}`);
      }
      
      console.log(`✅ E2E authentication successful on attempt ${attempt}`);
      return; // Success, exit retry loop
      
    } catch (error) {
      console.error(`❌ E2E authentication attempt ${attempt} failed:`, error);
      
      if (attempt === maxRetries) {
        console.error('🚫 All authentication attempts failed');
        throw error;
      }
      
      console.log(`⏳ Waiting ${retryDelay/1000}s before retry...`);
      await new Promise(resolve => setTimeout(resolve, retryDelay));
    }
  }
}

export async function navigateToApp(page: any): Promise<void> {
  // Use service principal authentication for E2E tests
  await createE2EAuthSession(page);
  await navigateToAppWithServicePrincipal(page);
  
  // Wait for the application to load
  await page.waitForTimeout(3000);
}

async function navigateToAppWithServicePrincipal(page: any): Promise<void> {
  // First, establish E2E session with backend
  const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL;
  const e2eAppUrl = `${apiBaseUrl}/api/e2etest/app`;
  
  await page.goto(e2eAppUrl);
  await page.waitForLoadState('domcontentloaded');
  
  // Extract E2E session cookie and set it for frontend domain
  const cookies = await page.context().cookies();
  const e2eCookie = cookies.find(c => c.name === 'E2ETestSession');
  if (e2eCookie) {
    // Use the staging domain for the cookie, not localhost
    const stagingDomain = new URL(apiBaseUrl).hostname;
    await page.context().addCookies([{
      name: e2eCookie.name,
      value: e2eCookie.value,
      domain: stagingDomain,
      path: '/',
      httpOnly: true,
      sameSite: 'Lax'
    }]);
  }
  
  // Navigate to root first with E2E flag
  const frontendWithE2E = `${apiBaseUrl}?e2e=true`;
  
  // Get token for sessionStorage
  const token = await getServicePrincipalToken();
  
  await page.goto(frontendWithE2E);
  await page.waitForLoadState('networkidle'); // Wait for all network requests to complete
  
  // Store the E2E token in sessionStorage so frontend can use it
  await page.evaluate((token) => {
    sessionStorage.setItem('e2e-test-token', token);
  }, token);
  
  // Wait for React app to initialize
  await page.waitForFunction(() => {
    return document.querySelector('.App') !== null || 
           document.querySelector('#root > div') !== null ||
           document.querySelector('nav') !== null;
  }, { timeout: 30000 });
}

export async function navigateToTransportBoxes(page: any): Promise<void> {
  await navigateToApp(page);
  
  // Wait for app to be fully loaded
  await page.waitForTimeout(2000);
  
  // Navigate to transport boxes via UI
  // Based on debug info, try "Sklad" (Storage/Warehouse) instead of "Logistika"
  const skladSelector = page.locator('button').filter({ hasText: 'Sklad' }).first();
  try {
    console.log('🧭 Attempting UI navigation to transport boxes via Sklad...');
    if (await skladSelector.isVisible({ timeout: 5000 })) {
      console.log('✅ Found Sklad menu item, clicking...');
      await skladSelector.click();
      await page.waitForTimeout(2000);
      
      // Look for "Transportní boxy" sub-item after clicking Sklad
      const transportBoxy = page.locator('text="Transportní boxy"').first();
      if (await transportBoxy.isVisible({ timeout: 5000 })) {
        console.log('✅ Found Transportní boxy submenu, clicking...');
        await transportBoxy.click();
        await page.waitForLoadState('networkidle');
        await page.waitForTimeout(2000);
        
        // Verify we landed on the right page
        const hasTransportBoxContent = await page.locator('h1, h2, h3, [data-testid*="transport"], .transport').count() > 0;
        if (hasTransportBoxContent) {
          console.log('✅ UI navigation successful');
          return;
        }
      } else {
        console.log('❌ Transportní boxy submenu not found under Sklad');
      }
    } else {
      console.log('❌ Sklad menu item not found');
    }
  } catch (e) {
    console.log('❌ UI navigation failed:', e.message);
  }
  
  // If UI navigation fails, go directly to the path and handle the page differently
  console.log('🔄 Trying direct navigation...');
  const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/logistics/transport-boxes`);
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(5000);
  
  // Log what we actually got after direct navigation
  const currentUrl = page.url();
  const pageText = await page.textContent('body');
  console.log('📍 Current URL after direct navigation:', currentUrl);
  console.log('📝 Page content (first 200 chars):', pageText?.substring(0, 200));
  
  console.log('✅ Direct navigation completed');
}

export async function navigateToCatalog(page: any): Promise<void> {
  await navigateToApp(page);
  
  // Navigate to catalog via UI
  // First, try to find and click on "Produkty" section
  const produktySelector = page.locator('text="Produkty"').first();
  try {
    if (await produktySelector.isVisible({ timeout: 2000 })) {
      await produktySelector.click();
      await page.waitForTimeout(1000);
      
      // Then click on "Katalog" sub-item
      const katalog = page.locator('text="Katalog"').first();
      if (await katalog.isVisible({ timeout: 2000 })) {
        await katalog.click();
        await page.waitForLoadState('domcontentloaded');
        await page.waitForTimeout(2000);
      }
    }
  } catch (e) {
    // If UI navigation fails, go directly to the path
    const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
    await page.goto(`${baseUrl}/catalog`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);
  }
}