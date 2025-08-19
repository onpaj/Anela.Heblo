import { test, expect } from '@playwright/test';

test.describe('Navigation Products Submenu', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3001/');
  });

  test('should display Products submenu above Purchase section', async ({ page }) => {
    // Find all navigation sections
    const navigationSections = await page.locator('[data-testid="nav-section"], [data-testid="nav-single"]').all();
    
    // Get section names
    const sectionNames = [];
    for (const section of navigationSections) {
      const nameElement = section.locator('text=Dashboard, Produkty, Nákup').first();
      if (await nameElement.isVisible()) {
        sectionNames.push(await nameElement.textContent());
      }
    }
    
    // Check that "Produkty" appears before "Nákup"
    const produktyIndex = sectionNames.findIndex(name => name?.includes('Produkty'));
    const nakupIndex = sectionNames.findIndex(name => name?.includes('Nákup'));
    
    expect(produktyIndex).toBeGreaterThanOrEqual(0);
    expect(nakupIndex).toBeGreaterThanOrEqual(0);
    expect(produktyIndex).toBeLessThan(nakupIndex);
  });

  test('should show Catalog link inside Products submenu', async ({ page }) => {
    // Find and click on Products section to expand it
    await page.locator('button:has-text("Produkty")').click();
    
    // Wait for submenu to expand
    await page.waitForSelector('text="Katalog"', { timeout: 5000 });
    
    // Check that Katalog link is inside Products submenu
    const catalogLink = page.locator('a:has-text("Katalog")');
    await expect(catalogLink).toBeVisible();
    
    // Verify that Katalog link has correct href
    await expect(catalogLink).toHaveAttribute('href', '/catalog');
  });

  test('should navigate to catalog page when Katalog link is clicked', async ({ page }) => {
    // Expand Products submenu
    await page.locator('button:has-text("Produkty")').click();
    
    // Click on Katalog link
    await page.locator('a:has-text("Katalog")').click();
    
    // Verify navigation to catalog page
    await expect(page).toHaveURL('http://localhost:3001/catalog');
    
    // Verify catalog page content loads
    await expect(page.locator('text="Katalog"')).toBeVisible();
  });

  test('should show Marže produktů link inside Products submenu', async ({ page }) => {
    // Expand Products submenu
    await page.locator('button:has-text("Produkty")').click();
    
    // Check that Marže produktů link is visible
    const marzeLink = page.locator('a:has-text("Marže produktů")');
    await expect(marzeLink).toBeVisible();
    
    // Verify that Marže produktů link has correct href
    await expect(marzeLink).toHaveAttribute('href', '/produkty/marze');
  });
});