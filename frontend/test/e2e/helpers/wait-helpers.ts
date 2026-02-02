import { Page, expect } from '@playwright/test';

/**
 * Wait helpers to replace arbitrary `waitForTimeout` calls with explicit condition waits.
 * These helpers improve test reliability and speed by waiting for actual conditions
 * instead of fixed time delays.
 */

/**
 * Wait for table to be loaded and visible with data.
 * Useful after navigation or filter application.
 */
export async function waitForTableLoad(page: Page, options: { minRows?: number; timeout?: number } = {}) {
  const { minRows = 1, timeout = 5000 } = options;

  // Wait for table to be visible
  await expect(page.locator('table')).toBeVisible({ timeout });

  // Wait for at least the minimum number of rows
  if (minRows > 0) {
    await expect(page.locator('tbody tr')).toHaveCount(minRows, { timeout });
  }
}

/**
 * Wait for filter to be applied by checking for API response.
 * More reliable than waiting for table update alone.
 */
export async function waitForFilterApply(page: Page, options: { endpoint?: string; timeout?: number } = {}) {
  const { endpoint = '/api/catalog', timeout = 15000 } = options;

  await page.waitForResponse(
    resp => resp.url().includes(endpoint) && resp.status() === 200,
    { timeout }
  );
}

/**
 * Wait for page navigation to complete and content to be visible.
 * Replaces: await page.goto(...); await page.waitForTimeout(3000);
 */
export async function waitForPageLoad(page: Page, options: { headingText?: string; timeout?: number } = {}) {
  const { headingText, timeout = 5000 } = options;

  // Wait for DOM to be loaded
  await page.waitForLoadState('domcontentloaded', { timeout });

  // If heading text is provided, wait for it
  if (headingText) {
    await expect(page.locator('h1')).toContainText(headingText, { timeout });
  }

  // Wait for main content to be visible
  await expect(page.locator('main, [role="main"]')).toBeVisible({ timeout });
}

/**
 * Wait for mutation operation (create, update, delete) to complete.
 * Looks for success toast/alert message.
 */
export async function waitForMutationSuccess(page: Page, options: { successText?: string; timeout?: number } = {}) {
  const { successText = 'Uloženo', timeout = 5000 } = options;

  // Wait for success alert/toast
  await expect(page.locator('[role="alert"], .toast, .notification')).toContainText(successText, { timeout });
}

/**
 * Wait for a specific element to be visible.
 * Generic helper for common wait scenarios.
 */
export async function waitForElement(page: Page, selector: string, options: { timeout?: number } = {}) {
  const { timeout = 5000 } = options;
  await expect(page.locator(selector)).toBeVisible({ timeout });
}

/**
 * Wait for loading indicator to disappear.
 * Useful when page has a loading spinner or skeleton.
 */
export async function waitForLoadingComplete(page: Page, options: { timeout?: number } = {}) {
  const { timeout = 10000 } = options;

  // Check if loading indicator exists
  const loadingIndicator = page.locator('[data-loading="true"], .loading, .spinner, [aria-busy="true"]');
  const count = await loadingIndicator.count();

  if (count > 0) {
    // Wait for it to disappear
    await expect(loadingIndicator).toHaveCount(0, { timeout });
  }
}

/**
 * Wait for search/filter results to update.
 * Combines API wait with table update check.
 */
export async function waitForSearchResults(page: Page, options: { endpoint?: string; timeout?: number } = {}) {
  const { endpoint = '/api/', timeout = 15000 } = options;

  // Wait for API response
  await page.waitForResponse(
    resp => resp.url().includes(endpoint) && resp.status() === 200,
    { timeout }
  );

  // Wait for table to update (loading state to finish)
  await waitForLoadingComplete(page, { timeout });
}

/**
 * Wait for dialog/modal to appear.
 */
export async function waitForDialog(page: Page, options: { dialogTitle?: string; timeout?: number } = {}) {
  const { dialogTitle, timeout = 5000 } = options;

  // Wait for dialog to be visible
  await expect(page.locator('[role="dialog"], .modal, .dialog')).toBeVisible({ timeout });

  // If title is provided, wait for it
  if (dialogTitle) {
    await expect(page.locator('[role="dialog"] h2, .modal-title, .dialog-title')).toContainText(dialogTitle, { timeout });
  }
}

/**
 * Wait for dropdown/combobox options to appear.
 */
export async function waitForDropdownOptions(page: Page, options: { minOptions?: number; timeout?: number } = {}) {
  const { minOptions = 1, timeout = 5000 } = options;

  // Wait for dropdown listbox to be visible
  await expect(page.locator('[role="listbox"], [role="menu"]')).toBeVisible({ timeout });

  // Wait for minimum number of options
  if (minOptions > 0) {
    await expect(page.locator('[role="option"], [role="menuitem"]')).toHaveCount(minOptions, { timeout });
  }
}

/**
 * Wait for form submission to complete.
 * Checks for button to be re-enabled or success message.
 */
export async function waitForFormSubmit(page: Page, options: { submitButton?: string; timeout?: number } = {}) {
  const { submitButton = 'button[type="submit"]', timeout = 5000 } = options;

  // Wait for submit button to be re-enabled (common pattern)
  await expect(page.locator(submitButton)).toBeEnabled({ timeout });
}

/**
 * Wait for data to be saved/persisted.
 * Combines form submission wait with success message check.
 */
export async function waitForDataSave(page: Page, options: { successText?: string; timeout?: number } = {}) {
  const { successText = 'Uloženo', timeout = 5000 } = options;

  // Wait for either success message or form to be re-enabled
  try {
    await waitForMutationSuccess(page, { successText, timeout });
  } catch {
    // If no success message, just wait for loading to complete
    await waitForLoadingComplete(page, { timeout });
  }
}
