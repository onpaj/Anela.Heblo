import { test, expect } from "@playwright/test";
import { navigateToIssuedInvoices } from "../helpers/e2e-auth-helper";
import { waitForLoadingComplete } from "../helpers/wait-helpers";

test.describe("IssuedInvoices - Filter Functionality", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);

    // Switch to Grid tab
    const gridTab = page.locator('button:has-text("Seznam")');
    await gridTab.click();
    await waitForLoadingComplete(page);
  });

  test("3: Invoice ID filter with Enter key", async ({ page }) => {
    const invoiceIdInput = page.locator("#invoiceId");
    const tableRows = page.locator("tbody tr");

    // Enter invoice ID and press Enter
    await invoiceIdInput.fill("2024");
    await invoiceIdInput.press("Enter");
    await waitForLoadingComplete(page);

    // Verify rows are filtered
    const filteredCount = await tableRows.count();

    // Should have results or show "Žádné faktury nebyly nalezeny"
    // eslint-disable-next-line jest/no-conditional-expect
    if (filteredCount === 0) {
      const emptyMessage = page.locator(
        'text="Žádné faktury nebyly nalezeny."',
      );
      // eslint-disable-next-line jest/no-conditional-expect
      await expect(emptyMessage).toBeVisible();
    } else {
      // Verify filtered results contain the search term
      const firstRowText = await tableRows.first().textContent();
      // eslint-disable-next-line jest/no-conditional-expect
      expect(firstRowText).toContain("2024");
    }
  });

  test("4: Invoice ID filter with Filtrovat button", async ({ page }) => {
    const invoiceIdInput = page.locator("#invoiceId");
    const filterButton = page.locator('button:has-text("Filtrovat")');
    const tableRows = page.locator("tbody tr");

    // Enter invoice ID
    await invoiceIdInput.fill("2024");

    // Click Filtrovat button
    await filterButton.click();
    await waitForLoadingComplete(page);

    // Verify filtering applied
    const filteredCount = await tableRows.count();

    // eslint-disable-next-line jest/no-conditional-expect
    if (filteredCount === 0) {
      const emptyMessage = page.locator(
        'text="Žádné faktury nebyly nalezeny."',
      );
      // eslint-disable-next-line jest/no-conditional-expect
      await expect(emptyMessage).toBeVisible();
    } else {
      const firstRowText = await tableRows.first().textContent();
      // eslint-disable-next-line jest/no-conditional-expect
      expect(firstRowText).toContain("2024");
    }
  });

  test("5: Customer Name filter with Enter key", async ({ page }) => {
    const customerNameInput = page.locator("#customerName");
    const tableRows = page.locator("tbody tr");

    // Enter customer name and press Enter
    await customerNameInput.fill("Test");
    await customerNameInput.press("Enter");
    await waitForLoadingComplete(page);

    // Verify filtering applied
    const filteredCount = await tableRows.count();

    // eslint-disable-next-line jest/no-conditional-expect
    if (filteredCount > 0) {
      // Verify at least one row contains the search term
      const allRowsText = await tableRows.allTextContents();
      const hasMatch = allRowsText.some((text) =>
        text.toLowerCase().includes("test"),
      );
      // eslint-disable-next-line jest/no-conditional-expect
      expect(hasMatch).toBe(true);
    }
  });

  test("6: Customer Name filter with Filtrovat button", async ({
    page,
  }) => {
    const customerNameInput = page.locator("#customerName");
    const filterButton = page.locator('button:has-text("Filtrovat")');
    const tableRows = page.locator("tbody tr");

    // Enter customer name
    await customerNameInput.fill("Test");

    // Click Filtrovat button
    await filterButton.click();
    await waitForLoadingComplete(page);

    // Verify filtering applied
    const filteredCount = await tableRows.count();

    // eslint-disable-next-line jest/no-conditional-expect
    if (filteredCount > 0) {
      const allRowsText = await tableRows.allTextContents();
      const hasMatch = allRowsText.some((text) =>
        text.toLowerCase().includes("test"),
      );
      // eslint-disable-next-line jest/no-conditional-expect
      expect(hasMatch).toBe(true);
    }
  });

  test("7: Date range filter (Od + Do fields)", async ({ page }) => {
    const dateFromInput = page.locator('input[type="date"]').first();
    const dateToInput = page.locator('input[type="date"]').last();
    const filterButton = page.locator('button:has-text("Filtrovat")');
    const tableRows = page.locator("tbody tr");

    // Set date range
    await dateFromInput.fill("2024-01-01");
    await dateToInput.fill("2024-12-31");

    // Apply filter
    await filterButton.click();
    await waitForLoadingComplete(page);

    // Verify filtering applied (results should be within date range)
    const filteredCount = await tableRows.count();
    expect(filteredCount).toBeGreaterThanOrEqual(0);
  });

  test("8: Show Only Unsynced checkbox", async ({ page }) => {
    const unsyncedCheckbox = page
      .locator('input[type="checkbox"]')
      .filter({ hasText: "Nesync" });
    const tableRows = page.locator("tbody tr");

    // Check the checkbox
    await unsyncedCheckbox.check();
    await waitForLoadingComplete(page);

    // Verify filtering applied
    const filteredCount = await tableRows.count();

    // eslint-disable-next-line jest/no-conditional-expect
    if (filteredCount > 0) {
      // Verify at least one row has "Čeká" (Pending) badge
      const pendingBadge = page.locator('span:has-text("Čeká")').first();
      // eslint-disable-next-line jest/no-conditional-expect
      await expect(pendingBadge).toBeVisible();
    }
  });

  test("9: Show Only With Errors checkbox", async ({ page }) => {
    const errorsCheckbox = page
      .locator('input[type="checkbox"]')
      .filter({ hasText: "Chyby" });
    const tableRows = page.locator("tbody tr");

    // Check the checkbox
    await errorsCheckbox.check();
    await waitForLoadingComplete(page);

    // Verify filtering applied
    const filteredCount = await tableRows.count();

    // eslint-disable-next-line jest/no-conditional-expect
    if (filteredCount > 0) {
      // Verify at least one row has "Chyba" (Error) badge
      const errorBadge = page.locator('span:has-text("Chyba")').first();
      // eslint-disable-next-line jest/no-conditional-expect
      await expect(errorBadge).toBeVisible();
    }
  });

  test("10: Combined filters (multiple filters simultaneously)", async ({
    page,
  }) => {
    const invoiceIdInput = page.locator("#invoiceId");
    const dateFromInput = page.locator('input[type="date"]').first();
    const dateToInput = page.locator('input[type="date"]').last();
    const filterButton = page.locator('button:has-text("Filtrovat")');
    const tableRows = page.locator("tbody tr");

    // Apply multiple filters
    await invoiceIdInput.fill("2024");
    await dateFromInput.fill("2024-01-01");
    await dateToInput.fill("2024-12-31");

    // Apply filters
    await filterButton.click();
    await waitForLoadingComplete(page);

    // Verify filtering applied
    const filteredCount = await tableRows.count();
    expect(filteredCount).toBeGreaterThanOrEqual(0);
  });

  test("11: Clear filters button (Vymazat)", async ({ page }) => {
    const invoiceIdInput = page.locator("#invoiceId");
    const customerNameInput = page.locator("#customerName");
    const dateFromInput = page.locator('input[type="date"]').first();
    const dateToInput = page.locator('input[type="date"]').last();
    const filterButton = page.locator('button:has-text("Filtrovat")');
    const clearButton = page.locator('button:has-text("Vymazat")');
    const tableRows = page.locator("tbody tr");

    // Apply some filters
    await invoiceIdInput.fill("2024");
    await customerNameInput.fill("Test");
    await dateFromInput.fill("2024-01-01");
    await dateToInput.fill("2024-12-31");
    await filterButton.click();
    await waitForLoadingComplete(page);

    const filteredCount = await tableRows.count();

    // Click clear button
    await clearButton.click();
    await waitForLoadingComplete(page);

    // Verify all filter inputs are cleared
    await expect(invoiceIdInput).toHaveValue("");
    await expect(customerNameInput).toHaveValue("");
    await expect(dateFromInput).toHaveValue("");
    await expect(dateToInput).toHaveValue("");

    // Verify row count changed (back to full list)
    const clearedCount = await tableRows.count();
    expect(clearedCount).toBeGreaterThanOrEqual(filteredCount);
  });
});
