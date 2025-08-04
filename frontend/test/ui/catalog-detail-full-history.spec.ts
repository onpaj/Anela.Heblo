import { test, expect } from '@playwright/test';

test.describe('Catalog Detail Full History Functionality', () => {
  test.beforeEach(async ({ page }) => {
    // Mock catalog list API
    await page.route('**/api/catalog?**', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [{
            productCode: "TEST001",
            productName: "Test Product for Full History",
            type: 3,
            stock: { eshop: 0, erp: 100, transport: 0, reserve: 10, available: 90 },
            price: { eshopPrice: { priceWithVat: 299, purchasePrice: 150 } },
            properties: { optimalStockDaysSetup: 30, stockMinSetup: 20, batchSize: 100, seasonMonths: [] },
            location: "Test Sklad",
            minimalOrderQuantity: "50",
            minimalManufactureQuantity: 25
          }],
          totalCount: 1, pageNumber: 1, pageSize: 20, totalPages: 1
        })
      });
    });

    // Mock catalog detail API with different responses based on monthsBack parameter
    await page.route('**/api/catalog/TEST001**', async (route) => {
      const url = route.request().url();
      console.log('API call with URL:', url);
      
      // Check if this is full history request (monthsBack=999)
      const isFullHistory = url.includes('monthsBack=999');
      console.log('Is full history request:', isFullHistory);
      
      const purchaseHistory = isFullHistory ? [
        // Full history - 8 records (including older ones)
        { date: "2024-01-15T00:00:00Z", supplierName: "Dodavatel ABC s.r.o.", amount: 200, pricePerPiece: 145.50, priceTotal: 29100.00, documentNumber: "FACT-2024-001" },
        { date: "2024-01-08T00:00:00Z", supplierName: "Dodavatel XYZ a.s.", amount: 150, pricePerPiece: 148.75, priceTotal: 22312.50, documentNumber: "FACT-2024-002" },
        { date: "2023-12-20T00:00:00Z", supplierName: "Dodavatel ABC s.r.o.", amount: 100, pricePerPiece: 142.00, priceTotal: 14200.00, documentNumber: "FACT-2023-145" },
        { date: "2023-12-10T00:00:00Z", supplierName: "Dodavatel MEGA spol. s r.o.", amount: 75, pricePerPiece: 139.25, priceTotal: 10443.75, documentNumber: "DOK-2023-889" },
        { date: "2023-11-25T00:00:00Z", supplierName: "Dodavatel XYZ a.s.", amount: 300, pricePerPiece: 144.90, priceTotal: 43470.00, documentNumber: "FACT-2023-998" },
        // Additional older records for full history
        { date: "2022-08-15T00:00:00Z", supplierName: "Dodavatel OLD Ltd.", amount: 500, pricePerPiece: 120.00, priceTotal: 60000.00, documentNumber: "OLD-2022-001" },
        { date: "2021-03-10T00:00:00Z", supplierName: "Dodavatel ANCIENT s.r.o.", amount: 250, pricePerPiece: 110.00, priceTotal: 27500.00, documentNumber: "ANC-2021-050" },
        { date: "2020-01-05T00:00:00Z", supplierName: "Dodavatel VINTAGE a.s.", amount: 180, pricePerPiece: 105.00, priceTotal: 18900.00, documentNumber: "VIN-2020-999" }
      ] : [
        // Limited history - only 5 recent records
        { date: "2024-01-15T00:00:00Z", supplierName: "Dodavatel ABC s.r.o.", amount: 200, pricePerPiece: 145.50, priceTotal: 29100.00, documentNumber: "FACT-2024-001" },
        { date: "2024-01-08T00:00:00Z", supplierName: "Dodavatel XYZ a.s.", amount: 150, pricePerPiece: 148.75, priceTotal: 22312.50, documentNumber: "FACT-2024-002" },
        { date: "2023-12-20T00:00:00Z", supplierName: "Dodavatel ABC s.r.o.", amount: 100, pricePerPiece: 142.00, priceTotal: 14200.00, documentNumber: "FACT-2023-145" },
        { date: "2023-12-10T00:00:00Z", supplierName: "Dodavatel MEGA spol. s r.o.", amount: 75, pricePerPiece: 139.25, priceTotal: 10443.75, documentNumber: "DOK-2023-889" },
        { date: "2023-11-25T00:00:00Z", supplierName: "Dodavatel XYZ a.s.", amount: 300, pricePerPiece: 144.90, priceTotal: 43470.00, documentNumber: "FACT-2023-998" }
      ];
      
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          item: {
            productCode: "TEST001",
            productName: "Test Product for Full History",
            type: 3,
            stock: { eshop: 0, erp: 100, transport: 0, reserve: 10, available: 90 },
            price: { eshopPrice: { priceWithVat: 299, purchasePrice: 150 } },
            properties: { optimalStockDaysSetup: 30, stockMinSetup: 20, batchSize: 100, seasonMonths: [] },
            location: "Test Sklad",
            minimalOrderQuantity: "50",
            minimalManufactureQuantity: 25
          },
          historicalData: {
            salesHistory: [],
            purchaseHistory: purchaseHistory,
            consumedHistory: []
          }
        })
      });
    });

    await page.goto('/catalog');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);
  });

  test('should load more records when full history is requested', async ({ page }) => {
    // Click on test product
    await page.locator('tr', { hasText: 'Test Product for Full History' }).click();
    await page.waitForTimeout(1000);

    // Switch to purchase history tab
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    await page.waitForTimeout(1000);

    // Verify initial state shows 5 records (limited history)
    const historyList = page.locator('.bg-white.rounded-lg .space-y-1');
    let recordCount = await historyList.locator('div[class*="border-b"]').count();
    console.log('Initial record count:', recordCount);
    expect(recordCount).toBe(5);

    // Verify the button says "Zobrazit celou historii" initially
    const fullHistoryButton = page.locator('button', { hasText: 'Zobrazit celou historii' });
    await expect(fullHistoryButton).toBeVisible();

    // Click "Show full history" button
    await fullHistoryButton.click();
    
    // Wait for loading and new data
    await page.waitForTimeout(2000);

    // Verify button text changed
    await expect(page.locator('button', { hasText: 'Zobrazit posledních 13 měsíců' })).toBeVisible();

    // Verify we now have 8 records (full history)
    recordCount = await historyList.locator('div[class*="border-b"]').count();
    console.log('Full history record count:', recordCount);
    expect(recordCount).toBe(8);

    // Verify we can see older records
    await expect(historyList).toContainText('Dodavatel OLD Ltd.');
    await expect(historyList).toContainText('OLD-2022-001');
    await expect(historyList).toContainText('Dodavatel ANCIENT s.r.o.');
    await expect(historyList).toContainText('ANC-2021-050');

    // Take screenshot for verification
    await page.screenshot({ path: 'test-results/catalog-detail-full-history-test.png' });
  });

  test('should toggle back to limited history', async ({ page }) => {
    // Click on test product and switch to history tab
    await page.locator('tr', { hasText: 'Test Product for Full History' }).click();
    await page.waitForTimeout(1000);

    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    await page.waitForTimeout(1000);

    // Click "Show full history"
    await page.locator('button', { hasText: 'Zobrazit celou historii' }).click();
    await page.waitForTimeout(2000);

    // Verify we have full history (8 records)
    const historyList = page.locator('.bg-white.rounded-lg .space-y-1');
    let recordCount = await historyList.locator('div[class*="border-b"]').count();
    expect(recordCount).toBe(8);

    // Click "Show last 13 months" to go back
    await page.locator('button', { hasText: 'Zobrazit posledních 13 měsíců' }).click();
    await page.waitForTimeout(2000);

    // Verify we're back to 5 records
    recordCount = await historyList.locator('div[class*="border-b"]').count();
    expect(recordCount).toBe(5);

    // Verify button text is back to original
    await expect(page.locator('button', { hasText: 'Zobrazit celou historii' })).toBeVisible();
  });
});