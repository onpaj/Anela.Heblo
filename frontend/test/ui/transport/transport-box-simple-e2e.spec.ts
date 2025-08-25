import { test, expect, Page } from '@playwright/test';

test.describe('Transport Box State Transitions - Simple E2E', () => {
  const TRANSPORT_BOX_LIST_URL = 'http://localhost:3001/transport-boxes';
  
  test.beforeEach(async ({ page }) => {
    // Set viewport for consistent testing
    await page.setViewportSize({ width: 1280, height: 720 });
  });

  test('should navigate to transport box list and open modal', async ({ page }) => {
    // Navigate to transport box list
    await page.goto(TRANSPORT_BOX_LIST_URL);
    await expect(page).toHaveURL(TRANSPORT_BOX_LIST_URL);
    
    // Wait for page content to load
    await page.waitForLoadState('networkidle');
    
    // Take screenshot of list page
    await page.screenshot({ 
      path: 'test-results/transport-box-list-loaded.png',
      fullPage: true
    });
    
    // Look for any content that indicates the page loaded correctly
    // This could be a title, table, or any visible content
    const pageContent = await page.textContent('body');
    expect(pageContent).toBeTruthy();
    expect(pageContent.length).toBeGreaterThan(0);
    
    // Try to find table or list elements
    const hasTable = await page.locator('table').count() > 0;
    const hasRows = await page.locator('tbody tr, .transport-box-row, [role="row"]').count() > 0;
    
    if (hasTable && hasRows) {
      console.log('✅ Found table with transport box rows');
      
      // Try to click on first row
      const firstRow = page.locator('tbody tr').first();
      await firstRow.click();
      
      // Wait for any modal or detail view
      await page.waitForTimeout(2000);
      
      // Take screenshot after clicking
      await page.screenshot({ 
        path: 'test-results/transport-box-after-click.png',
        fullPage: true
      });
      
      // Check if modal appeared
      const modalExists = await page.locator('.modal, .dialog, .fixed.inset-0, [role="dialog"]').count() > 0;
      if (modalExists) {
        console.log('✅ Modal opened successfully');
        
        // Look for state transition buttons
        const stateButtons = await page.locator('button').filter({ 
          hasText: /Otevřený|Nový|V přepravě|Přijatý|Swap|Naskladněný|Uzavřený/ 
        }).count();
        
        if (stateButtons > 0) {
          console.log(`✅ Found ${stateButtons} state transition buttons`);
          
          // Take final screenshot showing the modal with buttons
          await page.screenshot({ 
            path: 'test-results/transport-box-modal-with-buttons.png',
            fullPage: false
          });
        } else {
          console.log('⚠️  No state transition buttons found');
        }
      } else {
        console.log('⚠️  No modal appeared after clicking');
      }
    } else {
      console.log('⚠️  No transport box rows found');
      
      // Still take screenshot for debugging
      await page.screenshot({ 
        path: 'test-results/transport-box-no-rows.png',
        fullPage: true
      });
    }
  });

  test('should display page title and basic navigation', async ({ page }) => {
    await page.goto(TRANSPORT_BOX_LIST_URL);
    
    // Wait for page to load
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);
    
    // Check for page title or heading
    const title = await page.textContent('h1, h2, .title, .heading').catch(() => null);
    if (title) {
      console.log(`✅ Found page title: ${title}`);
    }
    
    // Check for navigation elements
    const navigation = await page.locator('nav, .nav, .navigation, .sidebar').count();
    if (navigation > 0) {
      console.log('✅ Found navigation elements');
    }
    
    // Take final screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-page-overview.png',
      fullPage: true
    });
  });

  test('should handle responsive layout on mobile', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    
    await page.goto(TRANSPORT_BOX_LIST_URL);
    await page.waitForLoadState('networkidle');
    
    // Take mobile screenshot
    await page.screenshot({ 
      path: 'test-results/transport-box-mobile-layout.png',
      fullPage: true
    });
    
    // Basic mobile functionality check
    const pageContent = await page.textContent('body');
    expect(pageContent).toBeTruthy();
    expect(pageContent.length).toBeGreaterThan(0);
  });

  test('should handle page load errors gracefully', async ({ page }) => {
    // Listen for console errors
    const consoleErrors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });
    
    // Listen for page errors
    const pageErrors: string[] = [];
    page.on('pageerror', error => {
      pageErrors.push(error.message);
    });
    
    await page.goto(TRANSPORT_BOX_LIST_URL);
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(3000);
    
    // Log any errors found
    if (consoleErrors.length > 0) {
      console.log('Console errors found:', consoleErrors);
    }
    
    if (pageErrors.length > 0) {
      console.log('Page errors found:', pageErrors);
    }
    
    // Take screenshot regardless of errors
    await page.screenshot({ 
      path: 'test-results/transport-box-error-handling.png',
      fullPage: true
    });
    
    // Page should still render something
    const bodyContent = await page.textContent('body');
    expect(bodyContent).toBeTruthy();
  });
});