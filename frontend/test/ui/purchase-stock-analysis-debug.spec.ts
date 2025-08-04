import { test, expect } from '@playwright/test';

test.describe('Purchase Stock Analysis Debug', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to the Purchase Stock Analysis page
    await page.goto('/nakup/analyza-skladu');
    
    // Wait for the page to load
    await page.waitForLoadState('networkidle');
  });

  test('should debug button overflow issues', async ({ page }) => {
    // Take screenshot of the whole page first
    await page.screenshot({ path: 'debug-full-page.png', fullPage: true });

    // Check viewport and screen size
    const viewportSize = page.viewportSize();
    console.log('Viewport size:', viewportSize);

    // Find header area where buttons are
    const headerArea = page.locator('div').filter({ hasText: 'Analýza skladových zásob' }).first();
    await expect(headerArea).toBeVisible();

    // Take screenshot of header area
    await headerArea.screenshot({ path: 'debug-header-area.png' });

    // Check if buttons are visible and their positions
    const refreshButton = page.getByRole('button', { name: 'Obnovit' });
    const exportButton = page.getByRole('button', { name: 'Export' });

    console.log('Refresh button visible:', await refreshButton.isVisible());
    console.log('Export button visible:', await exportButton.isVisible());

    // Get button bounds
    if (await refreshButton.isVisible()) {
      const refreshBounds = await refreshButton.boundingBox();
      console.log('Refresh button bounds:', refreshBounds);
    }

    if (await exportButton.isVisible()) {
      const exportBounds = await exportButton.boundingBox();
      console.log('Export button bounds:', exportBounds);
    }

    // Check for horizontal scroll
    const bodyScrollWidth = await page.evaluate(() => document.body.scrollWidth);
    const bodyClientWidth = await page.evaluate(() => document.body.clientWidth);
    console.log('Body scroll width:', bodyScrollWidth);
    console.log('Body client width:', bodyClientWidth);
    console.log('Has horizontal overflow:', bodyScrollWidth > bodyClientWidth);

    // Test different screen sizes
    const screenSizes = [
      { width: 1200, height: 800, name: 'Desktop' },
      { width: 768, height: 1024, name: 'Tablet' },
      { width: 375, height: 667, name: 'Mobile' }
    ];

    for (const size of screenSizes) {
      await page.setViewportSize({ width: size.width, height: size.height });
      await page.waitForTimeout(500);
      
      console.log(`\n--- ${size.name} (${size.width}x${size.height}) ---`);
      
      // Check if buttons are still visible
      const refreshVisible = await refreshButton.isVisible();
      const exportVisible = await exportButton.isVisible();
      
      console.log(`Refresh button visible: ${refreshVisible}`);
      console.log(`Export button visible: ${exportVisible}`);
      
      // Check if buttons overflow
      if (refreshVisible && exportVisible) {
        const refreshBounds = await refreshButton.boundingBox();
        const exportBounds = await exportButton.boundingBox();
        
        if (refreshBounds && exportBounds) {
          const rightmostX = Math.max(refreshBounds.x + refreshBounds.width, exportBounds.x + exportBounds.width);
          console.log(`Rightmost button edge: ${rightmostX}px`);
          console.log(`Viewport width: ${size.width}px`);
          console.log(`Buttons overflow: ${rightmostX > size.width}`);
        }
      }
      
      // Take screenshot for this size
      await page.screenshot({ path: `debug-${size.name.toLowerCase()}.png` });
    }
  });

  test('should check responsive header layout', async ({ page }) => {
    // Test header at different sizes
    const headerContainer = page.locator('.flex.items-center.justify-between').first();
    await expect(headerContainer).toBeVisible();

    // Desktop size
    await page.setViewportSize({ width: 1200, height: 800 });
    await page.waitForTimeout(500);
    
    const headerBounds = await headerContainer.boundingBox();
    console.log('Header container bounds at desktop:', headerBounds);
    
    // Check if title and buttons fit
    const title = page.getByText('Analýza skladových zásob');
    const buttonGroup = page.locator('.flex.items-center.space-x-3');
    
    const titleBounds = await title.boundingBox();
    const buttonGroupBounds = await buttonGroup.boundingBox();
    
    console.log('Title bounds:', titleBounds);
    console.log('Button group bounds:', buttonGroupBounds);
    
    // Check for overlap
    if (titleBounds && buttonGroupBounds) {
      const overlap = titleBounds.x + titleBounds.width > buttonGroupBounds.x;
      console.log('Title and buttons overlap:', overlap);
    }

    // Tablet size - buttons might wrap or get smaller
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: 'debug-tablet-header.png' });
    
    // Mobile size - should definitely change layout
    await page.setViewportSize({ width: 375, height: 667 });
    await page.waitForTimeout(500);
    await page.screenshot({ path: 'debug-mobile-header.png' });
  });
});