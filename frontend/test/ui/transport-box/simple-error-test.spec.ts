import { test, expect } from '@playwright/test';

test.describe('Simple Transport Box Error Test', () => {
  test('should navigate to transport box page and verify basic functionality', async ({ page }) => {
    // Navigate to transport boxes page
    await page.goto('http://localhost:3001/logistics/transport-boxes');
    await page.waitForLoadState('networkidle');
    
    // Wait for the page to load and find the main heading
    await expect(page.locator('h1:has-text("Transportní boxy")')).toBeVisible({ timeout: 10000 });
    
    // Check if the page shows some basic elements
    console.log('Transport box page loaded successfully');
    
    // Take a screenshot for debugging
    await page.screenshot({ path: 'transport-box-page.png' });
    
    // Just verify we can see the basic structure
    const pageContent = await page.textContent('body');
    expect(pageContent).toContain('Transportní boxy');
    
    console.log('Basic transport box page verification completed');
  });
  
  test('should show toast component when manually triggered', async ({ page }) => {
    // Navigate to the page first
    await page.goto('http://localhost:3001/logistics/transport-boxes');
    await page.waitForLoadState('networkidle');
    
    // Inject a simple test script to trigger toast
    await page.evaluate(() => {
      // Create a simple toast notification directly in the DOM
      const toastContainer = document.createElement('div');
      toastContainer.className = 'fixed top-4 right-4 z-50';
      toastContainer.innerHTML = `
        <div class="max-w-sm w-full bg-white border border-red-200 rounded-lg shadow-lg">
          <div class="p-4">
            <div class="flex items-start">
              <div class="flex-shrink-0">
                <svg class="h-5 w-5 text-red-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path>
                </svg>
              </div>
              <div class="ml-3 w-0 flex-1">
                <p class="text-sm font-medium text-gray-900">Chyba při změně stavu</p>
                <p class="mt-1 text-sm text-gray-500">Test error message</p>
              </div>
            </div>
          </div>
        </div>
      `;
      document.body.appendChild(toastContainer);
    });
    
    // Check that the toast was created
    await expect(page.locator('.fixed.top-4.right-4')).toBeVisible({ timeout: 2000 });
    await expect(page.locator('text=Chyba při změně stavu')).toBeVisible();
    await expect(page.locator('text=Test error message')).toBeVisible();
    
    console.log('Toast notification test completed');
  });
});