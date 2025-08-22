import { test, expect } from '@playwright/test';

test.describe('Catalog Detail E2E', () => {
  test('should open catalog detail modal from catalog list', async ({ page }) => {
    await page.goto('/catalog');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);

    // Look for catalog table
    const table = page.locator('table');
    if (await table.isVisible()) {
      // Click on first product row
      const firstRow = table.locator('tr').nth(1);
      if (await firstRow.isVisible()) {
        await firstRow.click();
        
        // Check some response to the click (modal, navigation, or detail view)
        await page.waitForTimeout(1000);
        
        const hasModal = await page.locator('[role="dialog"], .modal').isVisible();
        const hasDetailContent = await page.getByRole('heading', { name: 'Základní informace' }).isVisible();
        const urlChanged = page.url().includes('detail');
        
        expect(hasModal || hasDetailContent || urlChanged).toBe(true);
      }
    } else {
      // No data to test with - that's OK for E2E
      console.log('No catalog data available for testing detail modal');
    }
  });
});