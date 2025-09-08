import { test, expect } from '@playwright/test';
import { config } from 'dotenv';
import path from 'path';

// Load test environment variables
const envPath = path.resolve(__dirname, '../../.env.test');
config({ path: envPath });

// Override URLs for direct testing against dev ports
process.env.PLAYWRIGHT_BASE_URL = 'http://localhost:5000';

async function getServicePrincipalToken(): Promise<string> {
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

test.describe('Catalog API E2E Tests (Direct)', () => {
  test('should be able to call catalog API directly with E2E session', async ({ page }) => {
    const apiBaseUrl = 'http://localhost:5000';
    
    // Create E2E authentication session
    const token = await getServicePrincipalToken();
    const authResponse = await page.request.post(`${apiBaseUrl}/api/e2etest/auth`, {
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      }
    });
    
    expect(authResponse.ok()).toBeTruthy();
    console.log('✅ E2E auth session created');
    
    // Now try to call catalog API - this should work because we have E2E session
    const catalogResponse = await page.request.get(`${apiBaseUrl}/api/catalog?pageNumber=1&pageSize=5`);
    
    console.log('📊 Catalog API response status:', catalogResponse.status());
    
    if (catalogResponse.ok()) {
      const catalogData = await catalogResponse.json();
      console.log('✅ Catalog API call successful! Items count:', catalogData.items?.length || 0);
      expect(catalogData).toBeDefined();
      expect(catalogData.items).toBeDefined();
    } else {
      const errorText = await catalogResponse.text();
      console.error('❌ Catalog API call failed:', errorText);
      throw new Error(`Catalog API failed with ${catalogResponse.status()}: ${errorText}`);
    }
  });
});