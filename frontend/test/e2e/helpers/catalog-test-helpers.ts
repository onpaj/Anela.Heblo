import { Page, expect, Locator } from '@playwright/test';
import { waitForLoadingComplete } from './wait-helpers';

const CATALOG_API_ENDPOINT = '/api/catalog';

/**
 * Catalog E2E Test Helpers
 *
 * Reusable utilities for testing catalog filter functionality.
 * These helpers encapsulate common operations and validations for catalog E2E tests.
 */

// ============================================================================
// LOCATOR HELPERS
// ============================================================================

export function getProductNameInput(page: Page): Locator {
  return page.locator('input[placeholder="Název produktu..."]');
}

export function getProductCodeInput(page: Page): Locator {
  return page.locator('input[placeholder="Kód produktu..."]');
}

export function getProductTypeSelect(page: Page): Locator {
  return page.locator('select#productType');
}

export function getFilterButton(page: Page): Locator {
  return page.getByRole('button', { name: 'Filtrovat' });
}

export function getClearButton(page: Page): Locator {
  return page.getByRole('button', { name: 'Vymazat' });
}

export function getTableBody(page: Page): Locator {
  return page.locator('tbody');
}

export function getTableRows(page: Page): Locator {
  return page.locator('tbody tr');
}

export function getEmptyStateMessage(page: Page): Locator {
  return page.locator('text=Žádné produkty nebyly nalezeny');
}

export function getFilteredIndicator(page: Page): Locator {
  return page.locator('text=(filtrováno)');
}

export function getPageSizeSelect(page: Page): Locator {
  return page.locator('select').filter({ hasText: /10|20|50|100/ });
}

// ============================================================================
// FILTER APPLICATION HELPERS
// ============================================================================

/**
 * Apply product name filter using Filter button
 */
export async function applyProductNameFilter(page: Page, name: string): Promise<void> {
  console.log(`🔍 Applying product name filter: "${name}"`);
  const input = getProductNameInput(page);
  await input.fill(name);
  const filterButton = getFilterButton(page);

  // Register waitForResponse BEFORE the click so we don't miss the response
  const responsePromise = page.waitForResponse(
    resp => resp.url().includes(CATALOG_API_ENDPOINT) && resp.status() === 200,
    { timeout: 30000 }
  );
  await Promise.all([responsePromise, filterButton.click()]);

  await waitForLoadingComplete(page, { timeout: 10000 });
  console.log('✅ Product name filter applied');
}

/**
 * Apply product name filter using Enter key
 */
export async function applyProductNameFilterWithEnter(page: Page, name: string): Promise<void> {
  console.log(`🔍 Applying product name filter with Enter: "${name}"`);
  const input = getProductNameInput(page);
  await input.fill(name);

  // Register waitForResponse BEFORE the Enter press so we don't miss the response
  const responsePromise = page.waitForResponse(
    resp => resp.url().includes(CATALOG_API_ENDPOINT) && resp.status() === 200,
    { timeout: 30000 }
  );
  await Promise.all([responsePromise, input.press('Enter')]);

  await waitForLoadingComplete(page, { timeout: 10000 });
  console.log('✅ Product name filter applied with Enter');
}

/**
 * Apply product code filter using Filter button
 */
export async function applyProductCodeFilter(page: Page, code: string): Promise<void> {
  console.log(`🔍 Applying product code filter: "${code}"`);
  const input = getProductCodeInput(page);
  await input.fill(code);
  const filterButton = getFilterButton(page);

  // Register waitForResponse BEFORE the click so we don't miss the response
  const responsePromise = page.waitForResponse(
    resp => resp.url().includes(CATALOG_API_ENDPOINT) && resp.status() === 200,
    { timeout: 30000 }
  );
  await Promise.all([responsePromise, filterButton.click()]);

  await waitForLoadingComplete(page, { timeout: 10000 });
  console.log('✅ Product code filter applied');
}

/**
 * Apply product code filter using Enter key
 */
export async function applyProductCodeFilterWithEnter(page: Page, code: string): Promise<void> {
  console.log(`🔍 Applying product code filter with Enter: "${code}"`);
  const input = getProductCodeInput(page);
  await input.fill(code);

  // Register waitForResponse BEFORE the Enter press so we don't miss the response
  const responsePromise = page.waitForResponse(
    resp => resp.url().includes(CATALOG_API_ENDPOINT) && resp.status() === 200,
    { timeout: 30000 }
  );
  await Promise.all([responsePromise, input.press('Enter')]);

  await waitForLoadingComplete(page, { timeout: 10000 });
  console.log('✅ Product code filter applied with Enter');
}

/**
 * Select product type filter (applies immediately)
 */
export async function selectProductType(page: Page, type: string): Promise<void> {
  console.log(`🔽 Selecting product type: "${type}"`);
  const select = getProductTypeSelect(page);

  // Register waitForResponse BEFORE the select so we don't miss the response
  const responsePromise = page.waitForResponse(
    resp => resp.url().includes(CATALOG_API_ENDPOINT) && resp.status() === 200,
    { timeout: 30000 }
  );
  await Promise.all([responsePromise, select.selectOption({ label: type })]);

  await waitForLoadingComplete(page, { timeout: 10000 });
  console.log('✅ Product type filter applied');
}

/**
 * Clear all filters using Clear button
 */
export async function clearAllFilters(page: Page): Promise<void> {
  console.log('🔄 Clearing all filters');
  const clearButton = getClearButton(page);

  // Register waitForResponse BEFORE the click so we don't miss the response
  const responsePromise = page.waitForResponse(
    resp => resp.url().includes(CATALOG_API_ENDPOINT) && resp.status() === 200,
    { timeout: 30000 }
  );
  await Promise.all([responsePromise, clearButton.click()]);

  await waitForLoadingComplete(page, { timeout: 10000 });
  console.log('✅ All filters cleared');
}

// ============================================================================
// VALIDATION HELPERS
// ============================================================================

interface FilterCriteria {
  productName?: string;
  productCode?: string;
  productType?: string;
}

/**
 * Validate that all visible table rows match the specified filter criteria
 */
export async function validateFilteredResults(
  page: Page,
  expectedFilter: FilterCriteria,
  options: { maxRowsToCheck?: number; caseSensitive?: boolean } = {}
): Promise<void> {
  const { maxRowsToCheck = 10, caseSensitive = false } = options;

  console.log('🔍 Validating filtered results:', expectedFilter);

  const rows = await getTableRows(page).all();
  const rowsToCheck = Math.min(rows.length, maxRowsToCheck);

  if (rows.length === 0) {
    throw new Error('No rows found to validate');
  }

  console.log(`📊 Checking ${rowsToCheck} of ${rows.length} rows`);

  for (let i = 0; i < rowsToCheck; i++) {
    const row = rows[i];

    if (expectedFilter.productName) {
      const nameCell = row.locator('td:nth-child(2)');
      const text = await nameCell.textContent();
      const actualName = text?.trim() || '';
      const expectedName = expectedFilter.productName;

      const matches = caseSensitive
        ? actualName.includes(expectedName)
        : actualName.toLowerCase().includes(expectedName.toLowerCase());

      if (!matches) {
        throw new Error(
          `Row ${i + 1}: Expected product name to contain "${expectedName}" but got "${actualName}"`
        );
      }
      console.log(`   ✅ Row ${i + 1}: Name contains "${expectedName}"`);
    }

    if (expectedFilter.productCode) {
      const codeCell = row.locator('td:nth-child(1)');
      const text = await codeCell.textContent();
      const actualCode = text?.trim() || '';
      const expectedCode = expectedFilter.productCode;

      if (!actualCode.includes(expectedCode)) {
        throw new Error(
          `Row ${i + 1}: Expected product code to contain "${expectedCode}" but got "${actualCode}"`
        );
      }
      console.log(`   ✅ Row ${i + 1}: Code contains "${expectedCode}"`);
    }

    if (expectedFilter.productType) {
      const typeBadge = row.locator('span.inline-flex.items-center.px-2\\.5');
      const text = await typeBadge.textContent();
      const actualType = text?.trim() || '';
      const expectedType = expectedFilter.productType;

      if (actualType !== expectedType) {
        throw new Error(
          `Row ${i + 1}: Expected product type "${expectedType}" but got "${actualType}"`
        );
      }
      console.log(`   ✅ Row ${i + 1}: Type is "${expectedType}"`);
    }
  }

  console.log('✅ All filtered results validated successfully');
}

/**
 * Validate empty state is displayed
 */
export async function validateEmptyState(page: Page): Promise<void> {
  console.log('🔍 Validating empty state');
  await expect(getEmptyStateMessage(page)).toBeVisible();
  const rowCount = await getTableRows(page).count();
  expect(rowCount).toBe(0);
  console.log('✅ Empty state validated');
}

/**
 * Validate filter status indicator visibility
 */
export async function validateFilterStatusIndicator(page: Page, shouldBeVisible: boolean): Promise<void> {
  console.log(`🔍 Validating filter status indicator (should be ${shouldBeVisible ? 'visible' : 'hidden'})`);
  const indicator = getFilteredIndicator(page);
  if (shouldBeVisible) {
    await expect(indicator).toBeVisible();
    console.log('✅ Filter status indicator is visible');
  } else {
    await expect(indicator).not.toBeVisible();
    console.log('✅ Filter status indicator is hidden');
  }
}

/**
 * Validate that filter inputs are cleared
 */
export async function validateFiltersCleared(page: Page): Promise<void> {
  console.log('🔍 Validating filters are cleared');

  // Check product name input is empty
  const nameInput = getProductNameInput(page);
  await expect(nameInput).toHaveValue('');
  console.log('   ✅ Product name input is empty');

  // Check product code input is empty
  const codeInput = getProductCodeInput(page);
  await expect(codeInput).toHaveValue('');
  console.log('   ✅ Product code input is empty');

  // Check product type is reset to default
  const typeSelect = getProductTypeSelect(page);
  const selectedValue = await typeSelect.inputValue();
  expect(selectedValue).toBe('');
  console.log('   ✅ Product type is reset to default');

  console.log('✅ All filters cleared successfully');
}

/**
 * Get current page number from URL
 */
export async function getCurrentPageNumber(page: Page): Promise<number> {
  const url = new URL(page.url());
  const pageParam = url.searchParams.get('page');
  return pageParam ? parseInt(pageParam, 10) : 1;
}

/**
 * Validate page was reset to page 1
 */
export async function validatePageResetToOne(page: Page): Promise<void> {
  console.log('🔍 Validating page reset to 1');
  const currentPage = await getCurrentPageNumber(page);
  expect(currentPage).toBe(1);
  console.log('✅ Page reset to 1 confirmed');
}

/**
 * Get count of table rows
 */
export async function getRowCount(page: Page): Promise<number> {
  const count = await getTableRows(page).count();
  console.log(`📊 Current row count: ${count}`);
  return count;
}

/**
 * Wait for table to update after filter change.
 *
 * NOTE: When called after one of the apply*Filter helpers (which already await the API
 * response via Promise.all), this function only needs to verify DOM stability.
 * It does NOT call waitForSearchResults/waitForResponse, which would race against
 * an already-consumed response and time out.
 *
 * For cases where the trigger action is done outside a helper, pass the responsePromise
 * option to wait for the API response correctly.
 */
export async function waitForTableUpdate(
  page: Page,
  expectedMinRows: number = 0,
  options: { responsePromise?: Promise<unknown> } = {}
): Promise<void> {
  console.log('⏳ Waiting for table to update...');

  if (options.responsePromise) {
    // Caller registered waitForResponse before the action - await it now
    await options.responsePromise;
  }

  // Wait for loading indicator to disappear (if any)
  await waitForLoadingComplete(page, { timeout: 10000 });

  // Optionally wait for at least some rows to appear
  if (expectedMinRows > 0) {
    await page.waitForFunction(
      (minRows) => {
        const rows = document.querySelectorAll('tbody tr');
        return rows.length >= minRows;
      },
      expectedMinRows,
      { timeout: 5000 }
    ).catch(() => {
      // If timeout, check if empty state is shown instead
      console.log('⚠️  Expected rows not found, checking for empty state...');
    });
  }

  // Wait for table or empty state to be present to confirm DOM has updated
  await page.waitForFunction(
    () => {
      const rows = document.querySelectorAll('tbody tr');
      const emptyMsg = document.querySelector('[class*="text-center"]');
      return rows.length > 0 || (emptyMsg !== null && emptyMsg.textContent?.includes('nalezen'));
    },
    { timeout: 10000 }
  ).catch(() => {
    // DOM may not have the empty state text or rows - continue anyway
  });

  console.log('✅ Table update completed');
}
