import { Page } from '@playwright/test';
import { config } from 'dotenv';
import path from 'path';

// Load test environment variables
const envPath = path.resolve(__dirname, '../../../.env.test');
config({ path: envPath });

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

/**
 * Creates an E2E authentication session using Service Principal token
 */
export async function createE2EAuthSession(page: Page): Promise<void> {
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
    
    const authResult = await response.json();
    console.log('E2E authentication session created:', authResult.message);
    
  } catch (error) {
    console.error('Error creating E2E authentication session:', error);
    throw error;
  }
}