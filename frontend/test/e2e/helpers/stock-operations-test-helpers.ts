import { Page, Locator, expect } from '@playwright/test';

/**
 * Wait for stock operations table to update after filter/sort changes
 */
export async function waitForTableUpdate(page: Page): Promise<void> {
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(2000);
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
  return page.getByRole('button', { name: /Použít filtry/i });
}

/**
 * Get clear filters button
 */
export function getClearFiltersButton(page: Page): Locator {
  return page.getByRole('button', { name: /Vymazat filtry/i });
}

/**
 * Get refresh button
 */
export function getRefreshButton(page: Page): Locator {
  return page.getByRole('button', { name: /Obnovit/i });
}

/**
 * Get filter panel collapse/expand button
 */
export function getFilterPanelToggle(page: Page): Locator {
  return page.locator('button').filter({ hasText: 'Filtry a nastavení' });
}

/**
 * Select state filter
 */
export async function selectStateFilter(
  page: Page,
  state: 'All' | 'Active' | 'Pending' | 'Submitted' | 'Failed' | 'Completed'
): Promise<void> {
  const select = getStateFilterSelect(page);
  await select.selectOption({ label: state === 'All' ? 'Všechny' : state });
  await waitForTableUpdate(page);
}

/**
 * Select source type filter
 */
export async function selectSourceType(
  page: Page,
  type: 'All' | 'TransportBox' | 'GiftPackageManufacture'
): Promise<void> {
  const radio = getSourceTypeRadio(page, type);
  await radio.click();
  await waitForTableUpdate(page);
}

/**
 * Apply filters by clicking apply button
 */
export async function applyFilters(page: Page): Promise<void> {
  const button = getApplyFiltersButton(page);
  await button.click();
  await waitForTableUpdate(page);
}

/**
 * Clear all filters
 */
export async function clearFilters(page: Page): Promise<void> {
  const button = getClearFiltersButton(page);
  await button.click();
  await waitForTableUpdate(page);
}

/**
 * Toggle filter panel collapse/expand
 */
export async function toggleFilterPanel(page: Page): Promise<void> {
  const button = getFilterPanelToggle(page);
  await button.click();
  await page.waitForTimeout(500);
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
  const badge = row.locator('span.inline-flex.items-center.px-2\\.5');

  const stateColorMap = {
    Completed: 'bg-green-100',
    Failed: 'bg-red-100',
    Pending: 'bg-yellow-100',
    Submitted: 'bg-blue-100',
  };

  await expect(badge).toHaveClass(new RegExp(stateColorMap[state]));
  console.log(`   ✅ Row ${rowIndex}: ${state} badge validated`);
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
  console.log(`   ✅ Row ${rowIndex}: ${state} retry button validated`);
}

/**
 * Validate no retry button exists for Completed operations
 */
export async function validateNoRetryButton(page: Page, rowIndex: number = 0): Promise<void> {
  const row = page.locator('tbody tr').nth(rowIndex);
  const retryButton = row.locator('button').filter({ hasText: /Opakovat|Znovu zkusit|Spustit/i });

  await expect(retryButton).not.toBeVisible();
  console.log(`   ✅ Row ${rowIndex}: No retry button (as expected)`);
}

/**
 * Validate stuck operation warning (AlertTriangle with pulse)
 */
export async function validateStuckWarning(page: Page, rowIndex: number = 0): Promise<void> {
  const row = page.locator('tbody tr').nth(rowIndex);
  const alertIcon = row.locator('svg').filter({ hasText: '' }).first();

  await expect(alertIcon).toBeVisible();
  await expect(alertIcon).toHaveClass(/animate-pulse/);
  console.log(`   ✅ Row ${rowIndex}: Stuck warning validated`);
}

/**
 * Sort by column name
 */
export async function sortByColumn(page: Page, columnName: string): Promise<void> {
  const header = page.getByRole('columnheader', { name: new RegExp(columnName, 'i') });
  await header.click();
  await waitForTableUpdate(page);
}

/**
 * Get sort icon for a column (ChevronUp or ChevronDown)
 */
export async function getSortIcon(page: Page, columnName: string): Promise<string | null> {
  const header = page.getByRole('columnheader', { name: new RegExp(columnName, 'i') });
  const chevronDown = await header.locator('svg').filter({ hasText: '' }).count();
  const chevronUp = await header.locator('svg').filter({ hasText: '' }).count();

  if (chevronDown > 0) return 'descending';
  if (chevronUp > 0) return 'ascending';
  return null;
}
