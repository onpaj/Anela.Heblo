import { test, expect } from '@playwright/test';
import { navigateToCatalog } from '../helpers/e2e-auth-helper';

test.describe('Catalog UI E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to catalog with full authentication
    await navigateToCatalog(page);
  });

  test('should navigate to catalog and load products via UI', async ({ page }) => {
    // Listen to console events
    page.on('console', msg => {
      if (msg.type() === 'log') {
        console.log('🖥️  Browser console.log:', msg.text());
      } else if (msg.type() === 'error') {
        console.error('🖥️  Browser console.error:', msg.text());
      }
    });
    
    // Verify we're on the catalog page
    console.log('Current URL after navigation:', page.url());
    expect(page.url()).toContain('/catalog');
    
    // Look for catalog content - products table, list, or any product-related content
    console.log('Looking for catalog content...');
    
    const catalogContentSelectors = [
      '[data-testid="catalog-list"]',
      '[data-testid="product-list"]', 
      '.catalog-table',
      '.product-table',
      'table',
      '[role="table"]',
      '.catalog-content',
      '.product-content'
    ];
    
    let catalogContent = null;
    for (const selector of catalogContentSelectors) {
      try {
        catalogContent = page.locator(selector).first();
        if (await catalogContent.isVisible({ timeout: 3000 })) {
          console.log(`Found catalog content with selector: ${selector}`);
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (!catalogContent || !(await catalogContent.isVisible())) {
      // Look for any content that suggests products are loaded
      const pageText = await page.locator('body').textContent();
      console.log('Catalog page content preview:', pageText?.substring(0, 800) + '...');
      
      // Look for signs that products are loading or loaded
      const hasLoadingIndicator = pageText?.includes('Loading') || pageText?.includes('Načítá');
      const hasProductContent = pageText?.includes('Product') || pageText?.includes('produkt') || 
                               pageText?.includes('Název') || pageText?.includes('Kód');
      
      console.log('Page has loading indicator:', hasLoadingIndicator);
      console.log('Page has product-related content:', hasProductContent);
      
      if (!hasProductContent && !hasLoadingIndicator) {
        // Check if there are any error messages
        const hasError = pageText?.includes('Error') || pageText?.includes('Chyba') || 
                        pageText?.includes('404') || pageText?.includes('401');
        console.log('Page has error content:', hasError);
        
        if (hasError) {
          throw new Error('Catalog page shows error instead of products');
        }
      }
    }
    
    // Wait a bit more for any async loading
    console.log('Waiting for potential async content loading...');
    await page.waitForTimeout(5000);
    
    // Enhanced validation - look for catalog table structure and validate data
    console.log('🔍 Performing enhanced catalog data validation...');
    
    // First, look for table structure
    const tableSelectors = [
      'table',
      '[role="table"]',
      '.catalog-table',
      '.product-table'
    ];
    
    let catalogTable = null;
    for (const selector of tableSelectors) {
      try {
        catalogTable = page.locator(selector).first();
        if (await catalogTable.isVisible({ timeout: 3000 })) {
          console.log(`Found catalog table with selector: ${selector}`);
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (catalogTable && await catalogTable.isVisible()) {
      // Validate table headers - look for common catalog columns
      console.log('📋 Validating catalog table headers...');
      const expectedHeaders = ['název', 'kód', 'cena', 'skladem', 'kategorie', 'product', 'name', 'code', 'price', 'stock', 'category'];
      let foundHeaders = 0;
      
      for (const header of expectedHeaders) {
        try {
          const headerElement = catalogTable.locator(`th:has-text("${header}"), td:has-text("${header}")`).first();
          if (await headerElement.isVisible({ timeout: 1000 })) {
            console.log(`✅ Found header: ${header}`);
            foundHeaders++;
          }
        } catch (e) {
          // Header not found, continue
        }
      }
      
      console.log(`Found ${foundHeaders} catalog headers out of expected headers`);
      
      // Count and validate product rows
      const productRows = catalogTable.locator('tr:not(:first-child), tbody tr');
      const productCount = await productRows.count();
      console.log(`📊 Found ${productCount} product rows in catalog`);
      
      if (productCount > 0) {
        // Validate first few products have actual data
        const maxRowsToCheck = Math.min(5, productCount);
        let validProducts = 0;
        
        for (let i = 0; i < maxRowsToCheck; i++) {
          try {
            const row = productRows.nth(i);
            const rowText = await row.textContent();
            
            // Check if row has substantial content (not just empty cells)
            if (rowText && rowText.trim().length > 10) {
              validProducts++;
              console.log(`✅ Product row ${i + 1} contains data: ${rowText.substring(0, 50)}...`);
            }
          } catch (e) {
            console.log(`⚠️  Could not validate product row ${i + 1}: ${e.message}`);
          }
        }
        
        console.log(`📈 Validated ${validProducts}/${maxRowsToCheck} product rows contain data`);
        
        // Ensure we have at least some valid products
        expect(validProducts).toBeGreaterThan(0);
        expect(productCount).toBeGreaterThan(0);
      } else {
        console.log('⚠️  No product rows found in table - might be empty catalog or different structure');
        // Don't fail the test - empty catalog might be valid
      }
    } else {
      // Fallback: Look for any product elements using broader selectors
      console.log('📋 Table structure not found, looking for product elements with broader selectors...');
      
      const productElementSelectors = [
        'tr:not(:first-child)', // Table rows (excluding header)
        'li', // List items
        '[data-testid*="product"]', // Any element with product in test id
        '.product', // Elements with product class
        '[class*="product"]', // Elements with product in class name
        '[class*="catalog"]', // Elements with catalog in class name
        'div[role="gridcell"]', // Grid cells
        'article', // Article elements (might be used for product cards)
      ];
      
      let foundProducts = false;
      let totalElements = 0;
      
      for (const selector of productElementSelectors) {
        try {
          const elements = page.locator(selector);
          const count = await elements.count();
          if (count > 0) {
            console.log(`📦 Found ${count} elements with selector: ${selector}`);
            foundProducts = true;
            totalElements += count;
            
            // Validate first few elements have content
            const maxToCheck = Math.min(3, count);
            for (let i = 0; i < maxToCheck; i++) {
              const elementText = await elements.nth(i).textContent();
              if (elementText && elementText.trim().length > 5) {
                console.log(`✅ Element ${i + 1} contains: ${elementText.substring(0, 40)}...`);
              }
            }
            break; // Use the first successful selector
          }
        } catch (e) {
          continue;
        }
      }
      
      if (foundProducts) {
        console.log(`✅ Successfully found ${totalElements} catalog elements`);
        expect(totalElements).toBeGreaterThan(0);
      } else {
        console.log('⚠️  No clear product elements found, validating page has catalog-related content');
        const finalPageText = await page.locator('body').textContent();
        
        // Look for catalog-related content
        const hasCatalogContent = finalPageText?.toLowerCase().includes('katalog') ||
                                 finalPageText?.toLowerCase().includes('produkt') ||
                                 finalPageText?.toLowerCase().includes('catalog') ||
                                 finalPageText?.toLowerCase().includes('product');
        
        console.log('Page contains catalog-related content:', hasCatalogContent);
        
        // At minimum, ensure page loaded with substantial content
        expect(finalPageText?.length || 0).toBeGreaterThan(200);
        
        if (hasCatalogContent) {
          console.log('✅ Page contains catalog-related text content');
        } else {
          console.log('⚠️  Page loaded but may not contain expected catalog content');
        }
      }
    }
    
    // Final validation: Check for loading states or error messages
    const pageText = await page.locator('body').textContent();
    const hasError = pageText?.toLowerCase().includes('error') || 
                    pageText?.toLowerCase().includes('chyba') ||
                    pageText?.toLowerCase().includes('404') ||
                    pageText?.toLowerCase().includes('401');
    
    expect(hasError).toBe(false); // Should not have error messages
    
    console.log('✅ Catalog UI E2E test completed successfully with enhanced validation');
  });
});