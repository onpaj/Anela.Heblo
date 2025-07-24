import { test, expect } from '@playwright/test';

test.describe('Weather API Debug', () => {
  test('should debug weather API calls and responses', async ({ page }) => {
    // Capture network requests
    const requests: any[] = [];
    const responses: any[] = [];

    page.on('request', request => {
      console.log('REQUEST:', request.method(), request.url());
      requests.push({
        method: request.method(),
        url: request.url(),
        headers: request.headers(),
        postData: request.postData()
      });
    });

    page.on('response', async response => {
      console.log('RESPONSE:', response.status(), response.url());
      
      if (response.url().includes('WeatherForecast') || response.url().includes('weather')) {
        try {
          const responseText = await response.text();
          console.log('Weather API Response Body:', responseText);
          responses.push({
            status: response.status(),
            url: response.url(),
            headers: response.headers(),
            body: responseText
          });
        } catch (error) {
          console.log('Failed to read response body:', error);
        }
      }
    });

    // Navigate to the application
    await page.goto('http://localhost:3000');
    await page.waitForLoadState('domcontentloaded');

    // Wait a bit for initial API calls
    await page.waitForTimeout(3000);

    // Check what API URL is being used (avoid process.env in browser)
    console.log('Checking API configuration in browser...');

    // Check if we see any error messages
    const errorElement = page.locator('text=Error loading weather data');
    const hasError = await errorElement.isVisible();
    
    if (hasError) {
      const errorText = await page.locator('[class*="bg-red-50"] p').textContent();
      console.log('Error message displayed:', errorText);
    }

    // Click reload button to trigger API call
    console.log('Clicking reload button...');
    const reloadButton = page.locator('button:has-text("Reload")');
    await reloadButton.click();

    // Wait for response
    await page.waitForTimeout(3000);

    // Check final state
    const weatherCards = page.locator('[class*="grid"] > div:has([class*="text-4xl"])');
    const cardCount = await weatherCards.count();
    const stillHasError = await errorElement.isVisible();

    console.log('Final state:');
    console.log('- Weather cards count:', cardCount);
    console.log('- Has error:', stillHasError);
    console.log('- Total requests made:', requests.length);
    console.log('- Weather API responses:', responses.length);

    // Print all weather-related requests/responses
    console.log('\n=== Weather API Requests ===');
    requests.forEach((req, i) => {
      if (req.url.includes('WeatherForecast') || req.url.includes('weather')) {
        console.log(`Request ${i + 1}:`, req);
      }
    });

    console.log('\n=== Weather API Responses ===');
    responses.forEach((res, i) => {
      console.log(`Response ${i + 1}:`, res);
    });

    // Take screenshot for debugging
    await page.screenshot({ path: 'test-results/weather-debug-final-state.png' });

    // Assert that we made at least one weather API call
    const weatherRequests = requests.filter(req => 
      req.url.includes('WeatherForecast') || req.url.includes('weather')
    );
    
    console.log('Weather requests found:', weatherRequests.length);
    
    if (weatherRequests.length === 0) {
      console.log('❌ No weather API requests were made!');
      console.log('All requests made:', requests.map(r => r.url));
    }
  });

  test('should check API configuration', async ({ page }) => {
    await page.goto('http://localhost:3000');
    
    console.log('Checking API configuration...');
    
    await page.waitForTimeout(1000);
  });
});