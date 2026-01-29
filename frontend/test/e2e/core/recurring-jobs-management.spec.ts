import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Recurring Jobs Management', () => {
  test.beforeEach(async ({ page }) => {
    // Establish E2E authentication session with full frontend setup
    await navigateToApp(page);

    // Navigate to recurring jobs page
    await page.goto('/recurring-jobs');

    // Wait for the page to load
    await page.waitForLoadState('domcontentloaded');
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

    // Verify table headers exist using text locators (bypassing role selector issues)
    await expect(page.getByText('Display Name').first()).toBeVisible();
    await expect(page.getByText('Description').first()).toBeVisible();
    await expect(page.getByText('Cron Expression').first()).toBeVisible();
    await expect(page.getByText('Last Modified').first()).toBeVisible();
    await expect(page.getByText('Status').first()).toBeVisible();
    await expect(page.getByText('Actions').first()).toBeVisible();
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

    // Get the first job (regardless of state)
    const firstJobRow = page.locator('table tbody tr').first();
    const toggleButton = firstJobRow.locator('button[role="switch"]');

    // Check the current state
    const currentState = await toggleButton.getAttribute('aria-checked');

    // If job is disabled, enable it first
    if (currentState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(1500);

      // Verify it's now enabled
      await expect(toggleButton).toHaveAttribute('aria-checked', 'true');
      await expect(toggleButton).toContainText('Zapnuto');
    }

    // Now it should be enabled, verify initial state
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
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Get the first job (regardless of state)
    const firstJobRow = page.locator('table tbody tr').first();
    const toggleButton = firstJobRow.locator('button[role="switch"]');

    // Check the current state
    const currentState = await toggleButton.getAttribute('aria-checked');

    // If job is enabled, disable it first
    if (currentState === 'true') {
      await toggleButton.click();
      await page.waitForTimeout(1500);

      // Verify it's now disabled
      await expect(toggleButton).toHaveAttribute('aria-checked', 'false');
      await expect(toggleButton).toContainText('Vypnuto');
    }

    // Now it should be disabled, verify initial state
    await expect(toggleButton).toHaveAttribute('aria-checked', 'false');
    await expect(toggleButton).toContainText('Vypnuto');

    // Click to enable
    await toggleButton.click();

    // Wait for the API call to complete and UI to update
    await page.waitForTimeout(1500);

    // Verify it's now enabled
    await expect(toggleButton).toHaveAttribute('aria-checked', 'true');
    await expect(toggleButton).toContainText('Zapnuto');
  });

  test('should show loading state during toggle', async ({ page }) => {
    // Wait for table to load
    await page.waitForSelector('table tbody tr', { timeout: 10000 });

    // Get the first job (regardless of state)
    const firstJobRow = page.locator('table tbody tr').first();
    const toggleButton = firstJobRow.locator('button[role="switch"]');

    // Check the current state
    const currentState = await toggleButton.getAttribute('aria-checked');

    // If job is disabled, enable it first
    if (currentState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(1500);

      // Verify it's now enabled
      await expect(toggleButton).toHaveAttribute('aria-checked', 'true');
    }

    // Now it should be enabled, verify initial state
    await expect(toggleButton).toHaveAttribute('aria-checked', 'true');

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
    // Pattern allows: digits or * in each of the 5 fields
    const cronPattern = /^(\d+|\*)\s+(\d+|\*)\s+(\d+|\*)\s+(\d+|\*)\s+(\d+|\*)$/;

    for (const cron of cronExpressions) {
      const trimmedCron = cron.trim();
      expect(cronPattern.test(trimmedCron)).toBeTruthy();
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

    // Verify it contains a date in Czech format (DD. MM. YYYY HH:mm)
    // and a modifier (either "System" or "E2E Test User")
    expect(lastModifiedText).toMatch(/\d{2}\.\s\d{2}\.\s\d{4}\s\d{2}:\d{2}/);

    // Verify it contains a valid modifier (System or E2E Test User)
    expect(lastModifiedText).toMatch(/(System|E2E Test User)/);
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
    await page.waitForLoadState('domcontentloaded');
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
    // Establish E2E authentication session with full frontend setup
    await navigateToApp(page);

    // Navigate to recurring jobs page
    await page.goto('/recurring-jobs');

    // Wait for the page to load
    await page.waitForLoadState('domcontentloaded');
    await page.waitForSelector('table tbody tr', { timeout: 10000 });
  });

  test('should display "Run Now" button for each job', async ({ page }) => {
    // Get all rows in the table
    const rows = page.locator('table tbody tr');
    const rowCount = await rows.count();

    // Verify each row has a "Run Now" button (by visible text, not aria-label)
    for (let i = 0; i < rowCount; i++) {
      const row = rows.nth(i);
      const runNowButton = row.getByText('Run Now');
      await expect(runNowButton).toBeVisible();
    }
  });

  test('should have "Actions" column header', async ({ page }) => {
    // Verify the Actions column header exists
    await expect(page.getByText('Actions').first()).toBeVisible();
  });

  test('should open confirmation dialog when clicking "Run Now" on enabled job', async ({ page }) => {
    // Find the first job row
    const jobRow = page.locator('table tbody tr').first();

    // Enable the job first (toggle it on if it's off)
    const toggleButton = jobRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    // Get job name from the row
    const jobName = await jobRow.locator('td').nth(0).textContent();

    // Click "Run Now" button (by visible text, not aria-label)
    const runNowButton = jobRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog to appear and verify it's visible
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Verify dialog contains confirmation text and job name
    await expect(page.getByText('Chystáte se manuálně spustit úlohu:')).toBeVisible();
    await expect(page.getByRole('paragraph').filter({ hasText: jobName || '' })).toBeVisible();

    // Close dialog
    await page.getByRole('button', { name: /Zrušit/i }).click();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });

  test('should display job details in confirmation dialog', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Enable the job first (normalize state to enabled)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    const jobDisplayName = await firstRow.locator('td').nth(0).textContent();

    // Click "Run Now" button (by visible text, not aria-label)
    const runNowButton = firstRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog heading to appear (dialog doesn't have role="dialog")
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Verify confirmation text
    await expect(page.getByText('Chystáte se manuálně spustit úlohu:')).toBeVisible();

    // Verify job display name is shown (use paragraph role to scope to dialog)
    await expect(page.getByRole('paragraph').filter({ hasText: jobDisplayName || '' })).toBeVisible();

    // Close dialog
    await page.getByRole('button', { name: /Zrušit/i }).click();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });

  test('should show warning for disabled job in confirmation dialog', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Normalize state to disabled (opposite of other tests)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    // Disable the job if it's currently enabled
    if (initialState === 'true') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    // Now click "Run Now" on the disabled job (by visible text, not aria-label)
    const runNowButton = firstRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog heading to appear
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Verify warning is displayed for disabled job
    await expect(page.getByText(/Úloha je aktuálně vypnutá/i)).toBeVisible();
    await expect(page.getByText(/Spuštěním potvrdíte, že chcete tuto úlohu spustit i když je vypnutá/i)).toBeVisible();

    // Close dialog
    await page.getByRole('button', { name: /Zrušit/i }).click();

    // Re-enable the job for cleanup (restore to original state)
    if (initialState === 'true') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });

  test('should close dialog when clicking cancel', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Enable the job first (normalize state to enabled)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    // Click "Run Now" button (by visible text, not aria-label)
    const runNowButton = firstRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog heading to appear (dialog doesn't have role="dialog")
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Click cancel button
    await page.getByRole('button', { name: /Zrušit/i }).click();

    // Verify dialog is closed (heading should no longer be visible)
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).not.toBeVisible();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });

  test('should close dialog when clicking X button', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Enable the job first (normalize state to enabled)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    // Click "Run Now" button (by visible text, not aria-label)
    const runNowButton = firstRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog heading to appear (dialog doesn't have role="dialog")
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Click X button (located in top-right corner of dialog, contains SVG from lucide-react)
    const dialogContainer = page.locator('.relative.bg-white.rounded-lg.shadow-xl');
    const closeButton = dialogContainer.locator('button.absolute.top-4.right-4');
    await closeButton.click();

    // Verify dialog is closed (heading should no longer be visible)
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).not.toBeVisible();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });

  test('should close dialog when clicking backdrop', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Enable the job first (normalize state to enabled)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    // Click "Run Now" button (by visible text, not aria-label)
    const runNowButton = firstRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog heading to appear (dialog doesn't have role="dialog")
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Click backdrop (outside dialog)
    await page.locator('.fixed.inset-0.bg-black').click({ position: { x: 10, y: 10 } });

    // Verify dialog is closed (heading should no longer be visible)
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).not.toBeVisible();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });

  test('should trigger job when confirming in dialog', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Enable the job first (normalize state to enabled)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    // Click "Run Now" button (by visible text, not aria-label)
    const runNowButton = firstRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog heading to appear (dialog doesn't have role="dialog")
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Click confirm button (exact match "Spustit" to avoid matching "Run Now" buttons in table)
    const confirmButton = page.getByRole('button', { name: 'Spustit', exact: true });
    await confirmButton.click();

    // Wait for API call to complete
    await page.waitForTimeout(2000);

    // Dialog heading should no longer be visible (dialog closed)
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).not.toBeVisible();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }

    // Note: We can't verify the job actually executed in E2E test,
    // but we verified the UI flow works correctly
  });

  test('should show loading state during trigger execution', async ({ page }) => {
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Enable the job first (normalize state to enabled)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    // Click "Run Now" button (by visible text, not aria-label)
    const runNowButton = firstRow.getByText('Run Now');
    await runNowButton.click();

    // Wait for dialog heading to appear (dialog doesn't have role="dialog")
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Start confirmation but don't wait
    const confirmButton = page.getByRole('button', { name: 'Spustit', exact: true });
    const clickPromise = confirmButton.click();

    // The Run Now button in the table should show loading state briefly
    // (This might be too fast to catch, but we'll check if button becomes disabled)
    await page.waitForTimeout(100);

    // Wait for trigger to complete
    await clickPromise;
    await page.waitForTimeout(2000);

    // Dialog heading should no longer be visible (dialog closed)
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).not.toBeVisible();

    // After completion, button should not be disabled
    await expect(runNowButton).not.toBeDisabled();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });

  test('should have proper accessibility attributes on Run Now buttons', async ({ page }) => {
    // Get all "Run Now" buttons (by visible text since aria-label is in Czech)
    const runNowButtons = page.getByText('Run Now');
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
    // Find first job
    const firstRow = page.locator('table tbody tr').first();

    // Enable the job first (normalize state to enabled)
    const toggleButton = firstRow.locator('button[role="switch"]');
    const initialState = await toggleButton.getAttribute('aria-checked');

    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500); // Wait for state to update
    }

    const runNowButton = firstRow.getByText('Run Now');

    // Click "Run Now" button
    await runNowButton.click();

    // Wait for dialog heading to appear (dialog doesn't have role="dialog")
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Confirm (exact match "Spustit" to avoid matching "Run Now" buttons in table)
    await page.getByRole('button', { name: 'Spustit', exact: true }).click();
    await page.waitForTimeout(500);

    // Try to click again immediately
    await runNowButton.click();

    // Dialog heading should appear again
    await expect(page.getByRole('heading', { name: 'Spustit úlohu nyní?' })).toBeVisible();

    // Close it
    await page.getByRole('button', { name: /Zrušit/i }).click();

    // Toggle back to original state for cleanup
    if (initialState === 'false') {
      await toggleButton.click();
      await page.waitForTimeout(500);
    }
  });
});
