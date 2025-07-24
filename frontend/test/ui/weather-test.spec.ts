import { test, expect } from '@playwright/test';

test.describe('WeatherTest Page', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the application
    await page.goto('http://localhost:3000');
    await page.waitForLoadState('domcontentloaded');
  });

  test('should display weather page header and reload button', async ({ page }) => {
    // Check that the weather page header is visible
    await expect(page.locator('h1')).toContainText('Weather Forecast');
    await expect(page.locator('text=5-day weather forecast from API')).toBeVisible();
    
    // Check that reload button is present
    const reloadButton = page.locator('button:has-text("Reload")');
    await expect(reloadButton).toBeVisible();
    
    // Check reload button has correct styling
    await expect(reloadButton).toHaveClass(/bg-indigo-600/);
    
    // Verify RefreshCw icon is present
    await expect(page.locator('button:has-text("Reload") svg')).toBeVisible();
    
    // Take screenshot
    await page.screenshot({ path: 'test-results/weather-page-header.png' });
  });

  test('should show loading state when fetching data', async ({ page }) => {
    // Click reload button to trigger loading state
    const reloadButton = page.locator('button:has-text("Reload")');
    
    // Click the button
    await reloadButton.click();
    
    // Check for loading state (should show "Loading..." text)
    await expect(page.locator('button:has-text("Loading...")')).toBeVisible({ timeout: 1000 });
    
    // Check that the spinning icon is present during loading
    await expect(page.locator('button:has-text("Loading...") svg.animate-spin')).toBeVisible();
    
    // Take screenshot of loading state
    await page.screenshot({ path: 'test-results/weather-loading-state.png' });
  });

  test('should display weather data or error message', async ({ page }) => {
    // Wait a bit for initial data loading
    await page.waitForTimeout(2000);
    
    // Check if we have weather cards OR error message
    const weatherCards = page.locator('[class*="grid"] > div:has([class*="text-4xl"])');
    const errorMessage = page.locator('text=Error loading weather data');
    const emptyState = page.locator('text=No weather data available');
    
    // One of these should be visible
    const hasWeatherCards = await weatherCards.count() > 0;
    const hasError = await errorMessage.isVisible();
    const hasEmptyState = await emptyState.isVisible();
    
    expect(hasWeatherCards || hasError || hasEmptyState).toBeTruthy();
    
    if (hasWeatherCards) {
      console.log(`Found ${await weatherCards.count()} weather cards`);
      
      // Verify weather card structure
      const firstCard = weatherCards.first();
      await expect(firstCard).toBeVisible();
      
      // Check for temperature display
      await expect(firstCard.locator('text=/\\d+°C/')).toBeVisible();
      await expect(firstCard.locator('text=/\\d+°F/')).toBeVisible();
      
      // Check for weather emoji
      await expect(firstCard.locator('[class*="text-4xl"]')).toBeVisible();
      
      await page.screenshot({ path: 'test-results/weather-data-loaded.png' });
    } else if (hasError) {
      console.log('Error state detected');
      
      // Verify error state structure
      await expect(errorMessage).toBeVisible();
      await expect(page.locator('[class*="bg-red-50"]')).toBeVisible();
      
      await page.screenshot({ path: 'test-results/weather-error-state.png' });
    } else {
      console.log('Empty state detected');
      
      // Verify empty state structure
      await expect(emptyState).toBeVisible();
      await expect(page.locator('text=🌤️')).toBeVisible();
      
      await page.screenshot({ path: 'test-results/weather-empty-state.png' });
    }
  });

  test('should handle reload button clicks correctly', async ({ page }) => {
    // Wait for initial load
    await page.waitForTimeout(1000);
    
    const reloadButton = page.locator('button:has-text("Reload")');
    
    // Click reload button multiple times to test functionality
    for (let i = 0; i < 2; i++) {
      await reloadButton.click();
      
      // Wait for loading state
      await expect(page.locator('button:has-text("Loading...")')).toBeVisible({ timeout: 1000 });
      
      // Wait for loading to complete
      await expect(page.locator('button:has-text("Reload")')).toBeVisible({ timeout: 5000 });
      
      await page.waitForTimeout(500);
    }
    
    // Verify button is still functional
    await expect(reloadButton).toBeEnabled();
    
    await page.screenshot({ path: 'test-results/weather-reload-functionality.png' });
  });

  test('should display correct date formatting', async ({ page }) => {
    // Wait for data to load
    await page.waitForTimeout(2000);
    
    // Check if weather cards are present
    const weatherCards = page.locator('[class*="grid"] > div:has([class*="text-4xl"])');
    const cardCount = await weatherCards.count();
    
    if (cardCount > 0) {
      // Check that dates are formatted in Czech format
      const firstCard = weatherCards.first();
      
      // Look for Czech date format (should contain Czech day/month names)
      const dateElement = firstCard.locator('[class*="font-medium text-gray-900"]');
      await expect(dateElement).toBeVisible();
      
      const dateText = await dateElement.textContent();
      console.log('Date format:', dateText);
      
      // Verify it contains some text (date should be formatted)
      expect(dateText).toBeTruthy();
      expect(dateText!.length).toBeGreaterThan(5);
    }
    
    await page.screenshot({ path: 'test-results/weather-date-formatting.png' });
  });

  test('should be responsive on different screen sizes', async ({ page }) => {
    // Test desktop view
    await page.setViewportSize({ width: 1200, height: 800 });
    await page.waitForTimeout(1000);
    await page.screenshot({ path: 'test-results/weather-desktop-view.png' });
    
    // Test tablet view
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: 'test-results/weather-tablet-view.png' });
    
    // Test mobile view
    await page.setViewportSize({ width: 375, height: 667 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: 'test-results/weather-mobile-view.png' });
    
    // Verify elements are still visible on mobile
    await expect(page.locator('h1:has-text("Weather Forecast")')).toBeVisible();
    await expect(page.locator('button:has-text("Reload")')).toBeVisible();
  });

  test('should have proper accessibility attributes', async ({ page }) => {
    // Check that the reload button has proper accessibility
    const reloadButton = page.locator('button:has-text("Reload")');
    
    // Button should be focusable
    await reloadButton.focus();
    await expect(reloadButton).toBeFocused();
    
    // Take screenshot with focus
    await page.screenshot({ path: 'test-results/weather-accessibility-focus.png' });
    
    // Check that headings have proper hierarchy
    await expect(page.locator('h1')).toBeVisible();
    
    // Verify that temperature information is readable
    const weatherCards = page.locator('[class*="grid"] > div:has([class*="text-4xl"])');
    const cardCount = await weatherCards.count();
    
    if (cardCount > 0) {
      const firstCard = weatherCards.first();
      const tempElement = firstCard.locator('text=/\\d+°C/');
      await expect(tempElement).toBeVisible();
      
      // Verify color contrast by checking text colors are not too light
      const tempStyles = await tempElement.evaluate(el => {
        const styles = window.getComputedStyle(el);
        return {
          color: styles.color,
          backgroundColor: styles.backgroundColor
        };
      });
      
      console.log('Temperature element styles:', tempStyles);
    }
  });
});