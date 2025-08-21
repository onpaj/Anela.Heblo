import { test, expect } from '@playwright/test';

test.describe('Layout E2E Tests', () => {
  test('should display correct layout with sidebar and main content', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // Check sidebar is present
    await expect(page.getByText('Anela Heblo')).toBeVisible();
    
    // Check navigation items are present (use more specific selector)
    await expect(page.getByRole('link', { name: 'Dashboard' })).toBeVisible();
    await expect(page.getByText('Produkty')).toBeVisible();
    
    // Check main content area is present
    await expect(page.locator('main, [role="main"]')).toBeVisible();
    
    // Check user profile area
    await expect(page.getByText('Mock User')).toBeVisible();
  });

  test('should navigate between main sections', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    
    // Navigate to catalog
    await page.click('text=Katalog');
    await expect(page.locator('h1')).toContainText('Seznam produktů');
    
    // Navigate back to dashboard
    await page.click('text=Dashboard');
    await expect(page.getByText('Administrační dashboard')).toBeVisible();
  });
});