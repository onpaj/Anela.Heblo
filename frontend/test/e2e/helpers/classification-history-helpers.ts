import { Page, Locator } from '@playwright/test';
import { navigateToInvoiceClassification } from './e2e-auth-helper';

/**
 * Navigates to the Classification History page
 * This is an alias for navigateToInvoiceClassification for backwards compatibility
 */
export async function navigateToClassificationHistory(page: Page): Promise<void> {
  await navigateToInvoiceClassification(page);
  await waitForClassificationHistoryLoaded(page);
}

/**
 * Waits for the classification history page to be fully loaded
 */
export async function waitForClassificationHistoryLoaded(page: Page): Promise<void> {
  // Wait for page header
  await page.waitForSelector('h1:has-text("Klasifikace faktur")', { timeout: 15000 });

  // Wait for table to appear
  await page.waitForSelector('table', { timeout: 10000 });

  // Wait for either data rows to load OR no-records message to appear
  // This ensures we don't proceed until the table is fully populated
  await page.waitForSelector(
    'table tbody tr, :text("Nebyly nalezeny žádné záznamy")',
    { timeout: 15000 }
  );
}

/**
 * Gets filter input elements
 */
export function getFilterInputs(page: Page): {
  fromDate: Locator;
  toDate: Locator;
  invoiceNumber: Locator;
  companyName: Locator;
  filterButton: Locator;
  clearButton: Locator;
} {
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
  await page.waitForLoadState('networkidle'); // Wait for filter application
}

/**
 * Clears all filters
 */
export async function clearAllFilters(page: Page): Promise<void> {
  const inputs = getFilterInputs(page);
  await inputs.clearButton.click();
  await page.waitForLoadState('networkidle'); // Wait for clear to complete
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
export function getPaginationControls(page: Page): {
  nav: Locator;
  prevButton: Locator;
  nextButton: Locator;
  currentPageButton: Locator;
  pageSizeSelector: Locator;
} {
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
export function getRowActionButtons(row: Locator): {
  classifyButton: Locator;
  createRuleButton: Locator;
} {
  return {
    classifyButton: row.locator('button:has-text("Klasifikovat")'),
    createRuleButton: row.locator('button:has-text("Vytvořit pravidlo")'),
  };
}

/**
 * Gets the rule creation modal
 */
export function getRuleModal(page: Page): {
  modal: Locator;
  companyNameInput: Locator;
  submitButton: Locator;
  cancelButton: Locator;
} {
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
export function getStatusBadges(page: Page): {
  successBadge: Locator;
  manualReviewBadge: Locator;
  errorBadge: Locator;
} {
  return {
    successBadge: page.locator('.bg-emerald-100'),
    manualReviewBadge: page.locator('.bg-yellow-100'),
    errorBadge: page.locator('.bg-red-100'),
  };
}

/**
 * Clicks the Classify button in the first table row
 */
export async function clickFirstRowClassifyButton(page: Page): Promise<void> {
  const classifyButton = page
    .locator('table tbody tr')
    .first()
    .locator('button:has-text("Klassifizieren"), button:has-text("Klasifikovat")');

  await classifyButton.click();
}

/**
 * Opens the Classify Invoice modal by clicking the first row's classify button
 */
export async function openClassifyInvoiceModal(page: Page): Promise<void> {
  await clickFirstRowClassifyButton(page);
  await page.waitForSelector('div[role="dialog"]', { state: 'visible', timeout: 10000 });
}

/**
 * Gets the title text of the Classify Invoice modal
 */
export async function getClassifyInvoiceModalTitle(page: Page): Promise<string> {
  const titleElement = page.locator('div[role="dialog"] h2, div[role="dialog"] h3');
  return await titleElement.textContent() || '';
}

/**
 * Clicks the Cancel button in the Classify Invoice modal
 */
export async function clickClassifyInvoiceCancel(page: Page): Promise<void> {
  const cancelButton = page.locator('div[role="dialog"] button:has-text("Zrušit"), div[role="dialog"] button:has-text("Abbrechen")');
  await cancelButton.click();
}

/**
 * Clicks the Save button in the Classify Invoice modal
 */
export async function clickClassifyInvoiceSave(page: Page): Promise<void> {
  const saveButton = page.locator('div[role="dialog"] button:has-text("Uložit"), div[role="dialog"] button:has-text("Speichern")');
  await saveButton.click();
}

/**
 * Selects a rule type in the classification modal
 */
export async function selectClassificationRuleType(page: Page, ruleType: string): Promise<void> {
  const ruleTypeSelect = page.locator('div[role="dialog"] select[name="ruleType"], div[role="dialog"] select#ruleType');
  await ruleTypeSelect.selectOption({ label: ruleType });
}

/**
 * Selects an accounting template by index in the classification modal
 */
export async function selectClassificationAccountingTemplate(page: Page, index: number): Promise<void> {
  const templateSelect = page.locator('div[role="dialog"] select[name="accountingTemplate"], div[role="dialog"] select#accountingTemplate');
  const options = await templateSelect.locator('option').all();

  if (index >= options.length) {
    throw new Error(`Index ${index} out of bounds for accounting template options (${options.length} available)`);
  }

  const optionValue = await options[index].getAttribute('value');
  if (optionValue) {
    await templateSelect.selectOption(optionValue);
  }
}

/**
 * Selects a department by index in the classification modal
 */
export async function selectClassificationDepartment(page: Page, index: number): Promise<void> {
  const departmentSelect = page.locator('div[role="dialog"] select[name="department"], div[role="dialog"] select#department');
  const options = await departmentSelect.locator('option').all();

  if (index >= options.length) {
    throw new Error(`Index ${index} out of bounds for department options (${options.length} available)`);
  }

  const optionValue = await options[index].getAttribute('value');
  if (optionValue) {
    await departmentSelect.selectOption(optionValue);
  }
}

/**
 * Fills the description field in the classification modal
 */
export async function fillClassificationDescription(page: Page, description: string): Promise<void> {
  const descriptionInput = page.locator('div[role="dialog"] textarea[name="description"], div[role="dialog"] textarea#description, div[role="dialog"] input[name="description"], div[role="dialog"] input#description');
  await descriptionInput.fill(description);
}
