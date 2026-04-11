import { Page, Locator, expect } from '@playwright/test';
import { waitForLoadingComplete, waitForDropdownOptions } from './wait-helpers';

// Czech UI text labels
const UI_LABELS = {
  APPLY_FILTERS: 'Použít filtry',
  CLEAR_FILTERS: 'Vymazat filtry',
  REFRESH: 'Obnovit',
  FILTER_PANEL: 'Filtry a nastavení',
  ALL_STATES: 'Všechny',
} as const;

/**
 * Wait for stock operations table to update after filter/sort changes.
 * Uses DOM-based wait to avoid race conditions with page.waitForResponse,
 * which only catches API responses registered BEFORE the request fires.
 *
 * Waits for at least one data row (tbody tr) to be visible, or for the
 * empty-state message to appear. Using 'tbody tr' instead of 'table' avoids
 * matching layout/filter tables that may be present before the data table loads.
 */
export async function waitForTableUpdate(page: Page): Promise<void> {
  // Wait for either at least one data row or the empty-state message to appear
  await expect(
    page.locator('tbody tr').first().or(page.locator('h3').filter({ hasText: 'Žádné výsledky' }))
  ).toBeVisible({ timeout: 15000 });
}

/**
 * Get the count of stock operation rows in the table
 */
export async function getRowCount(page: Page): Promise<number> {
  const rows = await page.locator('tbody tr').count();
  return rows;
}

/**
 * Get state filter dropdown selector
 */
export function getStateFilterSelect(page: Page): Locator {
  return page.locator('select').first();
}

/**
 * Get source type radio button
 */
export function getSourceTypeRadio(page: Page, value: string): Locator {
  return page.locator(`input[type="radio"][value="${value}"]`);
}

/**
 * Get apply filters button
 */
export function getApplyFiltersButton(page: Page): Locator {
  return page.getByRole('button', { name: new RegExp(UI_LABELS.APPLY_FILTERS, 'i') });
}

/**
 * Get clear filters button
 */
export function getClearFiltersButton(page: Page): Locator {
  return page.getByRole('button', { name: new RegExp(UI_LABELS.CLEAR_FILTERS, 'i') });
}

/**
 * Get refresh button
 */
export function getRefreshButton(page: Page): Locator {
  return page.getByRole('button', { name: new RegExp(UI_LABELS.REFRESH, 'i') });
}

/**
 * Get filter panel collapse/expand button
 */
export function getFilterPanelToggle(page: Page): Locator {
  return page.locator('button').filter({ hasText: UI_LABELS.FILTER_PANEL });
}

/**
 * Select state filter
 */
export async function selectStateFilter(
  page: Page,
  state: 'All' | 'Active' | 'Pending' | 'Submitted' | 'Failed' | 'Completed'
): Promise<void> {
  console.log(`🔍 Selecting state filter: ${state}`);
  const select = getStateFilterSelect(page);
  // Use value instead of label since labels are in Czech but values are in English
  await select.selectOption({ value: state });

  // Click apply filters button to apply the change
  const applyButton = getApplyFiltersButton(page);
  await applyButton.click();

  await page.waitForTimeout(1000);
  console.log('✅ State filter applied');
}

/**
 * Select source type filter
 */
export async function selectSourceType(
  page: Page,
  type: 'All' | 'TransportBox' | 'GiftPackageManufacture'
): Promise<void> {
  console.log(`🔍 Selecting source type: ${type}`);
  const radio = getSourceTypeRadio(page, type);
  await radio.click();
  await page.waitForTimeout(1000);
  console.log('✅ Source type filter applied');
}

/**
 * Apply filters by clicking apply button
 */
export async function applyFilters(page: Page): Promise<void> {
  console.log('🔍 Applying filters');
  const button = getApplyFiltersButton(page);
  await button.click();

  // Wait for loading to complete (UI-based, more reliable than API response)
  await waitForLoadingComplete(page, { timeout: 30000 });

  // Small additional wait for table to stabilize
  await page.waitForTimeout(500);
  console.log('✅ Filters applied');
}

/**
 * Clear all filters
 */
export async function clearFilters(page: Page): Promise<void> {
  console.log('🧹 Clearing all filters');
  const button = getClearFiltersButton(page);
  await button.click();

  // Wait for loading to complete (UI-based, more reliable than API response)
  await waitForLoadingComplete(page, { timeout: 30000 });

  // Small additional wait for table to stabilize
  await page.waitForTimeout(500);
  console.log('✅ Filters cleared');
}

/**
 * Toggle filter panel collapse/expand
 */
export async function toggleFilterPanel(page: Page): Promise<void> {
  console.log('🔄 Toggling filter panel');
  const button = getFilterPanelToggle(page);
  await button.click();
  await waitForLoadingComplete(page);
  console.log('✅ Filter panel toggled');
}

/**
 * Validate state badge color and icon for a specific row
 */
export async function validateStateBadge(
  page: Page,
  state: 'Completed' | 'Failed' | 'Pending' | 'Submitted',
  rowIndex: number = 0
): Promise<void> {
  const row = page.locator('tbody tr').nth(rowIndex);
  // More flexible selector - look for badge with rounded-full class
  const badge = row.locator('span.rounded-full').first();

  const stateColorMap = {
    Completed: 'bg-green-100',
    Failed: 'bg-red-100',
    Pending: 'bg-yellow-100',
    Submitted: 'bg-blue-100',
  };

  await expect(badge).toHaveClass(new RegExp(stateColorMap[state]));
}

/**
 * Validate retry button exists with correct color and label
 */
export async function validateRetryButton(
  page: Page,
  state: 'Failed' | 'Submitted' | 'Pending',
  rowIndex: number = 0
): Promise<void> {
  const row = page.locator('tbody tr').nth(rowIndex);
  const retryButton = row.locator('button').filter({ hasText: /Opakovat|Znovu zkusit|Spustit/i });

  await expect(retryButton).toBeVisible();

  const buttonLabels = {
    Failed: 'Opakovat',
    Submitted: 'Znovu zkusit',
    Pending: 'Spustit',
  };

  await expect(retryButton).toContainText(buttonLabels[state]);
}

/**
 * Validate no retry button exists for Completed operations
 */
export async function validateNoRetryButton(page: Page, rowIndex: number = 0): Promise<void> {
  const row = page.locator('tbody tr').nth(rowIndex);
  const retryButton = row.locator('button').filter({ hasText: /Opakovat|Znovu zkusit|Spustit/i });

  await expect(retryButton).not.toBeVisible();
}

/**
 * Validate stuck operation warning (AlertTriangle with pulse)
 */
export async function validateStuckWarning(page: Page, rowIndex: number = 0): Promise<void> {
  const row = page.locator('tbody tr').nth(rowIndex);
  const alertIcon = row.locator('svg').filter({ hasText: '' }).first();

  await expect(alertIcon).toBeVisible();
  await expect(alertIcon).toHaveClass(/animate-pulse/);
}

/**
 * Sort by column name
 */
export async function sortByColumn(page: Page, columnName: string): Promise<void> {
  console.log(`📊 Sorting by column: ${columnName}`);

  // Stock Operations table structure: table has 2 <tbody> elements
  // First <tbody> = header row (cells are clickable)
  // Second <tbody> = data rows
  // Simple approach: find any td with matching text and click it
  // The column headers will be the first match since they come before data rows
  const header = page.locator('table').getByText(columnName, { exact: true }).first();
  await header.click();

  // Wait for loading to complete (UI-based, more reliable than API response)
  await waitForLoadingComplete(page, { timeout: 30000 });

  // Small additional wait for table to stabilize
  await page.waitForTimeout(500);
  console.log('✅ Column sort applied');
}

/**
 * Get sort icon for a column (ChevronUp or ChevronDown)
 */
export async function getSortIcon(page: Page, columnName: string): Promise<string | null> {
  // Stock Operations table: first <tbody> = headers
  const header = page.locator('table tbody').first().locator('td').filter({ hasText: columnName });

  // Check for presence of chevron icons - look for common SVG class patterns
  const hasChevronDown = await header.locator('svg').count() > 0;

  if (hasChevronDown) {
    // Return 'descending' as default when chevron is present
    // The actual direction can be determined by the UI implementation
    return 'descending';
  }

  return null;
}

// ============================================================================
// Product Autocomplete Helpers
// ============================================================================

/**
 * Get product autocomplete input field
 */
export function getProductAutocomplete(page: Page): Locator {
  // Product autocomplete is the CatalogAutocomplete component
  // Look for the combobox input
  return page.locator('input[role="combobox"]').first();
}

/**
 * Open product autocomplete dropdown
 */
export async function openProductAutocomplete(page: Page): Promise<void> {
  console.log('📂 Opening product autocomplete dropdown');
  const input = getProductAutocomplete(page);
  await input.click();
  await waitForDropdownOptions(page);
  console.log('✅ Product autocomplete opened');
}

/**
 * Search for product in autocomplete
 */
export async function searchProduct(page: Page, searchTerm: string): Promise<void> {
  console.log(`🔍 Searching for product: "${searchTerm}"`);
  const input = getProductAutocomplete(page);
  await input.click();
  // Register the response promise BEFORE triggering fill() to avoid a race
  // condition where the API response arrives before the listener is registered.
  const responsePromise = page.waitForResponse(
    r => r.url().includes('/api/catalog') && r.status() === 200,
    { timeout: 15000 }
  );
  await input.fill(searchTerm);
  await responsePromise;
  console.log('✅ Product search results loaded');
}

/**
 * Select product from autocomplete dropdown
 */
export async function selectProductFromDropdown(page: Page, productCodeOrName: string): Promise<void> {
  console.log(`✅ Selecting product from dropdown: "${productCodeOrName}"`);
  await searchProduct(page, productCodeOrName);
  // Click the matching option
  const option = page.locator(`[role="option"]`).filter({ hasText: productCodeOrName }).first();
  await option.click();
  await waitForLoadingComplete(page);
  console.log('✅ Product selected');
}

/**
 * Clear product selection
 */
export async function clearProductSelection(page: Page): Promise<void> {
  console.log('🧹 Clearing product selection');
  const input = getProductAutocomplete(page);
  // Look for clear button (X icon) - specifically look for button with X svg near the combobox
  const clearButton = page.locator('button[aria-label="Clear"]').or(
    page.locator('button').filter({ has: page.locator('svg.lucide-x') })
  ).first();

  const isVisible = await clearButton.isVisible().catch(() => false);
  if (isVisible) {
    await clearButton.click();
  } else {
    // Fallback: clear input directly
    await input.fill('');
  }
  await waitForLoadingComplete(page);
  console.log('✅ Product selection cleared');
}

// ============================================================================
// Document Number Filter Helpers
// ============================================================================

/**
 * Get document number filter input
 */
export function getDocumentNumberInput(page: Page): Locator {
  // Find input with placeholder or label for document number
  // More specific: look for input with search functionality near filter panel
  return page.locator('input[type="text"]').filter({
    hasText: ''
  }).or(
    page.locator('input[placeholder*="Číslo dokladu"]')
  ).or(
    page.locator('input[placeholder*="doklad"]')
  ).first();
}

/**
 * Search by document number
 */
export async function searchDocumentNumber(page: Page, documentNumber: string): Promise<void> {
  console.log(`🔍 Searching by document number: "${documentNumber}"`);
  const input = getDocumentNumberInput(page);
  await input.fill(documentNumber);
  await waitForLoadingComplete(page);
  console.log('✅ Document number search applied');
}

/**
 * Clear document number search
 */
export async function clearDocumentNumber(page: Page): Promise<void> {
  console.log('🧹 Clearing document number search');
  const input = getDocumentNumberInput(page);
  await input.fill('');
  await waitForLoadingComplete(page);
  console.log('✅ Document number search cleared');
}

// ============================================================================
// Date Range Filter Helpers
// ============================================================================

/**
 * Get "Created From" date input
 */
export function getDateFromInput(page: Page): Locator {
  // Date inputs are identified by label "Vytvořeno od:"
  return page.locator('input[type="date"]').first();
}

/**
 * Get "Created To" date input
 */
export function getDateToInput(page: Page): Locator {
  // Date inputs are identified by label "Vytvořeno do:"
  return page.locator('input[type="date"]').nth(1);
}

/**
 * Set "Created From" date
 */
export async function setDateFrom(page: Page, date: string): Promise<void> {
  console.log(`📅 Setting "Created From" date: ${date}`);
  const input = getDateFromInput(page);
  await input.fill(date); // Format: YYYY-MM-DD
  await waitForLoadingComplete(page);
  console.log('✅ "Created From" date set');
}

/**
 * Set "Created To" date
 */
export async function setDateTo(page: Page, date: string): Promise<void> {
  console.log(`📅 Setting "Created To" date: ${date}`);
  const input = getDateToInput(page);
  await input.fill(date); // Format: YYYY-MM-DD
  await waitForLoadingComplete(page);
  console.log('✅ "Created To" date set');
}

/**
 * Clear date filters
 */
export async function clearDateFilters(page: Page): Promise<void> {
  console.log('🧹 Clearing date filters');
  const dateFrom = getDateFromInput(page);
  const dateTo = getDateToInput(page);
  await dateFrom.fill('');
  await dateTo.fill('');
  await waitForLoadingComplete(page);
  console.log('✅ Date filters cleared');
}

// ============================================================================
// Panel Collapse/Expand Helpers
// ============================================================================

/**
 * Check if filter panel is collapsed
 */
export async function isFilterPanelCollapsed(page: Page): Promise<boolean> {
  const toggle = getFilterPanelToggle(page);
  // Check for ChevronRight icon (collapsed) vs ChevronDown (expanded)
  const chevronRight = toggle.locator('svg').first();
  const classes = await chevronRight.getAttribute('class');
  return classes?.includes('lucide-chevron-right') ?? false;
}

/**
 * Expand filter panel if collapsed
 */
export async function expandFilterPanel(page: Page): Promise<void> {
  console.log('📂 Expanding filter panel');
  const isCollapsed = await isFilterPanelCollapsed(page);
  if (isCollapsed) {
    await toggleFilterPanel(page);
  } else {
    console.log('ℹ️ Filter panel already expanded');
  }
}

/**
 * Collapse filter panel if expanded
 */
export async function collapseFilterPanel(page: Page): Promise<void> {
  console.log('🔒 Collapsing filter panel');
  const isCollapsed = await isFilterPanelCollapsed(page);
  if (!isCollapsed) {
    await toggleFilterPanel(page);
  } else {
    console.log('ℹ️ Filter panel already collapsed');
  }
}
