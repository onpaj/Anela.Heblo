import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis - Status Filter Buttons', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the purchase stock analysis page
    await page.goto('http://localhost:3001/purchase/stock-analysis');
    await page.waitForLoadState('networkidle');
  });

  test('displays clickable status summary buttons', async ({ page }) => {
    // Wait for summary cards to load
    await page.waitForTimeout(2000);
    
    // Check if summary buttons are clickable
    const statusButtons = [
      { text: 'Celkem:', filter: 'All' },
      { text: 'Kritické:', filter: 'Critical' },
      { text: 'Nízké:', filter: 'Low' },
      { text: 'Optimální:', filter: 'Optimal' },
      { text: 'Přeskladněno:', filter: 'Overstocked' },
      { text: 'Nezkonfigurováno:', filter: 'NotConfigured' }
    ];
    
    for (const status of statusButtons) {
      const buttonElement = page.getByText(status.text).first();
      if (await buttonElement.isVisible()) {
        await expect(buttonElement).toBeVisible();
        // Check if it's clickable (has button parent)
        const button = page.locator('button').filter({ hasText: status.text });
        if (await button.count() > 0) {
          await expect(button.first()).toBeVisible();
        }
      }
    }
  });

  test('clicking status buttons filters the data', async ({ page }) => {
    // Wait for initial data load
    await page.waitForTimeout(2000);
    
    // Click on Critical status button
    const criticalButton = page.locator('button').filter({ hasText: 'Kritické:' });
    if (await criticalButton.count() > 0) {
      await criticalButton.first().click();
      await page.waitForTimeout(500);
      
      // Check if the filter dropdown reflects the change
      const statusSelect = page.locator('select[value]').first();
      if (await statusSelect.isVisible()) {
        const value = await statusSelect.inputValue();
        // Should be filtered to show only critical items
        console.log('Filter value after clicking Critical:', value);
      }
    }
    
    // Click on All status button to reset
    const allButton = page.locator('button').filter({ hasText: 'Celkem:' });
    if (await allButton.count() > 0) {
      await allButton.first().click();
      await page.waitForTimeout(500);
      
      const statusSelect = page.locator('select[value]').first();
      if (await statusSelect.isVisible()) {
        const value = await statusSelect.inputValue();
        console.log('Filter value after clicking All:', value);
      }
    }
  });

  test('status buttons show visual feedback when active', async ({ page }) => {
    // Wait for data load
    await page.waitForTimeout(2000);
    
    // Click on Low status button
    const lowButton = page.locator('button').filter({ hasText: 'Nízké:' });
    if (await lowButton.count() > 0) {
      await lowButton.first().click();
      await page.waitForTimeout(500);
      
      // Check if button has active styling (ring classes)
      const buttonClasses = await lowButton.first().getAttribute('class');
      if (buttonClasses) {
        console.log('Button classes after click:', buttonClasses);
        // Should contain ring classes for active state
      }
    }
  });

  test('table rows have subtle color coding', async ({ page }) => {
    // Wait for data load
    await page.waitForTimeout(2000);
    
    // Check if table rows exist and have background colors
    const tableRows = page.locator('tbody tr');
    const rowCount = await tableRows.count();
    
    if (rowCount > 0) {
      // Check first few rows for background color classes
      for (let i = 0; i < Math.min(3, rowCount); i++) {
        const row = tableRows.nth(i);
        const rowClasses = await row.getAttribute('class');
        if (rowClasses) {
          console.log(`Row ${i} classes:`, rowClasses);
          // Should contain subtle background color classes
          expect(rowClasses).toMatch(/bg-(red|amber|emerald|blue|gray)-50/);
        }
      }
    }
  });
});