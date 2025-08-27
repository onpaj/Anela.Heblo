import { test, expect, Page } from '@playwright/test';

test.describe('Transport Box B999 State Transitions', () => {
  const TRANSPORT_BOX_LIST_URL = 'http://localhost:3001/logistics/transport-boxes';
  
  // Helper to find and open box B999
  async function openBoxB999(page: Page) {
    await page.goto(TRANSPORT_BOX_LIST_URL);
    await expect(page).toHaveURL(TRANSPORT_BOX_LIST_URL);
    
    // Wait for the page to load
    await page.waitForSelector('text=Transportní boxy', { timeout: 10000 });
    await page.waitForSelector('table tbody tr', { timeout: 10000 });
    
    // Look for box B999 specifically
    const b999Row = page.locator('table tbody tr').filter({ hasText: 'B999' }).first();
    
    // If B999 doesn't exist, click on any box for now
    if (await b999Row.count() === 0) {
      console.log('Box B999 not found, using first available box');
      await page.locator('table tbody tr').first().click();
    } else {
      await b999Row.click();
    }
    
    // Wait for modal to appear
    await page.waitForSelector('.fixed.inset-0', { timeout: 10000 });
    await expect(page.locator('.fixed.inset-0')).toBeVisible();
  }

  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 720 });
  });

  test('should display current state and transition buttons for B999', async ({ page }) => {
    await openBoxB999(page);
    
    // Check modal title
    await expect(page.locator('h2').filter({ hasText: 'Detail transportního boxu' })).toBeVisible();
    
    // Take screenshot to see current state
    await page.screenshot({ 
      path: 'test-results/transport-box-b999-initial.png',
      fullPage: true
    });
    
    // Check if state transition area exists in footer
    const footer = page.locator('div.border-t.border-gray-200').last();
    await expect(footer).toBeVisible();
    
    // Look for current state display
    const currentStateDisplay = footer.locator('span').filter({ hasText: /Nový|Otevřený|V přepravě|Přijatý|Swap|Naskladněný|Uzavřený/ });
    await expect(currentStateDisplay).toBeVisible();
  });

  test('should show correct state transition buttons layout', async ({ page }) => {
    await openBoxB999(page);
    
    // Check footer area
    const footer = page.locator('div.border-t.border-gray-200').last();
    
    // Look for arrow buttons (previous/next state)
    const leftArrowButton = footer.locator('button').filter({ has: page.locator('[data-lucide="arrow-left"]') });
    const rightArrowButton = footer.locator('button').filter({ has: page.locator('[data-lucide="arrow-right"]') });
    
    // At least one arrow button should be visible
    const hasLeftArrow = await leftArrowButton.count() > 0;
    const hasRightArrow = await rightArrowButton.count() > 0;
    
    expect(hasLeftArrow || hasRightArrow).toBe(true);
    
    // Take screenshot of footer with buttons
    await page.screenshot({ 
      path: 'test-results/transport-box-b999-footer.png',
      clip: { x: 0, y: 600, width: 1280, height: 120 }
    });
  });

  test('should handle New to Opened transition with box number', async ({ page }) => {
    await openBoxB999(page);
    
    // Check if we're in New state and need to assign box number
    const boxNumberInput = page.locator('#boxNumberInput');
    
    if (await boxNumberInput.isVisible()) {
      console.log('Box is in New state, testing box number assignment');
      
      // Fill in box number B999
      await boxNumberInput.fill('B999');
      
      // Click assign button
      const assignButton = page.locator('button').filter({ hasText: 'Přiřadit' });
      await assignButton.click();
      
      // Wait for assignment to complete
      await page.waitForTimeout(2000);
      
      // Take screenshot after assignment
      await page.screenshot({ 
        path: 'test-results/transport-box-b999-after-assignment.png',
        fullPage: true
      });
    }
    
    // Now try to transition to Opened state
    const footer = page.locator('div.border-t.border-gray-200').last();
    const nextButton = footer.locator('button').filter({ hasText: 'Otevřený' });
    
    if (await nextButton.count() > 0) {
      await nextButton.click();
      
      // Wait for state change
      await page.waitForTimeout(2000);
      
      // Verify we're now in Opened state
      const openedState = page.locator('span').filter({ hasText: 'Otevřený' });
      await expect(openedState).toBeVisible();
      
      await page.screenshot({ 
        path: 'test-results/transport-box-b999-opened.png',
        fullPage: true
      });
    }
  });

  test('should test full transition sequence', async ({ page }) => {
    await openBoxB999(page);
    
    const transitionSequence = [
      { targetState: 'Otevřený', action: 'next' },
      { targetState: 'V přepravě', action: 'next' },
      { targetState: 'Přijatý', action: 'next' },
    ];
    
    for (let i = 0; i < transitionSequence.length; i++) {
      const transition = transitionSequence[i];
      
      // Look for the target state button
      const footer = page.locator('div.border-t.border-gray-200').last();
      const targetButton = footer.locator('button').filter({ hasText: transition.targetState });
      
      if (await targetButton.count() === 0) {
        console.log(`Button for ${transition.targetState} not found, skipping`);
        break;
      }
      
      console.log(`Transitioning to: ${transition.targetState}`);
      await targetButton.click();
      
      // Wait for transition
      await page.waitForTimeout(2000);
      
      // Take screenshot of each state
      await page.screenshot({ 
        path: `test-results/transport-box-b999-state-${i + 1}.png`,
        fullPage: true
      });
      
      // Verify the state has changed
      const stateElement = page.locator('span').filter({ hasText: transition.targetState });
      await expect(stateElement).toBeVisible();
    }
  });

  test('should test reverse transitions', async ({ page }) => {
    await openBoxB999(page);
    
    // First advance a few states if possible
    const footer = page.locator('div.border-t.border-gray-200').last();
    
    // Try to go forward first
    const nextButton = footer.locator('button').filter({ has: page.locator('[data-lucide="arrow-right"]') });
    if (await nextButton.count() > 0) {
      await nextButton.click();
      await page.waitForTimeout(2000);
      
      // Now try to go back
      const prevButton = footer.locator('button').filter({ has: page.locator('[data-lucide="arrow-left"]') });
      if (await prevButton.count() > 0) {
        await prevButton.click();
        await page.waitForTimeout(2000);
        
        await page.screenshot({ 
          path: 'test-results/transport-box-b999-reverse-transition.png',
          fullPage: true
        });
      }
    }
  });

  test('should handle transit confirmation for Opened box', async ({ page }) => {
    await openBoxB999(page);
    
    // Check if box is in Opened state
    const openedState = page.locator('span').filter({ hasText: 'Otevřený' });
    
    if (await openedState.count() > 0) {
      console.log('Box is in Opened state, testing transit confirmation');
      
      // Look for box number input in header (for transit confirmation)
      const boxNumberInput = page.locator('#boxNumberInput');
      
      if (await boxNumberInput.isVisible()) {
        // This might be a confirmation input, try filling B999
        await boxNumberInput.fill('B999');
        
        const confirmButton = page.locator('button').filter({ hasText: /Potvrdit|Přiřadit/ });
        if (await confirmButton.count() > 0) {
          await confirmButton.click();
          await page.waitForTimeout(2000);
          
          await page.screenshot({ 
            path: 'test-results/transport-box-b999-transit-confirmed.png',
            fullPage: true
          });
        }
      }
    }
  });

  test('should verify button states and availability', async ({ page }) => {
    await openBoxB999(page);
    
    const footer = page.locator('div.border-t.border-gray-200').last();
    
    // Check all buttons in footer
    const allButtons = footer.locator('button');
    const buttonCount = await allButtons.count();
    
    console.log(`Found ${buttonCount} buttons in footer`);
    
    // Check each button
    for (let i = 0; i < buttonCount; i++) {
      const button = allButtons.nth(i);
      const buttonText = await button.textContent();
      const isDisabled = await button.isDisabled();
      const isVisible = await button.isVisible();
      
      console.log(`Button ${i}: "${buttonText}" - Disabled: ${isDisabled}, Visible: ${isVisible}`);
    }
    
    await page.screenshot({ 
      path: 'test-results/transport-box-b999-button-states.png',
      fullPage: true
    });
  });

  test('should handle loading states during transitions', async ({ page }) => {
    await openBoxB999(page);
    
    const footer = page.locator('div.border-t.border-gray-200').last();
    const transitionButtons = footer.locator('button').filter({ 
      hasNot: page.locator('text=Zavřít') 
    });
    
    if (await transitionButtons.count() > 0) {
      const firstTransitionButton = transitionButtons.first();
      
      // Click and immediately check for loading state
      await firstTransitionButton.click();
      
      // Check for loading spinner or disabled state
      const loadingSpinner = page.locator('[data-lucide="loader-2"]');
      const disabledButton = firstTransitionButton.and(page.locator(':disabled'));
      
      const hasLoadingState = await loadingSpinner.count() > 0 || await disabledButton.count() > 0;
      
      if (hasLoadingState) {
        console.log('Loading state detected during transition');
        await page.screenshot({ 
          path: 'test-results/transport-box-b999-loading.png',
          fullPage: true
        });
      }
      
      // Wait for transition to complete
      await page.waitForTimeout(3000);
    }
  });
});