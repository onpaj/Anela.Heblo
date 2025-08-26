import { test, expect } from '@playwright/test';

test.describe('Transport Box State Transitions Debug', () => {
  const TRANSPORT_BOX_LIST_URL = 'http://localhost:3001/logistics/transport-boxes';
  
  test('debug state transitions directly', async ({ page }) => {
    // Enable console logging
    page.on('console', msg => console.log('BROWSER:', msg.text()));
    
    // Navigate to transport boxes page
    await page.goto(TRANSPORT_BOX_LIST_URL);
    await page.waitForLoadState('networkidle');
    
    // Take initial screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-debug-1-initial.png',
      fullPage: true
    });
    
    // Check if we have any transport boxes at all
    const pageContent = await page.textContent('body');
    console.log('Page loaded, checking for transport boxes...');
    
    // Try multiple selectors for transport box rows
    const selectors = [
      'table tbody tr',
      '.transport-box-row',
      '[role="row"]',
      '.MuiDataGrid-row',
      'div[class*="row"]'
    ];
    
    let foundRow = false;
    for (const selector of selectors) {
      const count = await page.locator(selector).count();
      if (count > 0) {
        console.log(`Found ${count} rows using selector: ${selector}`);
        foundRow = true;
        
        // Click on first row
        const firstRow = page.locator(selector).first();
        await firstRow.click();
        break;
      }
    }
    
    if (!foundRow) {
      console.log('No transport box rows found, trying to create one...');
      
      // Look for "Create" or "Add" button
      const createButton = page.locator('button').filter({ 
        hasText: /Vytvořit|Přidat|Create|Add|Nový/i 
      });
      
      if (await createButton.count() > 0) {
        console.log('Found create button, clicking...');
        await createButton.first().click();
        await page.waitForTimeout(2000);
        
        await page.screenshot({ 
          path: 'test-results/transport-box-debug-2-after-create.png',
          fullPage: true
        });
      } else {
        console.log('No create button found either');
      }
    }
    
    // Wait a bit for modal to appear
    await page.waitForTimeout(2000);
    
    // Take screenshot after clicking
    await page.screenshot({ 
      path: 'test-results/transport-box-debug-3-after-click.png',
      fullPage: true
    });
    
    // Check if modal is open
    const modalSelectors = [
      '.fixed.inset-0',
      '[role="dialog"]',
      '.modal',
      '.dialog'
    ];
    
    let modalFound = false;
    for (const selector of modalSelectors) {
      const modalCount = await page.locator(selector).count();
      if (modalCount > 0) {
        console.log(`Modal found using selector: ${selector}`);
        modalFound = true;
        break;
      }
    }
    
    if (modalFound) {
      console.log('Modal is open, looking for state information...');
      
      // Look for current state display
      const stateElements = await page.locator('span, div').filter({ 
        hasText: /Nový|Otevřený|V přepravě|Přijatý|Swap|Naskladněný|Uzavřený|Reserve|Chyba/
      }).all();
      
      console.log(`Found ${stateElements.length} elements with state text`);
      
      for (let i = 0; i < Math.min(3, stateElements.length); i++) {
        const text = await stateElements[i].textContent();
        console.log(`State element ${i}: ${text}`);
      }
      
      // Look for buttons in footer
      const footer = page.locator('div').filter({ hasText: /Zavřít/ }).last().locator('..');
      await page.screenshot({ 
        path: 'test-results/transport-box-debug-4-modal-footer.png',
        clip: { x: 0, y: 400, width: 1280, height: 320 }
      });
      
      // Look for all buttons in footer
      const footerButtons = await footer.locator('button').all();
      console.log(`Found ${footerButtons.length} buttons in footer`);
      
      for (let i = 0; i < footerButtons.length; i++) {
        const buttonText = await footerButtons[i].textContent();
        const isDisabled = await footerButtons[i].isDisabled();
        console.log(`Button ${i}: "${buttonText}" - Disabled: ${isDisabled}`);
      }
      
      // Try to find state transition buttons specifically
      const transitionButtons = await page.locator('button').filter({ 
        hasText: /Otevřený|V přepravě|Přijatý|Swap|Naskladněný|Reserve/
      }).all();
      
      console.log(`Found ${transitionButtons.length} transition buttons`);
      
      if (transitionButtons.length > 0) {
        // Try clicking the first enabled transition button
        for (const button of transitionButtons) {
          const isDisabled = await button.isDisabled();
          const text = await button.textContent();
          
          if (!isDisabled) {
            console.log(`Clicking transition button: "${text}"`);
            
            // Take screenshot before click
            await page.screenshot({ 
              path: 'test-results/transport-box-debug-5-before-transition.png',
              fullPage: false
            });
            
            await button.click();
            
            // Wait for state change
            await page.waitForTimeout(3000);
            
            // Take screenshot after click
            await page.screenshot({ 
              path: 'test-results/transport-box-debug-6-after-transition.png',
              fullPage: false
            });
            
            console.log('State transition attempted, checking new state...');
            
            // Check new state
            const newStateElements = await page.locator('span, div').filter({ 
              hasText: /Nový|Otevřený|V přepravě|Přijatý|Swap|Naskladněný|Uzavřený|Reserve|Chyba/
            }).all();
            
            for (let i = 0; i < Math.min(3, newStateElements.length); i++) {
              const text = await newStateElements[i].textContent();
              console.log(`New state element ${i}: ${text}`);
            }
            
            break; // Only test one transition
          }
        }
      } else {
        console.log('No transition buttons found or all are disabled');
      }
    } else {
      console.log('No modal found after clicking');
    }
    
    // Final screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-debug-7-final.png',
      fullPage: true
    });
  });
});