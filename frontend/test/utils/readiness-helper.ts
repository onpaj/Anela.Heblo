import { Page, expect } from '@playwright/test';

/**
 * Waits for the application to be ready by checking the health/ready endpoint.
 * This ensures all background services have completed their initial load.
 * 
 * @param page Playwright page object
 * @param timeout Maximum time to wait in milliseconds (default: 60 seconds)
 * @returns Promise that resolves when the application is ready
 */
export async function waitForApplicationReady(page: Page, timeout: number = 60000): Promise<void> {
  const startTime = Date.now();
  
  while (Date.now() - startTime < timeout) {
    try {
      // Check the readiness endpoint
      const response = await page.request.get('/health/ready');
      
      if (response.ok()) {
        const healthData = await response.json();
        
        // Check if all services are ready
        if (healthData.status === 'Healthy') {
          console.log('Application is ready - all background services initialized');
          return;
        }
        
        // Log current status for debugging
        const entries = healthData.entries || {};
        const serviceStatuses = Object.keys(entries).map(key => {
          const entry = entries[key];
          return `${key}: ${entry.status || 'Unknown'}`;
        }).join(', ');
        
        console.log(`Waiting for services to be ready: ${serviceStatuses}`);
      } else {
        console.log(`Health check returned status: ${response.status()}`);
      }
    } catch (error) {
      console.log(`Health check failed: ${error.message}`);
    }
    
    // Wait 2 seconds before retrying
    await page.waitForTimeout(2000);
  }
  
  throw new Error(`Application did not become ready within ${timeout}ms`);
}

/**
 * Waits for basic application load (DOM ready) without waiting for background services.
 * Use this for tests that don't depend on full application readiness.
 */
export async function waitForBasicLoad(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
}

/**
 * Enhanced goto that waits for application readiness.
 * Use this in tests that require full application initialization.
 */
export async function gotoAndWaitReady(page: Page, url: string, timeout?: number): Promise<void> {
  await page.goto(url);
  await waitForApplicationReady(page, timeout);
}