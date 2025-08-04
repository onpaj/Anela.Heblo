import { test, expect } from '@playwright/test';

test.describe('Catalog Detail Purchase History with Mock Data', () => {
  test.beforeEach(async ({ page }) => {
    // Mock the catalog list API
    await page.route('**/api/catalog?**', async (route) => {
      console.log('Mock catalog list route called:', route.request().url());
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            items: [
              {
                productCode: "TEST001",
                productName: "Test Product s historií",
                type: 3,
                stock: {
                  eshop: 0,
                  erp: 100,
                  transport: 0,
                  reserve: 10,
                  available: 90
                },
                price: {
                  currentSellingPrice: null,
                  currentPurchasePrice: null,
                  sellingPriceWithVat: null,
                  purchasePriceWithVat: null,
                  eshopPrice: {
                    priceWithVat: 299,
                    purchasePrice: 150
                  },
                  erpPrice: {
                    priceWithoutVat: 249,
                    priceWithVat: 301,
                    purchasePrice: 152
                  }
                },
                properties: {
                  optimalStockDaysSetup: 30,
                  stockMinSetup: 20,
                  batchSize: 100,
                  seasonMonths: []
                },
                location: "Test Sklad",
                minimalOrderQuantity: "50",
                minimalManufactureQuantity: 25
              }
            ],
            totalCount: 1,
            pageNumber: 1,
            pageSize: 20,
            totalPages: 1
          })
        });
    });

    // Mock the catalog detail API separately
    await page.route('**/api/catalog/TEST001**', async (route) => {
      console.log('Mock catalog detail route called:', route.request().url());
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            item: {
              productCode: "TEST001",
              productName: "Test Product s historií",
              type: 3,
              stock: {
                eshop: 0,
                erp: 100,
                transport: 0,
                reserve: 10,
                available: 90
              },
              price: {
                currentSellingPrice: null,
                currentPurchasePrice: null,
                sellingPriceWithVat: null,
                purchasePriceWithVat: null,
                eshopPrice: {
                  priceWithVat: 299,
                  purchasePrice: 150
                },
                erpPrice: {
                  priceWithoutVat: 249,
                  priceWithVat: 301,
                  purchasePrice: 152
                }
              },
              properties: {
                optimalStockDaysSetup: 30,
                stockMinSetup: 20,
                batchSize: 100,
                seasonMonths: []
              },
              location: "Test Sklad",
              minimalOrderQuantity: "50",
              minimalManufactureQuantity: 25
            },
            historicalData: {
              salesHistory: [
                {
                  year: 2024,
                  month: 1,
                  amountTotal: 150,
                  amountB2B: 100,
                  amountB2C: 50,
                  sumTotal: 45000,
                  sumB2B: 30000,
                  sumB2C: 15000
                }
              ],
              purchaseHistory: [
                {
                  date: "2024-01-15T00:00:00Z",
                  supplierName: "Dodavatel ABC s.r.o.",
                  amount: 200,
                  pricePerPiece: 145.50,
                  priceTotal: 29100.00,
                  documentNumber: "FACT-2024-001"
                },
                {
                  date: "2024-01-08T00:00:00Z",
                  supplierName: "Dodavatel XYZ a.s.",
                  amount: 150,
                  pricePerPiece: 148.75,
                  priceTotal: 22312.50,
                  documentNumber: "FACT-2024-002"
                },
                {
                  date: "2023-12-20T00:00:00Z",
                  supplierName: "Dodavatel ABC s.r.o.",
                  amount: 100,
                  pricePerPiece: 142.00,
                  priceTotal: 14200.00,
                  documentNumber: "FACT-2023-145"
                },
                {
                  date: "2023-12-10T00:00:00Z",
                  supplierName: "Dodavatel MEGA spol. s r.o.",
                  amount: 75,
                  pricePerPiece: 139.25,
                  priceTotal: 10443.75,
                  documentNumber: "DOK-2023-889"
                },
                {
                  date: "2023-11-25T00:00:00Z",
                  supplierName: "Dodavatel XYZ a.s.",
                  amount: 300,
                  pricePerPiece: 144.90,
                  priceTotal: 43470.00,
                  documentNumber: "FACT-2023-998"
                }
              ],
              consumedHistory: [
                {
                  year: 2024,
                  month: 1,
                  amount: 80,
                  productName: "Test Product s historií"
                }
              ]
            }
          })
        });
    });

    // Navigate to catalog page
    await page.goto('/catalog');
    await page.waitForLoadState('domcontentloaded');
    await page.waitForTimeout(2000);
  });

  test('should display purchase history table with real data', async ({ page }) => {
    // Debug: Take screenshot of initial page
    await page.screenshot({ path: 'test-results/debug-initial-page.png' });
    
    // Click on the test product
    const productRow = page.locator('tr', { hasText: 'Test Product s historií' });
    console.log('Checking if product row is visible...');
    await expect(productRow).toBeVisible();
    
    console.log('Clicking on product row...');
    await productRow.click();
    
    // Wait for modal to open
    await page.waitForTimeout(1000);
    
    // Debug: Take screenshot after click
    await page.screenshot({ path: 'test-results/debug-after-click.png' });
    
    // Wait longer for detail API call
    await page.waitForTimeout(3000);
    
    // Verify modal is open
    const modal = page.locator('.fixed.inset-0.bg-black.bg-opacity-50');
    console.log('Checking if modal is visible...');
    await expect(modal).toBeVisible();
    
    // Switch to purchase history tab
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await expect(purchaseHistoryTab).toBeVisible();
    await purchaseHistoryTab.click();
    await page.waitForTimeout(1000);
    
    // Take screenshot after switching to purchase history tab
    await page.screenshot({ path: 'test-results/debug-purchase-history-tab.png' });
    
    // Verify purchase history tab is active
    await expect(purchaseHistoryTab).toHaveClass(/border-indigo-500/);
    
    // Verify history list is visible (not empty state)  
    const historyList = page.locator('.bg-white.rounded-lg .space-y-1');
    await expect(historyList).toBeVisible();
    
    // Verify "Show full history" button is present
    await expect(page.locator('button', { hasText: 'Zobrazit celou historii' })).toBeVisible();
    
    // Verify first record data (most recent - 2024-01-15)
    const firstRecord = historyList.locator('div').first();
    await expect(firstRecord).toContainText('15. 01. 2024');
    await expect(firstRecord).toContainText('Dodavatel ABC s.r.o.');
    await expect(firstRecord).toContainText('145,50 Kč/ks');
    await expect(firstRecord).toContainText('Množství: 200');
    await expect(firstRecord).toContainText('Celkem: 29 100,00 Kč');
    await expect(firstRecord).toContainText('FACT-2024-001');
    
    // Verify second record data (2024-01-08)
    const secondRecord = historyList.locator('div').nth(1);
    await expect(secondRecord).toContainText('08. 01. 2024');
    await expect(secondRecord).toContainText('Dodavatel XYZ a.s.');
    await expect(secondRecord).toContainText('148,75 Kč/ks');
    
    // Verify we have 5 records
    const recordCount = await historyList.locator('div[class*="border-b"]').count();
    expect(recordCount).toBe(5);
    
    // Scroll down within the modal content to see the summary section
    const modalContent = page.locator('.p-6.overflow-y-auto');
    await modalContent.evaluate(el => el.scrollTo(0, el.scrollHeight));
    await page.waitForTimeout(500);
    
    // Take screenshot to see summary section
    await page.screenshot({ path: 'test-results/debug-summary-section.png' });
    
    // Verify summary statistics using more specific selector
    await expect(page.locator('text=Celkové nákupy')).toBeVisible();
    
    // Calculate expected values from our mock data
    // Total amount: 200+150+100+75+300 = 825
    // Total cost: 29100+22312.50+14200+10443.75+43470 = 119526.25
    // Average price: 119526.25/825 = 144.88... (let's check what's actually displayed)
    
    await expect(page.locator('text=825')).toBeVisible();
    await expect(page.locator('text=119 526,25 Kč')).toBeVisible();
    
    // Take screenshot for verification
    await page.screenshot({ path: 'test-results/catalog-detail-purchase-history-with-data.png' });
  });

  test('should sort purchase history by date descending', async ({ page }) => {
    // Click on the test product and open purchase history
    await page.locator('tr', { hasText: 'Test Product s historií' }).click();
    await page.waitForTimeout(1000);
    
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    await page.waitForTimeout(500);
    
    const historyList = page.locator('.bg-white.rounded-lg .space-y-1');
    
    // Verify dates are in descending order
    const records = historyList.locator('div[class*="border-b"]');
    
    // Check first few records are properly sorted
    await expect(records.nth(0)).toContainText('15. 01. 2024'); // Most recent
    await expect(records.nth(1)).toContainText('08.01.2024');
    await expect(records.nth(2)).toContainText('20.12.2023');
    await expect(records.nth(3)).toContainText('10.12.2023');
    await expect(records.nth(4)).toContainText('25.11.2023'); // Oldest
  });

  test('should display different suppliers correctly', async ({ page }) => {
    // Click on the test product and open purchase history
    await page.locator('tr', { hasText: 'Test Product s historií' }).click();
    await page.waitForTimeout(1000);
    
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    await page.waitForTimeout(500);
    
    const historyList = page.locator('.bg-white.rounded-lg .space-y-1');
    
    // Verify different suppliers are displayed
    await expect(historyList).toContainText('Dodavatel ABC s.r.o.');
    await expect(historyList).toContainText('Dodavatel XYZ a.s.');
    await expect(historyList).toContainText('Dodavatel MEGA spol. s r.o.');
  });

  test('should format prices correctly in Czech locale', async ({ page }) => {
    // Click on the test product and open purchase history
    await page.locator('tr', { hasText: 'Test Product s historií' }).click();
    await page.waitForTimeout(1000);
    
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    await page.waitForTimeout(500);
    
    const historyList = page.locator('.bg-white.rounded-lg .space-y-1');
    
    // Verify Czech number formatting (comma as decimal separator)
    await expect(historyList).toContainText('145,50 Kč/ks');
    await expect(historyList).toContainText('Celkem: 29 100,00 Kč');
    await expect(historyList).toContainText('148,75 Kč/ks');
    await expect(historyList).toContainText('Celkem: 22 312,50 Kč');
  });

  test('should switch between tabs and maintain data', async ({ page }) => {
    // Click on the test product
    await page.locator('tr', { hasText: 'Test Product s historií' }).click();
    await page.waitForTimeout(1000);
    
    // Start on basic info tab - verify basic info is shown
    const basicInfoTab = page.locator('button', { hasText: 'Základní informace' });
    await expect(basicInfoTab).toHaveClass(/border-indigo-500/);
    await expect(page.locator('span', { hasText: 'Test Sklad' })).toBeVisible();
    
    // Switch to purchase history tab
    const purchaseHistoryTab = page.locator('button', { hasText: 'Historie nákupů' });
    await purchaseHistoryTab.click();
    await page.waitForTimeout(500);
    
    // Verify purchase history is shown
    await expect(purchaseHistoryTab).toHaveClass(/border-indigo-500/);
    await expect(page.locator('td', { hasText: 'Dodavatel ABC s.r.o.' }).first()).toBeVisible();
    
    // Switch back to basic info
    await basicInfoTab.click();
    await page.waitForTimeout(500);
    
    // Verify basic info is shown again
    await expect(basicInfoTab).toHaveClass(/border-indigo-500/);
    await expect(page.locator('span', { hasText: 'Test Sklad' })).toBeVisible();
    
    // Switch back to purchase history and verify data is still there
    await purchaseHistoryTab.click();
    await page.waitForTimeout(500);
    
    await expect(page.locator('td', { hasText: 'Dodavatel ABC s.r.o.' }).first()).toBeVisible();
    await expect(page.locator('td', { hasText: 'FACT-2024-001' })).toBeVisible();
  });
});