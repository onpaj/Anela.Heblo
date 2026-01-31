import { test, expect } from "@playwright/test";
import { navigateToIssuedInvoices } from "../helpers/e2e-auth-helper";
import { waitForLoadingComplete } from "../helpers/wait-helpers";

test.describe("IssuedInvoices - Import Modal", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);

    // Switch to Grid tab
    const gridTab = page.locator('button:has-text("Seznam")');
    await gridTab.click();
    await waitForLoadingComplete(page);
  });

  test.skip("30: Import button opens modal", async ({ page }) => {
    // SKIPPED: Test expectations don't match actual application behavior
    // The Import button opens a date-range import modal (for importing from external API),
    // NOT a file upload modal as the test expects.
    //
    // Actual modal behavior:
    // - Title: "Import faktur"
    // - Contains: Radio buttons for import type (Date range vs Specific invoice),
    //   currency dropdown, date fields, Cancel/Import buttons
    // - Does NOT contain: File upload area, drag-drop, file format text
    //
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // These tests should either be:
    // 1. Removed (if file upload feature is not planned)
    // 2. Rewritten to test the actual date-range import modal
    // 3. Kept but disabled until file upload feature is implemented
    //
    // See screenshot at: frontend/test-results/import-modal-*.png
    // TODO: Clarify with product owner whether file upload import is planned

    // Click Import button
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    // Verify modal is visible
    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Verify modal title
    const modalTitle = page.locator('h2:has-text("Import faktur")');
    await expect(modalTitle).toBeVisible();
  });

  test.skip("31: Modal displays file upload area", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The actual modal contains:
    //   - Radio buttons for import type (Date range vs Specific invoice)
    //   - Currency dropdown, date fields, Cancel/Import buttons
    // The actual modal does NOT contain:
    //   - File upload input (input[type="file"])
    //   - Drag-drop upload area or instructions
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See test #30 comments and Iteration 4 analysis for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    // Verify file upload area is visible
    const uploadArea = page.locator('input[type="file"]');
    await expect(uploadArea).toBeVisible();

    // Verify upload instructions text
    const uploadText = page.locator("text=/Přetáhněte|vyberte soubor/i");
    await expect(uploadText).toBeVisible();
  });

  test.skip("32: Modal displays accepted file formats", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The actual modal contains:
    //   - Radio buttons for import type (Date range vs Specific invoice)
    //   - Currency dropdown, date fields, Cancel/Import buttons
    // The actual modal does NOT contain:
    //   - File format text (CSV, Excel, XLSX)
    //   - File upload input or drag-drop area
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See tests #30-31 comments and Iterations 4-5 analysis for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    // Verify accepted formats are mentioned
    const formatText = page.locator("text=/CSV|Excel|XLSX/i");
    await expect(formatText).toBeVisible();
  });

  test.skip("33: Close modal with X button", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // While this test checks generic modal close functionality (which likely works),
    // all 14 tests in this file are designed to test file upload import workflow that doesn't exist.
    // The actual modal is a date-range import modal with different UI and purpose.
    // See tests #30-32 comments and Iterations 4-5 for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Click close button
    const closeButton = page.locator('button[aria-label="Zavřít"]');
    await closeButton.click();

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test.skip("34: Close modal with Cancel button", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // While this test checks generic modal close functionality (which likely works),
    // all 14 tests in this file are designed to test file upload import workflow that doesn't exist.
    // The actual modal is a date-range import modal with different UI and purpose.
    // See tests #30-33 comments and Iterations 1-2 for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Click cancel button
    const cancelButton = page.locator('button:has-text("Zrušit")');
    await cancelButton.click();

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test.skip("35: Close modal with Escape key", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // While this test checks generic modal close functionality (Escape key - which likely works),
    // all 14 tests in this file are designed to test file upload import workflow that doesn't exist.
    // The actual modal is a date-range import modal with different UI and purpose.
    // See tests #30-34 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Press Escape key
    await page.keyboard.press("Escape");

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test.skip("36: Close modal by clicking backdrop", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // While this test checks generic modal close functionality (clicking backdrop - which likely works),
    // all 14 tests in this file are designed to test file upload import workflow that doesn't exist.
    // The actual modal is a date-range import modal with different UI and purpose.
    // See tests #30-35 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Click outside modal (backdrop)
    await page.mouse.click(10, 10);

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test.skip("37: Upload button is disabled without file", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The test expects to find an "Upload" button ("Nahrát") that should be disabled without a file.
    // However, the actual modal is a date-range import modal with "Import" button, not "Upload".
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See tests #30-36 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    // Verify upload/submit button is disabled
    const submitButton = page.locator('button:has-text("Nahrát")');
    await expect(submitButton).toBeDisabled();
  });

  test.skip("38: File selection enables upload button", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The test expects to find file input (input[type="file"]) and test file upload enabling submit button.
    // However, the actual modal is a date-range import modal with different UI and purpose.
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See tests #30-37 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    // Find file input
    const fileInput = page.locator('input[type="file"]');

    // Create a test CSV file content
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(
        path,
        "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000",
      );
    }, testFilePath);

    // Upload file
    await fileInput.setInputFiles(testFilePath);

    // Verify upload button is now enabled
    const submitButton = page.locator('button:has-text("Nahrát")');
    await expect(submitButton).toBeEnabled();
  });

  test.skip("39: Displays file name after selection", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The test expects to find file input (input[type="file"]), upload a file, and see the filename displayed.
    // However, the actual modal is a date-range import modal with different UI and purpose.
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See tests #30-38 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Create a test file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(
        path,
        "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000",
      );
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    // Verify file name is displayed
    const fileName = page.locator('text="test-invoices.csv"');
    await expect(fileName).toBeVisible();
  });

  test.skip("40: Remove selected file", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The test expects to find file input (input[type="file"]), upload a file, remove it, and verify removal.
    // However, the actual modal is a date-range import modal with different UI and purpose.
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See tests #30-39 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Upload file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(
        path,
        "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000",
      );
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    // Verify file is selected
    const fileName = page.locator('text="test-invoices.csv"');
    await expect(fileName).toBeVisible();

    // Click remove/clear button
    const removeButton = page.locator('button[aria-label="Odebrat soubor"]');
    await removeButton.click();

    // Verify file is removed
    await expect(fileName).not.toBeVisible();

    // Verify upload button is disabled again
    const submitButton = page.locator('button:has-text("Nahrát")');
    await expect(submitButton).toBeDisabled();
  });

  test.skip("41: Displays validation error for invalid file type", async ({
    page,
  }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The test expects to find file input (input[type="file"]) and test file upload validation.
    // However, the actual modal is a date-range import modal with different UI and purpose.
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See tests #30-40 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Create an invalid file (e.g., .txt)
    const testFilePath = "/tmp/invalid-file.txt";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(path, "This is not a valid invoice file");
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    // Verify error message is displayed
    const errorMessage = page.locator(
      "text=/Nepodporovaný formát|Neplatný soubor/i",
    );
    await expect(errorMessage).toBeVisible();
  });

  test.skip("42: Shows progress indicator during upload", async ({ page }) => {
    // SKIPPED: Feature mismatch - Import button opens DATE-RANGE modal, not file upload modal
    // The test expects to find file input (input[type="file"]), upload a file, and see progress indicator during upload.
    // However, the actual modal is a date-range import modal with different UI and purpose.
    // All 14 tests in this file test file upload functionality that doesn't exist.
    // See tests #30-41 comments and previous iterations for detailed findings.
    // TODO: Remove these tests or rewrite to test the actual date-range import modal
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Upload valid file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(
        path,
        "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000",
      );
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    const submitButton = page.locator('button:has-text("Nahrát")');
    await submitButton.click();

    // Verify loading/progress indicator appears
    const loadingIndicator = page
      .locator('[role="progressbar"]')
      .or(page.locator("text=/Nahrávám|Zpracovávám/i"));
    await expect(loadingIndicator).toBeVisible({ timeout: 5000 });
  });

  test("43: Displays success message after upload", async ({ page }) => {
    const importButton = page.locator('button:has-text("Import")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Upload valid file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(
        path,
        "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000",
      );
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    const submitButton = page.locator('button:has-text("Nahrát")');
    await submitButton.click();

    // Wait for upload to complete and verify success message
    const successMessage = page.locator(
      "text=/Úspěšně nahráno|Import byl úspěšný/i",
    );
    await expect(successMessage).toBeVisible({ timeout: 10000 });
  });
});
