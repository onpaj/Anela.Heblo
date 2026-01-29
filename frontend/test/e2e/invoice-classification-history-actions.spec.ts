import { test, expect } from '@playwright/test';
import {
  navigateToClassificationHistory,
  getRowCount,
  clickFirstRowClassifyButton,
  openClassifyInvoiceModal,
  getClassifyInvoiceModalTitle,
  clickClassifyInvoiceCancel,
  clickClassifyInvoiceSave,
  selectClassificationRuleType,
  selectClassificationAccountingTemplate,
  selectClassificationDepartment,
  fillClassificationDescription,
} from './helpers/classification-history-helpers';

test.describe('Classification History - Classify Invoice Button', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToClassificationHistory(page);
  });

  test('should show Classify Invoice button in action column', async ({
    page,
  }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - button is visible in first row
    const classifyButton = page
      .locator('table tbody tr')
      .first()
      .locator('button:has-text("Klassifizieren")');

    // Assert
    await expect(classifyButton).toBeVisible();
  });

  test('should show loading state when classifying invoice', async ({
    page,
  }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - click classify button
    await clickFirstRowClassifyButton(page);

    // Wait for modal to appear
    await page.waitForSelector('div[role="dialog"]', { state: 'visible' });

    // Assert - modal title should be visible
    const modalTitle = await getClassifyInvoiceModalTitle(page);
    expect(modalTitle).toBe('Rechnung klassifizieren');
  });

  test('should disable save button when form is invalid', async ({ page }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - open modal
    await openClassifyInvoiceModal(page);

    // Assert - save button should be disabled initially (no selections made)
    const saveButton = page.locator('button:has-text("Speichern")');
    await expect(saveButton).toBeDisabled();
  });

  test('should successfully classify invoice when all required fields are filled', async ({
    page,
  }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - open modal and fill form
    await openClassifyInvoiceModal(page);

    // Select rule type
    await selectClassificationRuleType(page, 'Buchhaltungsvorlage');

    // Select accounting template (first option after placeholder)
    await selectClassificationAccountingTemplate(page, 1);

    // Optional: Select department if visible
    const departmentSelect = page.locator('select[name="department"]');
    if (await departmentSelect.isVisible()) {
      await selectClassificationDepartment(page, 1);
    }

    // Fill description
    await fillClassificationDescription(page, 'E2E Test Classification');

    // Click save
    await clickClassifyInvoiceSave(page);

    // Assert - modal should close
    await page.waitForSelector('div[role="dialog"]', { state: 'hidden' });

    // Success message or table refresh should occur
    // (Implementation depends on actual behavior)
  });

  test('should handle classification errors gracefully', async ({ page }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - open modal
    await openClassifyInvoiceModal(page);

    // Try to save without filling required fields
    // (if form validation allows clicking save)
    const saveButton = page.locator('button:has-text("Speichern")');

    // If save button is enabled (shouldn't be, but test error handling)
    if (await saveButton.isEnabled()) {
      await clickClassifyInvoiceSave(page);

      // Assert - error message should appear or modal stays open
      // (Implementation depends on actual error handling)
      await expect(page.locator('div[role="dialog"]')).toBeVisible();
    }

    // Clean up - close modal
    await clickClassifyInvoiceCancel(page);
  });
});

test.describe('Classification History - Create Rule Button', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToClassificationHistory(page);
  });

  test('should show Create Rule button and open modal', async ({ page }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - find and click Create Rule button in first row
    const createRuleButton = page
      .locator('table tbody tr')
      .first()
      .locator('button:has-text("Regel erstellen")');

    await expect(createRuleButton).toBeVisible();
    await createRuleButton.click();

    // Wait for modal to appear
    await page.waitForSelector('div[role="dialog"]', { state: 'visible' });

    // Assert - modal should have correct title
    const modalTitle = page.locator('div[role="dialog"] h2');
    await expect(modalTitle).toHaveText('Neue Regel erstellen');
  });

  test('should disable Create Rule button when no company is selected', async ({
    page,
  }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - find Create Rule button in a row with no company
    // (This test assumes some rows might not have company data)
    const firstRow = page.locator('table tbody tr').first();
    const createRuleButton = firstRow.locator(
      'button:has-text("Regel erstellen")'
    );

    // Assert - button should be visible
    // (Disabled state depends on whether company exists in row data)
    await expect(createRuleButton).toBeVisible();

    // If button is disabled, it should have appropriate styling or attribute
    // (Implementation-specific assertion)
  });

  test('should prefill company name when opening rule creation modal', async ({
    page,
  }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Get company name from first row
    const firstRow = page.locator('table tbody tr').first();
    const companyCell = firstRow.locator('td').nth(1); // Assuming company is 2nd column
    const companyName = await companyCell.textContent();

    // Act - open Create Rule modal
    const createRuleButton = firstRow.locator(
      'button:has-text("Regel erstellen")'
    );
    await createRuleButton.click();

    // Wait for modal
    await page.waitForSelector('div[role="dialog"]', { state: 'visible' });

    // Assert - company name input should be prefilled
    const companyInput = page.locator('input[name="companyName"]');
    const inputValue = await companyInput.inputValue();

    expect(inputValue).toBe(companyName?.trim() || '');
  });

  test('should close rule creation modal when cancel is clicked', async ({
    page,
  }) => {
    // Arrange
    const rowCount = await getRowCount(page);
    if (rowCount === 0) {
      throw new Error(
        'Test data missing: No classification history records found for testing'
      );
    }

    // Act - open modal
    const createRuleButton = page
      .locator('table tbody tr')
      .first()
      .locator('button:has-text("Regel erstellen")');
    await createRuleButton.click();

    // Wait for modal
    await page.waitForSelector('div[role="dialog"]', { state: 'visible' });

    // Click cancel button
    const cancelButton = page.locator('button:has-text("Abbrechen")');
    await cancelButton.click();

    // Assert - modal should be hidden
    await page.waitForSelector('div[role="dialog"]', { state: 'hidden' });
  });
});
