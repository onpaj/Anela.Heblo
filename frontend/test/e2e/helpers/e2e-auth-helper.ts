import { config } from 'dotenv';
import path from 'path';

// Load test environment variables
const envPath = path.resolve(__dirname, '../../../.env.test');
config({ path: envPath });

export async function getServicePrincipalToken(): Promise<string> {
  const clientId = process.env.AZURE_CLIENT_ID;
  const clientSecret = process.env.AZURE_CLIENT_SECRET;
  const tenantId = process.env.AZURE_TENANT_ID;

  if (!clientId || !clientSecret || !tenantId) {
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
  try {
    const token = await getServicePrincipalToken();
    const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL;
    const authUrl = `${apiBaseUrl}/api/e2etest/auth`;
    
    const response = await page.request.post(authUrl, {
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      }
    });
    
    if (!response.ok()) {
      const errorText = await response.text();
      throw new Error(`E2E authentication failed: ${response.status()} ${response.statusText()}\n${errorText}`);
    }
  } catch (error) {
    console.error('Service Principal authentication failed:', error);
    throw error;
  }
}

export async function navigateToApp(page: any): Promise<void> {
  // First, establish E2E session with backend
  const apiBaseUrl = process.env.PLAYWRIGHT_BASE_URL;
  const e2eAppUrl = `${apiBaseUrl}/api/e2etest/app`;
  
  await page.goto(e2eAppUrl);
  await page.waitForLoadState('domcontentloaded');
  
  // Extract E2E session cookie and set it for frontend domain
  const cookies = await page.context().cookies();
  const e2eCookie = cookies.find(c => c.name === 'E2ETestSession');
  if (e2eCookie) {
    await page.context().addCookies([{
      name: e2eCookie.name,
      value: e2eCookie.value,
      domain: 'localhost',
      path: '/',
      httpOnly: true,
      sameSite: 'Lax'
    }]);
  }
  
  // Navigate to root first, then use UI navigation
  const frontendUrl = process.env.PLAYWRIGHT_FRONTEND_URL;
  const frontendWithE2E = `${frontendUrl}?e2e=true`;
  
  // Get token for sessionStorage
  const token = await getServicePrincipalToken();
  
  await page.goto(frontendWithE2E);
  await page.waitForLoadState('domcontentloaded');
  
  // Store the E2E token in sessionStorage so frontend can use it
  await page.evaluate((token) => {
    sessionStorage.setItem('e2e-test-token', token);
  }, token);
  
  // Wait for the application to load
  await page.waitForTimeout(3000);
}

export async function navigateToTransportBoxes(page: any): Promise<void> {
  await navigateToApp(page);
  
  // Navigate to transport boxes via UI
  // First, try to find and click on "Logistika" section
  const logistikaSelector = page.locator('text="Logistika"').first();
  try {
    if (await logistikaSelector.isVisible({ timeout: 2000 })) {
      await logistikaSelector.click();
      await page.waitForTimeout(1000);
      
      // Then click on "Transportní boxy" sub-item
      const transportBoxy = page.locator('text="Transportní boxy"').first();
      if (await transportBoxy.isVisible({ timeout: 2000 })) {
        await transportBoxy.click();
        await page.waitForLoadState('domcontentloaded');
        await page.waitForTimeout(2000);
      }
    }
  } catch (e) {
    // If UI navigation fails, go directly to the path
    const frontendUrl = process.env.PLAYWRIGHT_FRONTEND_URL;
    await page.goto(`${frontendUrl}/logistics/transport-boxes?e2e=true`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);
  }
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
    const frontendUrl = process.env.PLAYWRIGHT_FRONTEND_URL;
    await page.goto(`${frontendUrl}/catalog?e2e=true`);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);
  }
}