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

  test("30: Import button opens modal", async ({ page }) => {
    // Click Import button
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    // Verify modal is visible
    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Verify modal title
    const modalTitle = page.locator('h2:has-text("Import faktur")');
    await expect(modalTitle).toBeVisible();
  });

  test("31: Modal displays file upload area", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    // Verify file upload area is visible
    const uploadArea = page.locator('input[type="file"]');
    await expect(uploadArea).toBeVisible();

    // Verify upload instructions text
    const uploadText = page.locator('text=/Přetáhněte|vyberte soubor/i');
    await expect(uploadText).toBeVisible();
  });

  test("32: Modal displays accepted file formats", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    // Verify accepted formats are mentioned
    const formatText = page.locator('text=/CSV|Excel|XLSX/i');
    await expect(formatText).toBeVisible();
  });

  test("33: Close modal with X button", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Click close button
    const closeButton = page.locator('button[aria-label="Zavřít"]');
    await closeButton.click();

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test("34: Close modal with Cancel button", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Click cancel button
    const cancelButton = page.locator('button:has-text("Zrušit")');
    await cancelButton.click();

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test("35: Close modal with Escape key", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Press Escape key
    await page.keyboard.press("Escape");

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test("36: Close modal by clicking backdrop", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const modal = page.locator('[role="dialog"]');
    await expect(modal).toBeVisible();

    // Click outside modal (backdrop)
    await page.mouse.click(10, 10);

    // Verify modal is closed
    await expect(modal).not.toBeVisible();
  });

  test("37: Upload button is disabled without file", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    // Verify upload/submit button is disabled
    const submitButton = page.locator('button:has-text("Nahrát")');
    await expect(submitButton).toBeDisabled();
  });

  test("38: File selection enables upload button", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    // Find file input
    const fileInput = page.locator('input[type="file"]');

    // Create a test CSV file content
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(path, "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000");
    }, testFilePath);

    // Upload file
    await fileInput.setInputFiles(testFilePath);

    // Verify upload button is now enabled
    const submitButton = page.locator('button:has-text("Nahrát")');
    await expect(submitButton).toBeEnabled();
  });

  test("39: Displays file name after selection", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Create a test file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(path, "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000");
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    // Verify file name is displayed
    const fileName = page.locator('text="test-invoices.csv"');
    await expect(fileName).toBeVisible();
  });

  test("40: Remove selected file", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Upload file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(path, "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000");
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

  test("41: Displays validation error for invalid file type", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
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
    const errorMessage = page.locator('text=/Nepodporovaný formát|Neplatný soubor/i');
    await expect(errorMessage).toBeVisible();
  });

  test("42: Shows progress indicator during upload", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Upload valid file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(path, "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000");
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    const submitButton = page.locator('button:has-text("Nahrát")');
    await submitButton.click();

    // Verify loading/progress indicator appears
    const loadingIndicator = page.locator('[role="progressbar"]').or(page.locator('text=/Nahrávám|Zpracovávám/i'));
    await expect(loadingIndicator).toBeVisible({ timeout: 5000 });
  });

  test("43: Displays success message after upload", async ({ page }) => {
    const importButton = page.locator('button:has-text("Importovat faktury")');
    await importButton.click();

    const fileInput = page.locator('input[type="file"]');

    // Upload valid file
    const testFilePath = "/tmp/test-invoices.csv";
    await page.evaluate((path) => {
      const fs = require("fs");
      fs.writeFileSync(path, "InvoiceId,CustomerName,Amount\n2024001,Test Customer,1000");
    }, testFilePath);

    await fileInput.setInputFiles(testFilePath);

    const submitButton = page.locator('button:has-text("Nahrát")');
    await submitButton.click();

    // Wait for upload to complete and verify success message
    const successMessage = page.locator('text=/Úspěšně nahráno|Import byl úspěšný/i');
    await expect(successMessage).toBeVisible({ timeout: 10000 });
  });
});
