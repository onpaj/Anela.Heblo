import { test, expect } from '@playwright/test';

test.describe('Transport Box Assign Number and Transition', () => {
  const TRANSPORT_BOX_LIST_URL = 'http://localhost:3001/logistics/transport-boxes';
  
  test('assign box number B999 and test transitions', async ({ page }) => {
    // Enable console logging
    page.on('console', msg => {
      if (!msg.text().includes('React DevTools') && !msg.text().includes('i18next')) {
        console.log('BROWSER:', msg.text());
      }
    });
    
    // Navigate to transport boxes page
    await page.goto(TRANSPORT_BOX_LIST_URL);
    await page.waitForLoadState('networkidle');
    
    console.log('Looking for create button...');
    
    // Click on "Create New Box" button
    const createButton = page.locator('button').filter({ hasText: /Otevřít nový box|Vytvořit|Create/i });
    if (await createButton.count() > 0) {
      console.log('Clicking create button...');
      await createButton.first().click();
      await page.waitForTimeout(2000);
    } else {
      console.log('No create button, checking if modal is already open or clicking first row...');
      // Try clicking on the first row if exists
      const firstRow = page.locator('table tbody tr').first();
      if (await firstRow.count() > 0) {
        await firstRow.click();
        await page.waitForTimeout(2000);
      }
    }
    
    // Check if modal is open
    const modal = page.locator('.fixed.inset-0');
    await expect(modal).toBeVisible({ timeout: 5000 });
    console.log('Modal is open');
    
    // Take initial screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-B999-1-initial-modal.png',
      fullPage: false
    });
    
    // Check current state
    const currentStateElement = page.locator('span').filter({ hasText: /^(Nový|Otevřený|V přepravě|Přijatý)$/ }).first();
    const currentState = await currentStateElement.textContent();
    console.log(`Current state: ${currentState}`);
    
    if (currentState === 'Nový') {
      console.log('Box is in New state, assigning box number B999...');
      
      // Find box number input
      const boxNumberInput = page.locator('#boxNumberInput');
      await expect(boxNumberInput).toBeVisible();
      
      // Fill in B999
      await boxNumberInput.fill('B999');
      console.log('Filled box number: B999');
      
      // Take screenshot before assign
      await page.screenshot({ 
        path: 'test-results/transport-box-B999-2-before-assign.png',
        fullPage: false
      });
      
      // Click assign button
      const assignButton = page.locator('button').filter({ hasText: 'Přiřadit' });
      await expect(assignButton).toBeVisible();
      await assignButton.click();
      console.log('Clicked assign button');
      
      // Wait for assignment to complete
      await page.waitForTimeout(3000);
      
      // Take screenshot after assign
      await page.screenshot({ 
        path: 'test-results/transport-box-B999-3-after-assign.png',
        fullPage: false
      });
      
      // Check if box number was assigned
      const boxNumberDisplay = page.locator('div').filter({ hasText: /B999/ });
      if (await boxNumberDisplay.count() > 0) {
        console.log('✅ Box number B999 assigned successfully');
        
        // Check if state automatically changed to Otevřený
        const openedStateElement = page.locator('span').filter({ hasText: /^Otevřený$/ });
        if (await openedStateElement.count() > 0) {
          console.log('✅ Box automatically transitioned to Otevřený state after assigning number');
        }
      } else {
        console.log('⚠️ Box number might not be assigned');
      }
    }
    
    // Now test state transitions
    console.log('Testing state transitions...');
    
    // Get current state after assignment
    const currentStateAfterAssign = await page.locator('span').filter({ hasText: /^(Nový|Otevřený|V přepravě|Přijatý)$/ }).first().textContent();
    console.log(`Current state after assignment: ${currentStateAfterAssign}`);
    
    // Look for state transition buttons in the footer
    const footer = page.locator('div').filter({ hasText: /Zavřít/ }).last().locator('..');
    
    // If current state is already Otevřený, try to transition to V přepravě
    if (currentStateAfterAssign === 'Otevřený') {
      console.log('Box is already in Otevřený state, looking for V přepravě button...');
      
      const transitButton = footer.locator('button').filter({ hasText: /^V přepravě$/ });
      
      if (await transitButton.count() > 0 && !(await transitButton.isDisabled())) {
        console.log('Found enabled "V přepravě" button, clicking...');
        
        await page.screenshot({ 
          path: 'test-results/transport-box-B999-4-before-transit.png',
          fullPage: false
        });
        
        await transitButton.click();
        await page.waitForTimeout(3000);
        
        await page.screenshot({ 
          path: 'test-results/transport-box-B999-5-after-transit.png',
          fullPage: false
        });
        
        // Check new state
        const transitStateElement = page.locator('span').filter({ hasText: 'V přepravě' });
        if (await transitStateElement.count() > 0) {
          console.log('✅ Successfully transitioned to V přepravě state');
          
          // Now try to transition to "Přijatý"
          const receivedButton = footer.locator('button').filter({ hasText: /^Přijatý$/ });
          
          if (await receivedButton.count() > 0 && !(await receivedButton.isDisabled())) {
            console.log('Found enabled "Přijatý" button, clicking...');
            
            await receivedButton.click();
            await page.waitForTimeout(3000);
            
            await page.screenshot({ 
              path: 'test-results/transport-box-B999-6-after-received.png',
              fullPage: false
            });
            
            const receivedStateElement = page.locator('span').filter({ hasText: 'Přijatý' });
            if (await receivedStateElement.count() > 0) {
              console.log('✅ Successfully transitioned to Přijatý state');
            } else {
              console.log('⚠️ Failed to transition to Přijatý state');
            }
          } else {
            console.log('⚠️ "Přijatý" button not found or disabled');
          }
        } else {
          console.log('⚠️ Failed to transition to V přepravě state');
        }
      } else {
        console.log('⚠️ "V přepravě" button not found or disabled');
      }
    } else if (currentStateAfterAssign === 'Nový') {
      // If still in New state, try to click Otevřený button
      console.log('Box is still in Nový state, looking for Otevřený button...');
      
      const openButton = footer.locator('button').filter({ hasText: /^Otevřený$/ });
      
      if (await openButton.count() > 0 && !(await openButton.isDisabled())) {
        console.log('Found enabled "Otevřený" button, clicking...');
        
        await openButton.click();
        await page.waitForTimeout(3000);
        
        const openedStateElement = page.locator('span').filter({ hasText: 'Otevřený' });
        if (await openedStateElement.count() > 0) {
          console.log('✅ Successfully transitioned to Otevřený state');
        }
      } else {
        console.log('⚠️ "Otevřený" button not found or disabled');
      
        // Check why it might be disabled
        const hasBoxNumber = await page.locator('div').filter({ hasText: /B\d{3}/ }).count() > 0;
        console.log(`Box has number assigned: ${hasBoxNumber}`);
      }
    }
    
    // Test reverse transition
    const prevButton = footer.locator('button').filter({ has: page.locator('[data-lucide="arrow-left"]') });
    if (await prevButton.count() > 0 && !(await prevButton.isDisabled())) {
      console.log('Testing reverse transition...');
      await prevButton.click();
      await page.waitForTimeout(3000);
      
      await page.screenshot({ 
        path: 'test-results/transport-box-B999-7-after-reverse.png',
        fullPage: false
      });
      
      console.log('✅ Reverse transition executed');
    }
    
    // Final screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-B999-8-final.png',
      fullPage: false
    });
    
    console.log('Test completed');
  });
});