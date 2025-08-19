import { test, expect } from '@playwright/test';

test.describe('Catalog Pagination and Sorting', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate directly to the catalog page
    await page.goto('/catalog');
    await page.waitForLoadState('domcontentloaded');
    
    // Wait for React content to load
    await page.waitForSelector('#root', { timeout: 10000 });
    await page.waitForTimeout(2000);
  });

  test.describe('Sorting Functionality', () => {
    test('should show sortable headers with arrow indicators', async ({ page }) => {
      // Check that sortable headers are present
      const sortableHeaders = [
        'Kód produktu',
        'Název produktu', 
        'Typ',
        'Dostupné'
      ];

      for (const headerText of sortableHeaders) {
        const header = page.locator(`th:has-text("${headerText}")`);
        await expect(header).toBeVisible();
        
        // Check for cursor pointer style
        await expect(header).toHaveClass(/cursor-pointer/);
        
        // Check for arrow indicators (ChevronUp/ChevronDown icons)
        const chevronUp = header.locator('svg').first();
        const chevronDown = header.locator('svg').last();
        
        await expect(chevronUp).toBeVisible();
        await expect(chevronDown).toBeVisible();
      }
    });

    test('should sort by product code when clicking header', async ({ page }) => {
      const productCodeHeader = page.locator('th:has-text("Kód produktu")');
      
      // Click to sort ascending
      await productCodeHeader.click();
      await page.waitForTimeout(500);
      
      // Get all product codes from the table
      const productCodes = await page.locator('tbody tr td:first-child').allTextContents();
      
      if (productCodes.length > 1) {
        // Check if they are sorted ascending
        const sortedCodes = [...productCodes].sort();
        expect(productCodes).toEqual(sortedCodes);
        
        // Click again to sort descending
        await productCodeHeader.click();
        await page.waitForTimeout(500);
        
        const productCodesDesc = await page.locator('tbody tr td:first-child').allTextContents();
        const sortedCodesDesc = [...productCodes].sort().reverse();
        expect(productCodesDesc).toEqual(sortedCodesDesc);
      }
      
      await page.screenshot({ path: 'test-results/catalog-sorting-product-code.png' });
    });

    test('should sort by product name when clicking header', async ({ page }) => {
      const productNameHeader = page.locator('th:has-text("Název produktu")');
      
      // Click to sort ascending
      await productNameHeader.click();
      await page.waitForTimeout(500);
      
      // Get all product names from the table
      const productNames = await page.locator('tbody tr td:nth-child(2)').allTextContents();
      
      if (productNames.length > 1) {
        // Check if they are sorted ascending (Czech locale)
        const sortedNames = [...productNames].sort((a, b) => a.localeCompare(b, 'cs'));
        expect(productNames).toEqual(sortedNames);
      }
      
      await page.screenshot({ path: 'test-results/catalog-sorting-product-name.png' });
    });

    test('should show visual indicators for active sorting', async ({ page }) => {
      const productCodeHeader = page.locator('th:has-text("Kód produktu")');
      
      // Before clicking - arrows should be gray
      const chevronUpBefore = productCodeHeader.locator('svg').first();
      await expect(chevronUpBefore).toHaveClass(/text-gray-300/);
      
      // Click to sort
      await productCodeHeader.click();
      await page.waitForTimeout(500);
      
      // After clicking - one arrow should be colored (indigo)
      const chevronUpAfter = productCodeHeader.locator('svg').first();
      await expect(chevronUpAfter).toHaveClass(/text-indigo-600/);
      
      await page.screenshot({ path: 'test-results/catalog-sorting-visual-indicators.png' });
    });
  });

  test.describe('Pagination Functionality', () => {
    test('should show pagination controls when there are items', async ({ page }) => {
      // Check if pagination section is visible
      const paginationSection = page.locator('[aria-label="Pagination"]').locator('..');
      
      // If there are items, pagination should be visible
      const tableRows = page.locator('tbody tr');
      const rowCount = await tableRows.count();
      
      if (rowCount > 0) {
        await expect(paginationSection).toBeVisible();
        
        // Check for previous/next buttons
        const prevButton = page.locator('button:has-text("Předchozí"), button[aria-label*="Předchozí"]');
        const nextButton = page.locator('button:has-text("Další"), button[aria-label*="Další"]');
        
        await expect(prevButton).toBeAttached();
        await expect(nextButton).toBeAttached();
        
        // Check for page size selector
        const pageSizeSelect = page.locator('select#pageSize');
        await expect(pageSizeSelect).toBeVisible();
        
        // Check available page sizes
        const pageSizeOptions = await pageSizeSelect.locator('option').allTextContents();
        expect(pageSizeOptions).toContain('10');
        expect(pageSizeOptions).toContain('20');
        expect(pageSizeOptions).toContain('50');
        expect(pageSizeOptions).toContain('100');
      }
      
      await page.screenshot({ path: 'test-results/catalog-pagination-controls.png' });
    });

    test('should display correct pagination info', async ({ page }) => {
      // Wait for data to load
      await page.waitForTimeout(2000);
      
      const tableRows = page.locator('tbody tr');
      const rowCount = await tableRows.count();
      
      if (rowCount > 0) {
        // Check pagination info text
        const paginationInfo = page.locator('text=/\\d+-\\d+ z \\d+/');
        await expect(paginationInfo).toBeVisible();
        
        const infoText = await paginationInfo.textContent();
        console.log('Pagination info:', infoText);
        
        // The info should contain numbers
        expect(infoText).toMatch(/\d+-\d+ z \d+/);
      }
      
      await page.screenshot({ path: 'test-results/catalog-pagination-info.png' });
    });

    test('should change page size when selector is used', async ({ page }) => {
      await page.waitForTimeout(2000);
      
      const tableRows = page.locator('tbody tr');
      const initialRowCount = await tableRows.count();
      
      if (initialRowCount > 0) {
        const pageSizeSelect = page.locator('select#pageSize');
        
        // Change to 10 items per page
        await pageSizeSelect.selectOption('10');
        await page.waitForTimeout(1000);
        
        const newRowCount = await page.locator('tbody tr').count();
        
        // Row count should change (unless there were already ≤10 items)
        if (initialRowCount > 10) {
          expect(newRowCount).toBeLessThanOrEqual(10);
        }
        
        // Change to 50 items per page
        await pageSizeSelect.selectOption('50');
        await page.waitForTimeout(1000);
        
        const finalRowCount = await page.locator('tbody tr').count();
        console.log(`Row counts: initial=${initialRowCount}, 10pp=${newRowCount}, 50pp=${finalRowCount}`);
      }
      
      await page.screenshot({ path: 'test-results/catalog-page-size-change.png' });
    });

    test('should navigate between pages when multiple pages exist', async ({ page }) => {
      await page.waitForTimeout(2000);
      
      // Set small page size to ensure multiple pages
      const pageSizeSelect = page.locator('select#pageSize');
      await pageSizeSelect.selectOption('10');
      await page.waitForTimeout(1000);
      
      // Check if there are multiple pages
      const pageButtons = page.locator('[aria-label="Pagination"] button').filter({ hasText: /^\\d+$/ });
      const pageButtonCount = await pageButtons.count();
      
      if (pageButtonCount > 1) {
        // Click on page 2
        const page2Button = page.locator('[aria-label="Pagination"] button:has-text("2")');
        if (await page2Button.isVisible()) {
          await page2Button.click();
          await page.waitForTimeout(1000);
          
          // Check that page 2 is now active
          await expect(page2Button).toHaveClass(/bg-indigo-50/);
          
          // Go back to page 1
          const page1Button = page.locator('[aria-label="Pagination"] button:has-text("1")');
          await page1Button.click();
          await page.waitForTimeout(1000);
          
          await expect(page1Button).toHaveClass(/bg-indigo-50/);
        }
      } else {
        console.log('Only one page available - skipping page navigation test');
      }
      
      await page.screenshot({ path: 'test-results/catalog-page-navigation.png' });
    });

    test('should disable navigation buttons appropriately', async ({ page }) => {
      await page.waitForTimeout(2000);
      
      // On first page, previous button should be disabled
      const prevButton = page.locator('button').filter({ hasText: /Předchozí|Previous/ }).or(
        page.locator('button[aria-label*="Předchozí"]')
      ).first();
      
      if (await prevButton.isVisible()) {
        await expect(prevButton).toBeDisabled();
      }
      
      // Check if there are multiple pages
      const pageButtons = page.locator('[aria-label="Pagination"] button').filter({ hasText: /^\\d+$/ });
      const pageButtonCount = await pageButtons.count();
      
      if (pageButtonCount <= 1) {
        // If only one page, next button should be disabled
        const nextButton = page.locator('button').filter({ hasText: /Další|Next/ }).or(
          page.locator('button[aria-label*="Další"]')
        ).first();
        
        if (await nextButton.isVisible()) {
          await expect(nextButton).toBeDisabled();
        }
      }
      
      await page.screenshot({ path: 'test-results/catalog-navigation-disabled.png' });
    });
  });

  test.describe('Pagination and Sorting Integration', () => {
    test('should reset to page 1 when sorting changes', async ({ page }) => {
      await page.waitForTimeout(2000);
      
      // Set small page size and go to page 2 if possible
      const pageSizeSelect = page.locator('select#pageSize');
      await pageSizeSelect.selectOption('10');
      await page.waitForTimeout(1000);
      
      const page2Button = page.locator('[aria-label="Pagination"] button:has-text("2")');
      if (await page2Button.isVisible()) {
        await page2Button.click();
        await page.waitForTimeout(1000);
        
        // Verify we're on page 2
        await expect(page2Button).toHaveClass(/bg-indigo-50/);
        
        // Change sorting
        const productCodeHeader = page.locator('th:has-text("Kód produktu")');
        await productCodeHeader.click();
        await page.waitForTimeout(1000);
        
        // Should be back on page 1
        const page1Button = page.locator('[aria-label="Pagination"] button:has-text("1")');
        await expect(page1Button).toHaveClass(/bg-indigo-50/);
      }
      
      await page.screenshot({ path: 'test-results/catalog-sort-resets-pagination.png' });
    });

    test('should reset to page 1 when filters change', async ({ page }) => {
      await page.waitForTimeout(2000);
      
      // Set small page size and go to page 2 if possible
      const pageSizeSelect = page.locator('select#pageSize');
      await pageSizeSelect.selectOption('10');
      await page.waitForTimeout(1000);
      
      const page2Button = page.locator('[aria-label="Pagination"] button:has-text("2")');
      if (await page2Button.isVisible()) {
        await page2Button.click();
        await page.waitForTimeout(1000);
        
        // Apply a filter
        const nameFilter = page.locator('input[placeholder*="název"]');
        await nameFilter.fill('test');
        await page.waitForTimeout(1000);
        
        // Should be back on page 1
        const page1Button = page.locator('[aria-label="Pagination"] button:has-text("1")');
        if (await page1Button.isVisible()) {
          await expect(page1Button).toHaveClass(/bg-indigo-50/);
        }
      }
      
      await page.screenshot({ path: 'test-results/catalog-filter-resets-pagination.png' });
    });
  });

  test.describe('Data Consistency Tests', () => {
    test('should maintain correct total count during operations', async ({ page }) => {
      await page.waitForTimeout(2000);
      
      // Get initial pagination info
      const paginationInfo = page.locator('text=/Zobrazeno.*z.*výsledků/');
      
      if (await paginationInfo.isVisible()) {
        const initialInfo = await paginationInfo.textContent();
        console.log('Initial pagination info:', initialInfo);
        
        // Extract total count from text like "Zobrazeno 1 až 20 z 100 výsledků"
        const totalMatch = initialInfo?.match(/z (\\d+) výsledků/);
        const initialTotal = totalMatch ? parseInt(totalMatch[1]) : 0;
        
        // Apply sorting - total should remain the same
        const productCodeHeader = page.locator('th:has-text("Kód produktu")');
        await productCodeHeader.click();
        await page.waitForTimeout(1000);
        
        const afterSortInfo = await paginationInfo.textContent();
        const afterSortMatch = afterSortInfo?.match(/z (\\d+) výsledků/);
        const afterSortTotal = afterSortMatch ? parseInt(afterSortMatch[1]) : 0;
        
        expect(afterSortTotal).toBe(initialTotal);
        console.log('After sort - total should be same:', { initialTotal, afterSortTotal });
        
        // Change page size - total should remain the same
        const pageSizeSelect = page.locator('select#pageSize');
        await pageSizeSelect.selectOption('50');
        await page.waitForTimeout(1000);
        
        const afterPageSizeInfo = await paginationInfo.textContent();
        const afterPageSizeMatch = afterPageSizeInfo?.match(/z (\\d+) výsledků/);
        const afterPageSizeTotal = afterPageSizeMatch ? parseInt(afterPageSizeMatch[1]) : 0;
        
        expect(afterPageSizeTotal).toBe(initialTotal);
        console.log('After page size change - total should be same:', { initialTotal, afterPageSizeTotal });
      }
      
      await page.screenshot({ path: 'test-results/catalog-total-count-consistency.png' });
    });

    test('should show correct item ranges in pagination info', async ({ page }) => {
      await page.waitForTimeout(2000);
      
      // Set page size to 10
      const pageSizeSelect = page.locator('select#pageSize');
      await pageSizeSelect.selectOption('10');
      await page.waitForTimeout(1000);
      
      const paginationInfo = page.locator('text=/Zobrazeno.*z.*výsledků/');
      
      if (await paginationInfo.isVisible()) {
        // On page 1, should show "Zobrazeno 1 až 10 z X výsledků"
        const page1Info = await paginationInfo.textContent();
        console.log('Page 1 info:', page1Info);
        
        // Should start with 1
        expect(page1Info).toMatch(/Zobrazeno 1 až/);
        
        // Go to page 2 if possible
        const page2Button = page.locator('[aria-label="Pagination"] button:has-text("2")');
        if (await page2Button.isVisible()) {
          await page2Button.click();
          await page.waitForTimeout(1000);
          
          const page2Info = await paginationInfo.textContent();
          console.log('Page 2 info:', page2Info);
          
          // Should start with 11 (if there are enough items)
          expect(page2Info).toMatch(/Zobrazeno 1[1-9]/);
        }
      }
      
      await page.screenshot({ path: 'test-results/catalog-item-ranges.png' });
    });
  });
});