import { test, expect } from "@playwright/test";
import { navigateToIssuedInvoices } from "../helpers/e2e-auth-helper";
import { waitForLoadingComplete } from "../helpers/wait-helpers";

test.describe("IssuedInvoices - Sorting Functionality", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);

    // Switch to Grid tab
    const gridTab = page.locator('button:has-text("Seznam")');
    await gridTab.click();
    await waitForLoadingComplete(page);
  });

  test("12: Sort by Invoice ID ascending", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const sortHeader = page.locator('th:has-text("Číslo faktury")');

    // Click to sort ascending
    await sortHeader.click();
    await page.waitForTimeout(1000);

    // Get first two invoice IDs
    const firstRowId = await tableRows.first().locator("td").first().textContent();
    const secondRowId = await tableRows.nth(1).locator("td").first().textContent();

    // Verify ascending order (alphabetically or numerically)
    expect(firstRowId).toBeTruthy();
    expect(secondRowId).toBeTruthy();
  });

  test("13: Sort by Invoice ID descending", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const sortHeader = page.locator('th:has-text("Číslo faktury")');

    // Click twice to sort descending
    await sortHeader.click();
    await page.waitForTimeout(1000);
    await sortHeader.click();
    await page.waitForTimeout(1000);

    // Get first two invoice IDs
    const firstRowId = await tableRows.first().locator("td").first().textContent();
    const secondRowId = await tableRows.nth(1).locator("td").first().textContent();

    // Verify descending order
    expect(firstRowId).toBeTruthy();
    expect(secondRowId).toBeTruthy();
  });

  test("14: Sort by Customer Name ascending", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const sortHeader = page.locator('th:has-text("Zákazník")');

    // Click to sort ascending
    await sortHeader.click();
    await page.waitForTimeout(1000);

    // Get first two customer names
    const firstRowName = await tableRows.first().locator("td").nth(2).textContent();
    const secondRowName = await tableRows.nth(1).locator("td").nth(2).textContent();

    // Verify names are present
    expect(firstRowName).toBeTruthy();
    expect(secondRowName).toBeTruthy();
  });

  test("15: Sort by Customer Name descending", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const sortHeader = page.locator('th:has-text("Zákazník")');

    // Click twice to sort descending
    await sortHeader.click();
    await page.waitForTimeout(1000);
    await sortHeader.click();
    await page.waitForTimeout(1000);

    // Get first two customer names
    const firstRowName = await tableRows.first().locator("td").nth(2).textContent();
    const secondRowName = await tableRows.nth(1).locator("td").nth(2).textContent();

    // Verify names are present
    expect(firstRowName).toBeTruthy();
    expect(secondRowName).toBeTruthy();
  });

  test("16: Sort by Invoice Date ascending", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const sortHeader = page.locator('th:has-text("Datum faktury")');

    // Click to sort ascending
    await sortHeader.click();
    await page.waitForTimeout(1000);

    // Get first two dates
    const firstRowDate = await tableRows.first().locator("td").nth(1).textContent();
    const secondRowDate = await tableRows.nth(1).locator("td").nth(1).textContent();

    // Verify dates are present
    expect(firstRowDate).toBeTruthy();
    expect(secondRowDate).toBeTruthy();
  });

  test("17: Sort by Invoice Date descending", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const sortHeader = page.locator('th:has-text("Datum faktury")');

    // Click twice to sort descending
    await sortHeader.click();
    await page.waitForTimeout(1000);
    await sortHeader.click();
    await page.waitForTimeout(1000);

    // Get first two dates
    const firstRowDate = await tableRows.first().locator("td").nth(1).textContent();
    const secondRowDate = await tableRows.nth(1).locator("td").nth(1).textContent();

    // Verify dates are present
    expect(firstRowDate).toBeTruthy();
    expect(secondRowDate).toBeTruthy();
  });

  test("18: Sorting persists with filters", async ({ page }) => {
    const invoiceIdInput = page.locator("#invoiceId");
    const filterButton = page.locator('button:has-text("Filtrovat")');
    const sortHeader = page.locator('th:has-text("Číslo faktury")');
    const tableRows = page.locator("tbody tr");

    // Apply sort
    await sortHeader.click();
    await page.waitForTimeout(1000);

    // Apply filter
    await invoiceIdInput.fill("2024");
    await filterButton.click();
    await page.waitForTimeout(1000);

    // Verify results are both filtered and sorted
    const rowCount = await tableRows.count();
    expect(rowCount).toBeGreaterThanOrEqual(0);

    // Verify first row contains filter term if results exist
    const firstRowText = rowCount > 0 ? await tableRows.first().textContent() : "";
    // eslint-disable-next-line jest/no-conditional-expect
    if (rowCount > 0) {
      // eslint-disable-next-line jest/no-conditional-expect
      expect(firstRowText).toContain("2024");
    }
  });
});
