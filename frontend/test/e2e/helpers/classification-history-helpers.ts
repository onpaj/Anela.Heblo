import { Page, Locator, expect } from '@playwright/test';

/**
 * Waits for the classification history page to be fully loaded
 */
export async function waitForClassificationHistoryLoaded(page: Page): Promise<void> {
  // Wait for page header
  await page.waitForSelector('h1:has-text("Klasifikace faktur")', { timeout: 15000 });

  // Wait for content - table, no records message, or error message
  await page.waitForSelector(
    'table, :text("Nebyly nalezeny žádné záznamy"), :text("Načítání"), :text("Loading"), :text("Error"), :text("Chyba")',
    { timeout: 10000 }
  );
}

/**
 * Gets filter input elements
 */
export function getFilterInputs(page: Page) {
  return {
    fromDate: page.locator('input#fromDate'),
    toDate: page.locator('input#toDate'),
    invoiceNumber: page.locator('input[placeholder*="Číslo faktury"]'),
    companyName: page.locator('input[placeholder*="Název firmy"]'),
    filterButton: page.locator('button:has-text("Filtrovat")'),
    clearButton: page.locator('button:has-text("Vymazat")'),
  };
}

/**
 * Applies filters by filling inputs and clicking filter button
 */
export async function applyFilters(
  page: Page,
  filters: {
    fromDate?: string;
    toDate?: string;
    invoiceNumber?: string;
    companyName?: string;
  }
): Promise<void> {
  const inputs = getFilterInputs(page);

  if (filters.fromDate) {
    await inputs.fromDate.fill(filters.fromDate);
  }
  if (filters.toDate) {
    await inputs.toDate.fill(filters.toDate);
  }
  if (filters.invoiceNumber) {
    await inputs.invoiceNumber.fill(filters.invoiceNumber);
  }
  if (filters.companyName) {
    await inputs.companyName.fill(filters.companyName);
  }

  await inputs.filterButton.click();
  await page.waitForTimeout(1000); // Wait for filter application
}

/**
 * Clears all filters
 */
export async function clearAllFilters(page: Page): Promise<void> {
  const inputs = getFilterInputs(page);
  await inputs.clearButton.click();
  await page.waitForTimeout(1000); // Wait for clear to complete
}

/**
 * Gets the data table
 */
export function getDataTable(page: Page): Locator {
  return page.locator('table');
}

/**
 * Gets table rows (excluding header)
 */
export function getTableRows(page: Page): Locator {
  return page.locator('table tbody tr');
}

/**
 * Gets the row count
 */
export async function getRowCount(page: Page): Promise<number> {
  const rows = getTableRows(page);
  return await rows.count();
}

/**
 * Checks if the page shows "no records" message
 */
export async function hasNoRecordsMessage(page: Page): Promise<boolean> {
  const noRecords = page.locator(':text("Nebyly nalezeny žádné záznamy")');
  return (await noRecords.count()) > 0;
}

/**
 * Gets pagination controls
 */
export function getPaginationControls(page: Page) {
  const nav = page.locator('nav[aria-label="Pagination"]');
  return {
    nav,
    prevButton: nav.locator('button').first(),
    nextButton: nav.locator('button').last(),
    currentPageButton: nav.locator('button.z-10, button.bg-indigo-50'),
    pageSizeSelector: page.locator('select').filter({ hasText: /10|20|50|100/ }),
  };
}

/**
 * Gets action buttons in a table row
 */
export function getRowActionButtons(row: Locator) {
  return {
    classifyButton: row.locator('button:has-text("Klasifikovat")'),
    createRuleButton: row.locator('button:has-text("Vytvořit pravidlo")'),
  };
}

/**
 * Gets the rule creation modal
 */
export function getRuleModal(page: Page) {
  const modal = page.locator('.fixed.inset-0.bg-gray-600');
  return {
    modal,
    companyNameInput: modal.locator('input[id*="company"], input[placeholder*="Firma"]'),
    submitButton: modal.locator('button:has-text("Uložit"), button:has-text("Vytvořit")'),
    cancelButton: modal.locator('button:has-text("Zrušit")'),
  };
}

/**
 * Gets status badge elements
 */
export function getStatusBadges(page: Page) {
  return {
    successBadge: page.locator('.bg-emerald-100'),
    manualReviewBadge: page.locator('.bg-yellow-100'),
    errorBadge: page.locator('.bg-red-100'),
  };
}
