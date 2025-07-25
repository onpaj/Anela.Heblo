import { test, expect } from '@playwright/test';

test.describe('Weather API Detailed Debug', () => {
  test('should capture detailed response information', async ({ page }) => {
    // Listen for console messages from the browser
    page.on('console', msg => {
      console.log('BROWSER CONSOLE:', msg.type(), msg.text());
    });

    // Capture failed requests with details
    page.on('requestfailed', request => {
      console.log('REQUEST FAILED:', request.url(), request.failure()?.errorText);
    });

    // Capture all responses including failures
    page.on('response', async response => {
      if (response.url().includes('WeatherForecast')) {
        console.log('=== WEATHER API RESPONSE ===');
        console.log('Status:', response.status());
        console.log('Status Text:', response.statusText());
        console.log('URL:', response.url());
        console.log('Headers:', response.headers());
        
        try {
          const responseText = await response.text();
          console.log('Response Body:', responseText);
        } catch (error) {
          console.log('Failed to read response body:', error);
        }
      }
    });

    // Navigate to page
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // Wait for component to load and make initial API call
    await page.waitForTimeout(5000);

    // Check current state
    const errorElement = page.locator('text=Error loading weather data');
    const hasError = await errorElement.isVisible();
    
    if (hasError) {
      const errorMessage = await page.locator('[class*="bg-red-50"] p').textContent();
      console.log('Error displayed on page:', errorMessage);
    }

    // Try reload to trigger another API call
    const reloadButton = page.locator('button:has-text("Reload")');
    await reloadButton.click();
    
    // Wait for response
    await page.waitForTimeout(5000);

    // Take screenshot
    await page.screenshot({ path: 'test-results/weather-detailed-debug.png' });
  });

  test('should test API endpoint directly', async ({ page }) => {
    // Navigate to a blank page first
    await page.goto('about:blank');
    
    // Try to fetch the API directly from browser context
    const result = await page.evaluate(async () => {
      try {
        const response = await fetch('https://localhost:44390/WeatherForecast', {
          method: 'GET',
          headers: {
            'Content-Type': 'application/json',
          }
        });
        
        return {
          ok: response.ok,
          status: response.status,
          statusText: response.statusText,
          headers: Object.fromEntries(response.headers.entries()),
          body: response.ok ? await response.text() : 'Failed to read body'
        };
      } catch (error) {
        return {
          error: error.message,
          type: error.constructor.name
        };
      }
    });
    
    console.log('Direct API fetch result:', result);
  });
});