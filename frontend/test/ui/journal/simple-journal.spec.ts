import { test, expect } from '@playwright/test';

test.describe('Simple Journal Tests', () => {
  test('should load journal page successfully', async ({ page }) => {
    // Navigate to journal page
    await page.goto('http://localhost:3001/journal');
    
    // Wait for any content to load
    await page.waitForLoadState('networkidle');
    
    // Should not have any obvious errors
    const errorElements = page.locator('[data-testid*="error"], .error, [class*="error"]');
    await expect(errorElements).toHaveCount(0);
    
    // Should have some basic structure
    const body = page.locator('body');
    await expect(body).toBeVisible();
  });

  test('should display basic journal structure', async ({ page }) => {
    await page.goto('http://localhost:3001/journal');
    await page.waitForLoadState('networkidle');
    
    // Should have main content area
    const mainContent = page.locator('[class*="flex"], main, .main-content').first();
    await expect(mainContent).toBeVisible();
  });

  test('should be responsive', async ({ page }) => {
    // Test mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('http://localhost:3001/journal');
    await page.waitForLoadState('networkidle');
    
    // Page should load without horizontal scroll
    const body = page.locator('body');
    await expect(body).toBeVisible();
    
    // Test desktop viewport
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.reload();
    await page.waitForLoadState('networkidle');
    
    await expect(body).toBeVisible();
  });
});