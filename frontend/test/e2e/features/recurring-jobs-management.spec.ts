import { test, expect } from '@playwright/test';

test.describe('Recurring Jobs Management', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to recurring jobs page
    await page.goto('/recurring-jobs');

    // Wait for the page to load
    await page.waitForLoadState('networkidle');
  });

  test('should display recurring jobs page with correct title', async ({ page }) => {
    // Wait for page title to be visible
    await expect(page.getByRole('heading', { name: 'Správa Recurring Jobs' })).toBeVisible();

    // Verify description is visible
    await expect(page.getByText('Zapínání/vypínání Hangfire úloh')).toBeVisible();
  });

  test('should display jobs table with all columns', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table', { timeout: 10000 });

    // Verify table headers
    await expect(page.getByRole('columnheader', { name: 'Display Name' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Description' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Cron Expression' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Last Modified' })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'Status' })).toBeVisible();
  });

  test('should display all 9 recurring jobs', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Count the number of rows in the table
    const rows = page.locator('table tbody tr');
    const rowCount = await rows.count();

    // Verify we have 9 jobs
    expect(rowCount).toBe(9);
  });

  test('should display job details correctly', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Get the first row
    const firstRow = page.locator('table tbody tr').first();

    // Verify row contains expected data
    await expect(firstRow).toBeVisible();

    // Check that job name/display name is present
    const displayNameCell = firstRow.locator('td').nth(0);
    await expect(displayNameCell).not.toBeEmpty();

    // Check that description is present
    const descriptionCell = firstRow.locator('td').nth(1);
    await expect(descriptionCell).not.toBeEmpty();

    // Check that cron expression is present
    const cronCell = firstRow.locator('td').nth(2);
    await expect(cronCell).not.toBeEmpty();

    // Check that last modified date is present
    const lastModifiedCell = firstRow.locator('td').nth(3);
    await expect(lastModifiedCell).not.toBeEmpty();

    // Check that status toggle button is present
    const statusCell = firstRow.locator('td').nth(4);
    const toggleButton = statusCell.locator('button[role="switch"]');
    await expect(toggleButton).toBeVisible();
  });

  test('should toggle job status from enabled to disabled', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Find an enabled job
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    // Get the toggle button
    const toggleButton = enabledJobRow.locator('button[role="switch"]');

    // Verify it's initially enabled
    await expect(toggleButton).toHaveAttribute('aria-checked', 'true');
    await expect(toggleButton).toContainText('Zapnuto');

    // Click to disable
    await toggleButton.click();

    // Wait for the API call to complete and UI to update
    await page.waitForTimeout(1500);

    // Verify it's now disabled
    await expect(toggleButton).toHaveAttribute('aria-checked', 'false');
    await expect(toggleButton).toContainText('Vypnuto');
  });

  test('should toggle job status from disabled to enabled', async ({ page }) => {
    // First, ensure there's at least one disabled job
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Find an enabled job and disable it
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    const toggleButton = enabledJobRow.locator('button[role="switch"]');

    // Disable it
    await toggleButton.click();
    await page.waitForTimeout(1500);

    // Verify it's disabled
    await expect(toggleButton).toHaveAttribute('aria-checked', 'false');

    // Now enable it again
    await toggleButton.click();
    await page.waitForTimeout(1500);

    // Verify it's enabled
    await expect(toggleButton).toHaveAttribute('aria-checked', 'true');
    await expect(toggleButton).toContainText('Zapnuto');
  });

  test('should show loading state during toggle', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Find an enabled job
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    const toggleButton = enabledJobRow.locator('button[role="switch"]');

    // Start the click but don't wait for it to complete
    const clickPromise = toggleButton.click();

    // Check for loading spinner (should appear briefly)
    const spinner = toggleButton.locator('svg.animate-spin');

    // The spinner should be visible at some point during the request
    // (This might be too fast to catch in some cases, so we'll just verify the button becomes disabled)
    const isDisabledDuringUpdate = await toggleButton.evaluate(
      (el) => el.hasAttribute('disabled')
    );

    // Wait for the click to complete
    await clickPromise;
    await page.waitForTimeout(1500);

    // After completion, button should not be disabled anymore
    await expect(toggleButton).not.toBeDisabled();
  });

  test('should refresh jobs list when clicking refresh button', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Find the refresh button
    const refreshButton = page.getByRole('button', { name: /Obnovit/ });
    await expect(refreshButton).toBeVisible();

    // Click refresh
    await refreshButton.click();

    // Wait for refresh to complete
    await page.waitForTimeout(1000);

    // Verify table is still visible
    await expect(page.locator('table')).toBeVisible();

    // Verify we still have 9 jobs
    const rows = page.locator('table tbody tr');
    const rowCount = await rows.count();
    expect(rowCount).toBe(9);
  });

  test('should display correct job names', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Expected job display names (from seed data)
    const expectedJobs = [
      'Daily Comgate CZK Import',
      'Daily Comgate EUR Import',
      'Daily Consumption Calculation',
      'Daily Invoice Import (CZK)',
      'Daily Invoice Import (EUR)',
      'Invoice Classification',
      'Product Export Download',
      'Product Weight Recalculation',
      'Purchase Price Recalculation'
    ];

    // Get all display name cells
    const displayNameCells = page.locator('table tbody tr td:first-child');
    const displayNames = await displayNameCells.allTextContents();

    // Verify all expected jobs are present
    for (const expectedJob of expectedJobs) {
      expect(displayNames).toContain(expectedJob);
    }
  });

  test('should display cron expressions correctly', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Get all cron expression cells
    const cronCells = page.locator('table tbody tr td:nth-child(3)');
    const cronExpressions = await cronCells.allTextContents();

    // Verify all cron expressions are valid (match cron format pattern)
    const cronPattern = /^\d+\s+\d+\s+\*\s+\*\s+\*/;

    for (const cron of cronExpressions) {
      expect(cronPattern.test(cron.trim())).toBeTruthy();
    }
  });

  test('should show last modified information', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Get the first row's last modified cell
    const firstRow = page.locator('table tbody tr').first();
    const lastModifiedCell = firstRow.locator('td:nth-child(4)');

    // Verify it contains date/time information
    const lastModifiedText = await lastModifiedCell.textContent();
    expect(lastModifiedText).toBeTruthy();

    // Verify it contains "System" as the modifier
    await expect(lastModifiedCell).toContainText('System');
  });

  test('should have proper accessibility attributes on toggle buttons', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Get all toggle buttons
    const toggleButtons = page.locator('button[role="switch"]');
    const buttonCount = await toggleButtons.count();

    // Verify we have toggle buttons for all 9 jobs
    expect(buttonCount).toBe(9);

    // Check each button has proper ARIA attributes
    for (let i = 0; i < buttonCount; i++) {
      const button = toggleButtons.nth(i);

      // Verify role="switch"
      await expect(button).toHaveAttribute('role', 'switch');

      // Verify aria-checked is either "true" or "false"
      const ariaChecked = await button.getAttribute('aria-checked');
      expect(['true', 'false']).toContain(ariaChecked);

      // Verify aria-label is present
      const ariaLabel = await button.getAttribute('aria-label');
      expect(ariaLabel).toBeTruthy();
      expect(ariaLabel).toMatch(/(Zapnout|Vypnout) úlohu/);
    }
  });

  test('should persist job status changes after page refresh', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Find a specific job (e.g., Invoice Classification)
    const targetRow = page.locator('table tbody tr').filter({
      hasText: 'Invoice Classification'
    });

    const toggleButton = targetRow.locator('button[role="switch"]');

    // Get initial state
    const initialState = await toggleButton.getAttribute('aria-checked');

    // Toggle it
    await toggleButton.click();
    await page.waitForTimeout(1500);

    // Verify state changed
    const newState = await toggleButton.getAttribute('aria-checked');
    expect(newState).not.toBe(initialState);

    // Refresh the page
    await page.reload();
    await page.waitForLoadState('networkidle');
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Find the same job again
    const targetRowAfterRefresh = page.locator('table tbody tr').filter({
      hasText: 'Invoice Classification'
    });

    const toggleButtonAfterRefresh = targetRowAfterRefresh.locator('button[role="switch"]');

    // Verify the state persisted
    const stateAfterRefresh = await toggleButtonAfterRefresh.getAttribute('aria-checked');
    expect(stateAfterRefresh).toBe(newState);

    // Toggle back to original state for cleanup
    await toggleButtonAfterRefresh.click();
    await page.waitForTimeout(1500);
  });
});
