import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis E2E', () => {
  test('should load stock analysis page and display basic functionality', async ({ page }) => {
    // Navigate to the Purchase Stock Analysis page
    await page.goto('/purchase/stock-analysis');
    await page.waitForLoadState('networkidle');

    // Check page loaded successfully (be flexible with text)
    const pageText = await page.textContent('body');
    expect(pageText).toContain('skladových zásob' || 'Stock Analysis' || 'Analýza');
    
    // Check basic controls are present
    await expect(page.getByRole('button', { name: 'Obnovit' })).toBeVisible();
    
    // Check some data is displayed (either table or empty state)
    const table = page.locator('table');
    const emptyState = page.getByText('Žádné záznamy nenalezeny');
    
    // Either table or empty state should be visible
    const hasTable = await table.isVisible();
    const hasEmptyState = await emptyState.isVisible();
    
    expect(hasTable || hasEmptyState).toBe(true);
  });
});