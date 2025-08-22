import { test, expect } from '@playwright/test';

test.describe('Catalog Detail Journal Integration', () => {
  test('should have Journal tab in catalog detail and allow adding notes', async ({ page }) => {
    // Navigate to catalog page
    await page.goto('http://localhost:3001/catalog');
    
    // Remove webpack overlay if present
    await page.evaluate(() => {
      const iframe = document.querySelector('#webpack-dev-server-client-overlay');
      if (iframe) iframe.remove();
    });
    
    // Wait for catalog to load
    await page.waitForSelector('table', { timeout: 10000 });
    
    // Click on first product to open detail
    const firstRow = page.locator('tbody tr').first();
    await firstRow.click({ force: true });
    
    // Wait for detail modal to open
    await page.waitForSelector('text="Základní informace"', { timeout: 10000 });
    
    // Click on Journal tab
    const journalTab = page.locator('button:has-text("Deník")');
    await expect(journalTab).toBeVisible();
    await journalTab.click();
    
    // Verify Journal button is present in the journal tab
    const journalButton = page.locator('button:has-text("Přidat záznam")');
    await expect(journalButton).toBeVisible();
    
    // Get the product code from detail modal
    const productCodeElement = page.locator('p.text-sm.text-gray-500:has-text("Kód:")');
    const productCodeText = await productCodeElement.textContent();
    const productCode = productCodeText?.replace('Kód: ', '').trim();
    
    // Click the Journal button
    await journalButton.click();
    
    // Wait for Journal modal to open
    await page.waitForSelector('h3:has-text("Nový záznam")', { timeout: 5000 });
    
    // Verify the product is pre-populated in associated products
    const associatedProduct = page.locator('.inline-flex.items-center.px-3.py-1.rounded-full.text-xs.font-medium.bg-indigo-100.text-indigo-800');
    await expect(associatedProduct).toHaveText(productCode || '');
    
    // Verify we can fill the form
    await page.fill('input[id="title"]', 'Test poznámka pro produkt');
    await page.fill('textarea[id="content"]', 'Toto je testovací poznámka vytvořená z detailu produktu.');
    
    // Close the journal modal
    const closeButton = page.locator('button[title="Zavřít (Esc)"]').last();
    await closeButton.click();
    
    // Verify journal modal is closed and we're back to catalog detail
    await expect(page.locator('h3:has-text("Nový záznam")')).not.toBeVisible();
    await expect(journalButton).toBeVisible();
    
    // Close catalog detail
    const catalogDetailCloseButton = page.locator('button:has(.h-6.w-6)').first();
    await catalogDetailCloseButton.click();
    
    // Verify we're back to catalog list
    await expect(page.locator('table')).toBeVisible();
  });
  
  test('should allow creating a journal entry from catalog detail', async ({ page }) => {
    // Navigate to catalog page
    await page.goto('http://localhost:3001/catalog');
    
    // Remove webpack overlay if present
    await page.evaluate(() => {
      const iframe = document.querySelector('#webpack-dev-server-client-overlay');
      if (iframe) iframe.remove();
    });
    
    // Wait for catalog to load
    await page.waitForSelector('table', { timeout: 10000 });
    
    // Click on first product to open detail
    const firstRow = page.locator('tbody tr').first();
    await firstRow.click({ force: true });
    
    // Wait for detail modal to open
    await page.waitForSelector('text="Základní informace"', { timeout: 10000 });
    
    // Get the product code
    const productCodeElement = page.locator('p.text-sm.text-gray-500:has-text("Kód:")');
    const productCodeText = await productCodeElement.textContent();
    const productCode = productCodeText?.replace('Kód: ', '').trim();
    
    // Click on Journal tab
    const journalTab = page.locator('button:has-text("Deník")');
    await journalTab.click();
    
    // Click the Add Journal button in the journal tab
    const journalButton = page.locator('button:has-text("Přidat záznam")');
    await journalButton.click();
    
    // Wait for Journal modal to open
    await page.waitForSelector('h3:has-text("Nový záznam")', { timeout: 5000 });
    
    // Fill the journal form
    const testTitle = `Test poznámka pro ${productCode}`;
    const testContent = `Důležitá poznámka o produktu ${productCode} vytvořená z detailu katalogové položky.`;
    
    await page.fill('input[id="title"]', testTitle);
    await page.fill('textarea[id="content"]', testContent);
    
    // Save the journal entry
    const saveButton = page.locator('button:has-text("Vytvořit záznam")');
    await saveButton.click();
    
    // Wait for save to complete (modal should close)
    await page.waitForSelector('h3:has-text("Nový záznam")', { state: 'hidden', timeout: 5000 });
    
    // Verify we're back to catalog detail
    await expect(journalButton).toBeVisible();
    
    // Navigate to Journal page to verify the entry was created
    await page.goto('http://localhost:3001/journal');
    
    // Wait for journal list to load
    await page.waitForSelector('h1:has-text("Deník")', { timeout: 5000 });
    
    // Look for our created entry
    const journalEntry = page.locator(`text="${testTitle}"`).first();
    await expect(journalEntry).toBeVisible({ timeout: 10000 });
    
    // Verify the product association is shown
    const productTag = page.locator(`.bg-indigo-100:has-text("${productCode}")`).first();
    await expect(productTag).toBeVisible();
  });
});