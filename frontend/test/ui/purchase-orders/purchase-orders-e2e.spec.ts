import { test, expect } from '@playwright/test';

test.describe('Purchase Orders E2E Workflow', () => {
  test('should load purchase orders page', async ({ page }) => {
    await page.goto('/purchase/orders');
    await page.waitForLoadState('networkidle');

    // Check page loaded (be flexible)
    const bodyText = await page.textContent('body');
    expect(bodyText).toContain('objednávky' || 'Purchase' || 'Orders');
    
    // Look for any button that might create orders
    const buttons = page.locator('button');
    expect(await buttons.count()).toBeGreaterThan(0);
    
    // Check either orders list or empty state
    const table = page.locator('table');
    const emptyState = page.getByText('Žádné objednávky nenalezeny');
    
    const hasTable = await table.isVisible();
    const hasEmptyState = await emptyState.isVisible();
    
    expect(hasTable || hasEmptyState).toBe(true);
  });
});