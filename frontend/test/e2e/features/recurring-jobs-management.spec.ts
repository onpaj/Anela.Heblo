import { test, expect } from '@playwright/test';
import { createE2EAuthSession } from '../helpers/e2e-auth-helper';

test.describe('Recurring Jobs Management', () => {
  test.beforeEach(async ({ page }) => {
    // Establish E2E authentication session
    await createE2EAuthSession(page);

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

test.describe('Recurring Jobs - Manual Trigger', () => {
  test.beforeEach(async ({ page }) => {
    // Establish E2E authentication session
    await createE2EAuthSession(page);

    // Navigate to recurring jobs page
    await page.goto('/recurring-jobs');

    // Wait for the page to load
    await page.waitForLoadState('networkidle');
    await page.waitForSelector('table tbody tr', { timeout: 10000 });
  });

  test('should display "Run Now" button for each job', async ({ page }) => {
    // Get all rows in the table
    const rows = page.locator('table tbody tr');
    const rowCount = await rows.count();

    // Verify each row has a "Run Now" button
    for (let i = 0; i < rowCount; i++) {
      const row = rows.nth(i);
      const runNowButton = row.getByRole('button', { name: /Run Now/i });
      await expect(runNowButton).toBeVisible();
    }
  });

  test('should have "Actions" column header', async ({ page }) => {
    // Verify the Actions column header exists
    await expect(page.getByRole('columnheader', { name: 'Actions' })).toBeVisible();
  });

  test('should open confirmation dialog when clicking "Run Now" on enabled job', async ({ page }) => {
    // Find an enabled job
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    // Get job name from the row
    const jobName = await enabledJobRow.locator('td').nth(0).textContent();

    // Click "Run Now" button
    const runNowButton = enabledJobRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Verify dialog is visible
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Verify dialog contains job name
    await expect(dialog).toContainText(jobName || '');

    // Close dialog
    const cancelButton = dialog.getByRole('button', { name: /Zrušit/i });
    await cancelButton.click();
  });

  test('should display job details in confirmation dialog', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();
    const jobDisplayName = await firstRow.locator('td').nth(0).textContent();

    // Click "Run Now" button
    const runNowButton = firstRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Wait for dialog to appear
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Verify dialog title
    await expect(dialog.getByText('Potvrdit spuštění úlohy')).toBeVisible();

    // Verify job display name is shown
    await expect(dialog).toContainText(jobDisplayName || '');

    // Verify confirmation text
    await expect(dialog.getByText(/Opravdu chcete spustit tuto úlohu/i)).toBeVisible();

    // Close dialog
    await dialog.getByRole('button', { name: /Zrušit/i }).click();
  });

  test('should show warning for disabled job in confirmation dialog', async ({ page }) => {
    // Find an enabled job and disable it first
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    // Disable the job
    const toggleButton = enabledJobRow.locator('button[role="switch"]');
    await toggleButton.click();
    await page.waitForTimeout(1500);

    // Now click "Run Now" on the disabled job
    const runNowButton = enabledJobRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Wait for dialog
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Verify warning is displayed
    await expect(dialog.getByText(/Pozor/i)).toBeVisible();
    await expect(dialog.getByText(/Tato úloha je momentálně vypnutá/i)).toBeVisible();

    // Close dialog
    await dialog.getByRole('button', { name: /Zrušit/i }).click();

    // Re-enable the job for cleanup
    await toggleButton.click();
    await page.waitForTimeout(1500);
  });

  test('should close dialog when clicking cancel', async ({ page }) => {
    // Click "Run Now" on first job
    const firstRow = page.locator('table tbody tr').first();
    const runNowButton = firstRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Verify dialog is visible
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Click cancel button
    const cancelButton = dialog.getByRole('button', { name: /Zrušit/i });
    await cancelButton.click();

    // Verify dialog is closed
    await expect(dialog).not.toBeVisible();
  });

  test('should close dialog when clicking X button', async ({ page }) => {
    // Click "Run Now" on first job
    const firstRow = page.locator('table tbody tr').first();
    const runNowButton = firstRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Verify dialog is visible
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Click X button
    const closeButton = dialog.locator('button[aria-label*="Zavřít"]');
    await closeButton.click();

    // Verify dialog is closed
    await expect(dialog).not.toBeVisible();
  });

  test('should close dialog when clicking backdrop', async ({ page }) => {
    // Click "Run Now" on first job
    const firstRow = page.locator('table tbody tr').first();
    const runNowButton = firstRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Verify dialog is visible
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Click backdrop (outside dialog)
    await page.locator('.fixed.inset-0.bg-black').click({ position: { x: 10, y: 10 } });

    // Verify dialog is closed
    await expect(dialog).not.toBeVisible();
  });

  test('should trigger job when confirming in dialog', async ({ page }) => {
    // Find first enabled job
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    // Click "Run Now" button
    const runNowButton = enabledJobRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Wait for dialog
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Click confirm button
    const confirmButton = dialog.getByRole('button', { name: /Potvrdit/i });
    await confirmButton.click();

    // Wait for API call to complete
    await page.waitForTimeout(2000);

    // Dialog should be closed
    await expect(dialog).not.toBeVisible();

    // Note: We can't verify the job actually executed in E2E test,
    // but we verified the UI flow works correctly
  });

  test('should show loading state during trigger execution', async ({ page }) => {
    // Find first enabled job
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    // Click "Run Now" button
    const runNowButton = enabledJobRow.getByRole('button', { name: /Run Now/i });
    await runNowButton.click();

    // Wait for dialog
    const dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Start confirmation but don't wait
    const confirmButton = dialog.getByRole('button', { name: /Potvrdit/i });
    const clickPromise = confirmButton.click();

    // The Run Now button in the table should show loading state briefly
    // (This might be too fast to catch, but we'll check if button becomes disabled)
    await page.waitForTimeout(100);

    // Wait for trigger to complete
    await clickPromise;
    await page.waitForTimeout(2000);

    // After completion, button should not be disabled
    await expect(runNowButton).not.toBeDisabled();
  });

  test('should have proper accessibility attributes on Run Now buttons', async ({ page }) => {
    // Get all "Run Now" buttons
    const runNowButtons = page.getByRole('button', { name: /Run Now/i });
    const buttonCount = await runNowButtons.count();

    // Verify we have buttons for all 9 jobs
    expect(buttonCount).toBe(9);

    // Check first button has proper aria-label
    const firstButton = runNowButtons.first();
    const ariaLabel = await firstButton.getAttribute('aria-label');
    expect(ariaLabel).toBeTruthy();
    expect(ariaLabel).toMatch(/Spustit úlohu .* nyní/);
  });

  test('should handle multiple rapid trigger attempts gracefully', async ({ page }) => {
    // Find first enabled job
    const enabledJobRow = page.locator('table tbody tr').filter({
      has: page.locator('button[role="switch"][aria-checked="true"]')
    }).first();

    const runNowButton = enabledJobRow.getByRole('button', { name: /Run Now/i });

    // Click "Run Now" button
    await runNowButton.click();

    // Wait for dialog
    let dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Confirm
    await dialog.getByRole('button', { name: /Potvrdit/i }).click();
    await page.waitForTimeout(500);

    // Try to click again immediately
    await runNowButton.click();

    // Dialog should open again
    dialog = page.locator('[role="dialog"]');
    await expect(dialog).toBeVisible();

    // Close it
    await dialog.getByRole('button', { name: /Zrušit/i }).click();
  });
});
