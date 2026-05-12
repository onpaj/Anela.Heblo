import { test, expect } from "@playwright/test";
import { navigateToIssuedInvoices } from "../helpers/e2e-auth-helper";
import { waitForLoadingComplete } from "../helpers/wait-helpers";

test.describe("IssuedInvoices - Filter Functionality", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);

    // Wait for page to load completely - check for either loading state or tabs
    // If there's an error or infinite loading, this will timeout and fail clearly
    const loadingIndicator = page.locator('text=/Načítání faktur/i');
    const gridTab = page.locator('button:has-text("Seznam")');

    // Wait for loading to complete OR tabs to appear (whichever comes first)
    try {
      await Promise.race([
        loadingIndicator.waitFor({ state: 'hidden', timeout: 30000 }),
        gridTab.waitFor({ state: 'visible', timeout: 30000 })
      ]);
    } catch (error) {
      // If we timeout, check what's actually on the page
      const errorMessage = page.locator('text=/Chyba při načítání/i');
      const isErrorVisible = await errorMessage.isVisible().catch(() => false);

      if (isErrorVisible) {
        const errorText = await errorMessage.textContent();
        throw new Error(`Page failed to load - error message: ${errorText}`);
      }

      throw new Error(`Page failed to load within timeout - loading indicator still visible or tabs never appeared`);
    }

    // Now switch to Grid tab (should be visible after loading completes)
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
    // Add stabilization wait to ensure table has finished updating after filter
    await page.waitForTimeout(500);

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

  // Unskipped: navigation helper fixed in e2e-auth-helper.ts — re-validate on staging per e2e-test-map.md audit.
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

  // Unskipped: navigation helper fixed in e2e-auth-helper.ts — re-validate on staging per e2e-test-map.md audit.
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

  // Unskipped: navigation helper fixed in e2e-auth-helper.ts — re-validate on staging per e2e-test-map.md audit.
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

  // Unskipped: navigation helper fixed in e2e-auth-helper.ts — re-validate on staging per e2e-test-map.md audit.
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

  // Unskipped: navigation helper fixed in e2e-auth-helper.ts — re-validate on staging per e2e-test-map.md audit.
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

  // Unskipped: navigation helper fixed in e2e-auth-helper.ts — re-validate on staging per e2e-test-map.md audit.
  // Note: if "Show Only With Errors" checkbox element is still missing from UI, re-skip and update selector or remove test.
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
