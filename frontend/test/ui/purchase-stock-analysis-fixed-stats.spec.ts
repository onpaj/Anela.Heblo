import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis - Fixed Statistics', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3001/purchase/stock-analysis');
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000); // Wait for data load
  });

  test('statistics remain constant regardless of filter', async ({ page }) => {
    // First, capture initial statistics
    const initialStats = {};
    
    const statButtons = [
      { text: 'Celkem:', key: 'total' },
      { text: 'Kritické:', key: 'critical' },
      { text: 'Nízké:', key: 'low' },
      { text: 'Optimální:', key: 'optimal' },
      { text: 'Přeskladněno:', key: 'overstocked' },
      { text: 'Nezkonfigurováno:', key: 'notConfigured' }
    ];

    // Capture initial numbers
    for (const stat of statButtons) {
      const button = page.locator('button').filter({ hasText: stat.text });
      if (await button.count() > 0) {
        const buttonText = await button.first().textContent() || '';
        // Extract number from text like "Kritické: 2"
        const match = buttonText.match(/(\d+)/);
        if (match) {
          initialStats[stat.key] = parseInt(match[1]);
          console.log(`Initial ${stat.key}:`, initialStats[stat.key]);
        }
      }
    }

    console.log('Initial stats:', initialStats);

    // Now click on "Critical" filter
    const criticalButton = page.locator('button').filter({ hasText: 'Kritické:' });
    if (await criticalButton.count() > 0) {
      await criticalButton.first().click();
      await page.waitForTimeout(1000); // Wait for filter to apply
      
      // Check that statistics haven't changed
      for (const stat of statButtons) {
        const button = page.locator('button').filter({ hasText: stat.text });
        if (await button.count() > 0) {
          const buttonText = await button.first().textContent() || '';
          const match = buttonText.match(/(\d+)/);
          if (match) {
            const currentValue = parseInt(match[1]);
            console.log(`After Critical filter ${stat.key}:`, currentValue);
            
            if (initialStats[stat.key] !== undefined) {
              expect(currentValue).toBe(initialStats[stat.key]);
            }
          }
        }
      }
    }

    // Try another filter - "Low"
    const lowButton = page.locator('button').filter({ hasText: 'Nízké:' });
    if (await lowButton.count() > 0) {
      await lowButton.first().click();
      await page.waitForTimeout(1000);
      
      // Check statistics again
      for (const stat of statButtons) {
        const button = page.locator('button').filter({ hasText: stat.text });
        if (await button.count() > 0) {
          const buttonText = await button.first().textContent() || '';
          const match = buttonText.match(/(\d+)/);
          if (match) {
            const currentValue = parseInt(match[1]);
            console.log(`After Low filter ${stat.key}:`, currentValue);
            
            if (initialStats[stat.key] !== undefined) {
              expect(currentValue).toBe(initialStats[stat.key]);
            }
          }
        }
      }
    }

    // Reset to "All" and verify numbers are still the same
    const allButton = page.locator('button').filter({ hasText: 'Celkem:' });
    if (await allButton.count() > 0) {
      await allButton.first().click();
      await page.waitForTimeout(1000);
      
      for (const stat of statButtons) {
        const button = page.locator('button').filter({ hasText: stat.text });
        if (await button.count() > 0) {
          const buttonText = await button.first().textContent() || '';
          const match = buttonText.match(/(\d+)/);
          if (match) {
            const currentValue = parseInt(match[1]);
            console.log(`After All filter ${stat.key}:`, currentValue);
            
            if (initialStats[stat.key] !== undefined) {
              expect(currentValue).toBe(initialStats[stat.key]);
            }
          }
        }
      }
    }
  });

  test('table content changes but statistics stay fixed', async ({ page }) => {
    // Count initial table rows
    const initialRowCount = await page.locator('tbody tr').count();
    console.log('Initial table rows:', initialRowCount);

    // Capture critical count from button
    const criticalButton = page.locator('button').filter({ hasText: 'Kritické:' });
    let criticalCount = 0;
    if (await criticalButton.count() > 0) {
      const buttonText = await criticalButton.first().textContent() || '';
      const match = buttonText.match(/(\d+)/);
      if (match) {
        criticalCount = parseInt(match[1]);
        console.log('Critical count from button:', criticalCount);
      }
    }

    // Click critical filter
    await criticalButton.first().click();
    await page.waitForTimeout(1000);

    // Count filtered table rows - should be different from initial
    const filteredRowCount = await page.locator('tbody tr').count();
    console.log('Filtered table rows:', filteredRowCount);

    // Table rows should have changed (unless there are exactly as many critical items as total)
    // But critical count in button should remain the same
    const buttonTextAfter = await criticalButton.first().textContent() || '';
    const matchAfter = buttonTextAfter.match(/(\d+)/);
    if (matchAfter) {
      const criticalCountAfter = parseInt(matchAfter[1]);
      expect(criticalCountAfter).toBe(criticalCount);
    }
  });
});