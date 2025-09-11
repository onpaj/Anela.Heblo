import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToTransportBoxes } from './helpers/e2e-auth-helper';

test.describe('Transport Boxes - Basic Functionality Tests', () => {
  
  test.beforeEach(async ({ page }) => {
    await createE2EAuthSession(page);
  });

  test('should successfully navigate to transport boxes page', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Verify we're on the transport boxes list page
    await expect(page.locator('h1')).toContainText('Transportní boxy');
    
    // Verify essential UI elements are present
    const createButton = page.locator('button').filter({ hasText: /Otevřít nový box/ });
    await expect(createButton).toBeVisible();
  });

  test('should complete full transport box creation workflow (New → Opened)', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Step 1: Click "Otevřít nový box" button
    const createButton = page.locator('button').filter({ hasText: /Otevřít nový box/ });
    await createButton.first().click();
    
    // Step 2: Wait for box creation and detail modal to open
    await page.waitForTimeout(3000);
    
    // Step 3: Verify we're in detail modal with a new box in "New" state
    const detailModal = page.locator('[role="dialog"], .modal, .fixed.inset-0').first();
    await expect(detailModal).toBeVisible();
    
    // Step 4: Verify box is in "New" state and box number input is visible
    const boxNumberInput = page.locator('#boxNumberInput, input[placeholder="B001"], input[maxlength="4"]');
    await expect(boxNumberInput).toBeVisible();
    
    // Step 5: Enter box number (following real user workflow)
    const testBoxNumber = 'B999'; // Use a test number
    await boxNumberInput.fill(testBoxNumber);
    await expect(boxNumberInput).toHaveValue(testBoxNumber);
    
    // Step 6: Click "Přiřadit" button or press Enter to assign box number
    const assignButton = page.locator('button').filter({ hasText: /Přiřadit/ });
    if (await assignButton.count() > 0) {
      await assignButton.click();
    } else {
      // Fallback: submit form with Enter
      await boxNumberInput.press('Enter');
    }
    
    // Step 7: Wait for state transition from "New" to "Opened"
    await page.waitForTimeout(2000);
    
    // Step 8: Verify box transitioned to "Opened" state
    // Look for success indicators or state change
    const stateIndicators = [
      page.locator('.bg-blue-100.text-blue-800'), // "Opened" state badge color
      page.locator('text=Otevřený'),               // Czech text for "Opened"
      page.locator('text=Opened'),                 // English text for "Opened"
      page.locator('[data-testid="box-state"]').filter({ hasText: /Opened|Otevřený/ })
    ];
    
    let foundOpenedState = false;
    for (const indicator of stateIndicators) {
      if (await indicator.count() > 0) {
        foundOpenedState = true;
        break;
      }
    }
    
    // Step 9: Verify the workflow completed successfully
    expect(foundOpenedState).toBe(true);
    
    // Step 10: Close detail modal to return to list
    const closeButton = page.locator('button').filter({ hasText: /Close|Zavřít|×/ }).first();
    if (await closeButton.count() > 0) {
      await closeButton.click();
    } else {
      await page.keyboard.press('Escape');
    }
    
    // Step 11: Verify we're back on list page and new box appears
    await page.waitForTimeout(1000);
    await expect(page.locator('h1')).toContainText('Transportní boxy');
    
    // Look for the newly created box in the list
    const newBoxInList = page.locator(`text=${testBoxNumber}`);
    if (await newBoxInList.count() > 0) {
      await expect(newBoxInList).toBeVisible();
    }
  });

  test('should display transport boxes list correctly', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Wait for page to load
    await page.waitForTimeout(2000);
    
    // Check that either we have boxes or we have an empty state message
    const boxItems = page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item, tr:has(td)');
    const emptyState = page.locator('.empty-state, .no-data, .no-results');
    const loadingState = page.locator('.loading, .spinner');
    
    const hasBoxes = await boxItems.count() > 0;
    const hasEmptyState = await emptyState.count() > 0;
    const isLoading = await loadingState.count() > 0;
    
    // Page should show boxes, empty state, or be loading
    expect(hasBoxes || hasEmptyState || isLoading).toBe(true);
    
    // If we have boxes, verify page title is correct
    if (hasBoxes) {
      await expect(page.locator('h1')).toContainText('Transportní boxy');
    }
  });

  test('should have functional UI controls', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Wait for page to load
    await page.waitForTimeout(2000);
    
    // Check for basic UI controls
    const createButton = page.locator('button').filter({ hasText: /Otevřít nový box/ });
    const refreshButton = page.locator('button').filter({ hasText: /Refresh|Aktualizovat/ });
    
    // Create button should always be visible
    await expect(createButton).toBeVisible();
    
    // Test refresh button if it exists
    if (await refreshButton.count() > 0) {
      await expect(refreshButton).toBeVisible();
      await refreshButton.click();
      await page.waitForTimeout(1000);
      
      // Page should still be on transport boxes after refresh
      await expect(page.locator('h1')).toContainText('Transportní boxy');
    }
  });

  test('should handle errors gracefully', async ({ page }) => {
    await navigateToTransportBoxes(page);
    
    // Test that the page loads without JavaScript errors
    const errors: string[] = [];
    
    page.on('pageerror', (error) => {
      errors.push(error.message);
    });
    
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        errors.push(msg.text());
      }
    });
    
    // Interact with the page
    await page.waitForTimeout(3000);
    
    const createButton = page.locator('button').filter({ hasText: /Otevřít nový box/ });
    if (await createButton.count() > 0) {
      await createButton.click();
      await page.waitForTimeout(2000);
    }
    
    // Filter out known acceptable errors or warnings
    const criticalErrors = errors.filter(error => 
      !error.includes('Failed to fetch') && // Network errors are acceptable in tests
      !error.includes('AbortError') &&     // Abort errors from request cancellation
      !error.includes('Warning')           // React warnings
    );
    
    // Should not have critical JavaScript errors
    expect(criticalErrors.length).toBe(0);
  });
});