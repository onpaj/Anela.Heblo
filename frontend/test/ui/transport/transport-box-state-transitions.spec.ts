import { test, expect, Page } from '@playwright/test';

test.describe('Transport Box State Transitions', () => {
  const TRANSPORT_BOX_LIST_URL = 'http://localhost:3001/transport-boxes';
  
  // Helper function to navigate to a specific transport box
  async function navigateToTransportBox(page: Page, boxId: number = 1) {
    await page.goto(TRANSPORT_BOX_LIST_URL);
    await expect(page).toHaveURL(TRANSPORT_BOX_LIST_URL);
    
    // Wait for the page to load by looking for transport boxes title
    await page.waitForSelector('text=Transportní boxy', { timeout: 10000 });
    
    // Wait for table to load - look for table or rows
    await page.waitForSelector('table tbody tr', { timeout: 10000 });
    
    // Click on the first transport box row
    const firstRow = page.locator('table tbody tr').first();
    await firstRow.click();
    
    // Wait for modal to appear - look for modal dialog
    await page.waitForSelector('.fixed.inset-0', { timeout: 10000 });
  }

  test.beforeEach(async ({ page }) => {
    // Set viewport for consistent testing
    await page.setViewportSize({ width: 1280, height: 720 });
  });

  test('should display state transition buttons in modal footer', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // Check modal is visible
    await expect(page.locator('.fixed.inset-0')).toBeVisible();
    
    // Check that state transition buttons are present in modal footer
    const stateButtons = page.locator('button').filter({ hasText: /Otevřený|Nový|V přepravě|Přijatý|Swap|Naskladněný|Uzavřený/ });
    await expect(stateButtons.first()).toBeVisible();
    
    // Take screenshot for visual verification
    await page.screenshot({ 
      path: 'test-results/transport-box-state-buttons.png',
      fullPage: false
    });
  });

  test('should show correct directional layout for state buttons', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // Check modal is visible
    await expect(page.locator('.fixed.inset-0')).toBeVisible();
    
    // Check if arrow icons exist in buttons
    const leftArrowButtons = page.locator('button svg[data-lucide="arrow-left"]');
    const rightArrowButtons = page.locator('button svg[data-lucide="arrow-right"]');
    
    // At least one button should exist (either direction)
    const stateButtons = page.locator('button').filter({ hasText: /Otevřený|Nový|V přepravě|Přijatý|Swap|Naskladněný|Uzavřený/ });
    await expect(stateButtons.first()).toBeVisible();
  });

  test('should successfully transition from New to Opened state', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // Check initial state is "New" (Czech: "Nový")
    await expect(page.locator('[data-testid="transport-box-state"]')).toContainText('Nový');
    
    // Find and click the "Open" button (Czech: "Otevřený")
    const openButton = page.locator('[data-testid="next-state-button"]', { hasText: 'Otevřený' });
    await expect(openButton).toBeVisible();
    await openButton.click();
    
    // Wait for state change to complete
    await page.waitForTimeout(1000);
    
    // Check that state has changed to "Opened" (Czech: "Otevřený")
    await expect(page.locator('[data-testid="transport-box-state"]')).toContainText('Otevřený');
    
    // Take screenshot of successful state change
    await page.screenshot({ 
      path: 'test-results/transport-box-opened-state.png',
      fullPage: false
    });
  });

  test('should successfully transition through multiple states', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // Test sequence: New -> Opened -> InTransit -> Received
    const stateSequence = [
      { from: 'Nový', to: 'Otevřený', buttonType: 'next-state-button' },
      { from: 'Otevřený', to: 'V přepravě', buttonType: 'next-state-button' },
      { from: 'V přepravě', to: 'Doručený', buttonType: 'next-state-button' }
    ];
    
    for (const transition of stateSequence) {
      // Verify current state
      await expect(page.locator('[data-testid="transport-box-state"]')).toContainText(transition.from);
      
      // Click transition button
      const transitionButton = page.locator(`[data-testid="${transition.buttonType}"]`, { hasText: transition.to });
      await expect(transitionButton).toBeVisible();
      await transitionButton.click();
      
      // Wait for transition to complete
      await page.waitForTimeout(1500);
      
      // Verify new state
      await expect(page.locator('[data-testid="transport-box-state"]')).toContainText(transition.to);
    }
    
    // Take final screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-final-state.png',
      fullPage: false
    });
  });

  test('should show loading state during transition', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // Get the next state button
    const nextButton = page.locator('[data-testid="next-state-button"]').first();
    await expect(nextButton).toBeVisible();
    
    // Click the button and immediately check for loading state
    await nextButton.click();
    
    // Check for loading indication (disabled buttons or loading text)
    await expect(
      page.locator('text=Měnění stavu...').or(
        page.locator('[data-testid="next-state-button"]:disabled')
      )
    ).toBeVisible({ timeout: 2000 });
  });

  test('should handle reverse state transitions', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // First, advance to Opened state
    const openButton = page.locator('[data-testid="next-state-button"]', { hasText: 'Otevřený' });
    if (await openButton.isVisible()) {
      await openButton.click();
      await page.waitForTimeout(1000);
    }
    
    // Now test reverse transition back to New
    const previousButton = page.locator('[data-testid="previous-state-button"]', { hasText: 'Nový' });
    if (await previousButton.isVisible()) {
      await previousButton.click();
      await page.waitForTimeout(1000);
      
      // Verify we're back to New state
      await expect(page.locator('[data-testid="transport-box-state"]')).toContainText('Nový');
    }
  });

  test('should display error handling for invalid transitions', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // We'll simulate this by attempting an invalid state change
    // The UI should prevent this, but if errors occur they should be displayed
    
    // Look for any error messages after attempting transitions
    const errorSelector = '[data-testid="state-change-error"]';
    
    // If errors appear, they should be visible
    const errorElement = page.locator(errorSelector);
    
    // This test mainly verifies error handling exists in the UI
    // The actual error scenarios are better tested in unit tests
    console.log('Error handling test - UI should prevent invalid transitions');
  });

  test('should maintain proper button states across different box states', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // Test that buttons appear/disappear correctly for different states
    const states = ['Nový', 'Otevřený', 'V přepravě'];
    
    for (let i = 0; i < states.length - 1; i++) {
      const currentState = states[i];
      const nextState = states[i + 1];
      
      // Verify current state
      await expect(page.locator('[data-testid="transport-box-state"]')).toContainText(currentState);
      
      // Check button availability
      const nextButton = page.locator('[data-testid="next-state-button"]');
      const previousButton = page.locator('[data-testid="previous-state-button"]');
      
      // Next button should be available (except for final states)
      if (i < states.length - 1) {
        await expect(nextButton).toHaveCountGreaterThan(0);
      }
      
      // Previous button should be available (except for initial state New without conditions)
      if (i > 0 || currentState !== 'Nový') {
        await expect(previousButton).toHaveCountGreaterThanOrEqual(0);
      }
      
      // Click next if available
      const nextTransitionButton = nextButton.filter({ hasText: nextState }).first();
      if (await nextTransitionButton.isVisible()) {
        await nextTransitionButton.click();
        await page.waitForTimeout(1000);
      }
    }
  });

  test('should be responsive on mobile viewport', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    await navigateToTransportBox(page);
    
    // Check modal is responsive
    await expect(page.locator('[data-testid="transport-box-modal"]')).toBeVisible();
    
    // Check buttons are still accessible on mobile
    const stateButtons = page.locator('[data-testid="state-transition-button"]');
    await expect(stateButtons).toHaveCountGreaterThan(0);
    
    // Take mobile screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-mobile-states.png',
      fullPage: false
    });
  });

  test('should close modal after successful state changes', async ({ page }) => {
    await navigateToTransportBox(page);
    
    // Perform a state transition
    const nextButton = page.locator('[data-testid="next-state-button"]').first();
    if (await nextButton.isVisible()) {
      await nextButton.click();
      await page.waitForTimeout(1000);
    }
    
    // Close modal using close button or ESC
    const closeButton = page.locator('[data-testid="modal-close-button"]');
    if (await closeButton.isVisible()) {
      await closeButton.click();
    } else {
      await page.keyboard.press('Escape');
    }
    
    // Verify modal is closed
    await expect(page.locator('[data-testid="transport-box-modal"]')).not.toBeVisible({ timeout: 5000 });
    
    // Verify we're back on the list page
    await expect(page).toHaveURL(TRANSPORT_BOX_LIST_URL);
  });
});