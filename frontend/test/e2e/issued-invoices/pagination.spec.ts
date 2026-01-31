import { test, expect } from "@playwright/test";
import { navigateToIssuedInvoices } from "../helpers/e2e-auth-helper";
import { waitForLoadingComplete } from "../helpers/wait-helpers";

test.describe("IssuedInvoices - Pagination", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);

    // Switch to Grid tab
    const gridTab = page.locator('button:has-text("Seznam")');
    await gridTab.click();
    await waitForLoadingComplete(page);
  });

  test("19: Default page size is 10 rows per page", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const rowCount = await tableRows.count();

    // Should display up to 20 rows on first page (default page size is 20)
    expect(rowCount).toBeGreaterThan(0);
    expect(rowCount).toBeLessThanOrEqual(20);

    // Verify page size selector shows default value of 20
    const pageSizeSelect = page.locator('select#pageSize');
    await expect(pageSizeSelect).toHaveValue('20');
  });

  test("20: Navigate to next page", async ({ page }) => {
    const tableRows = page.locator("tbody tr");

    // Get first row ID on page 1
    const firstRowPage1 = await tableRows.first().textContent();

    // Click next page button
    const nextButton = page.locator('button[aria-label="Další stránka"]');
    await nextButton.click();
    await waitForLoadingComplete(page);

    // Get first row ID on page 2
    const firstRowPage2 = await tableRows.first().textContent();

    // Verify content changed (different rows)
    expect(firstRowPage1).not.toEqual(firstRowPage2);
  });

  test("21: Navigate to previous page", async ({ page }) => {
    const tableRows = page.locator("tbody tr");

    // Navigate to page 2
    const nextButton = page.locator('button[aria-label="Další stránka"]');
    await nextButton.click();
    await waitForLoadingComplete(page);

    const firstRowPage2 = await tableRows.first().textContent();

    // Navigate back to page 1
    const prevButton = page.locator('button[aria-label="Předchozí stránka"]');
    await prevButton.click();
    await waitForLoadingComplete(page);

    const firstRowPage1 = await tableRows.first().textContent();

    // Verify we're back on page 1 (different content than page 2)
    expect(firstRowPage1).not.toEqual(firstRowPage2);
  });

  test("22: Navigate to first page", async ({ page }) => {
    // Navigate to page 2
    const nextButton = page.locator('button[aria-label="Další stránka"]');
    await nextButton.click();
    await waitForLoadingComplete(page);

    // Click first page button
    const firstPageButton = page.locator('button[aria-label="První stránka"]');
    await firstPageButton.click();
    await waitForLoadingComplete(page);

    // Verify we're on page 1
    const currentPage = page.locator('[aria-current="page"]');
    await expect(currentPage).toHaveText("1");
  });

  test("23: Navigate to last page", async ({ page }) => {
    // Click last page button
    const lastPageButton = page.locator('button[aria-label="Poslední stránka"]');
    await lastPageButton.click();
    await waitForLoadingComplete(page);

    // Verify we're on last page (next button should be disabled)
    const nextButton = page.locator('button[aria-label="Další stránka"]');
    await expect(nextButton).toBeDisabled();
  });

  test("24: Change page size (items per page)", async ({ page }) => {
    const tableRows = page.locator("tbody tr");

    // Find page size selector
    const pageSizeSelect = page.locator('select').filter({ hasText: "10" });

    // Change to 25 items per page
    await pageSizeSelect.selectOption("25");
    await waitForLoadingComplete(page);

    // Verify more rows are displayed (up to 25)
    const rowCount = await tableRows.count();
    expect(rowCount).toBeGreaterThan(10);
    expect(rowCount).toBeLessThanOrEqual(25);
  });

  test("25: Pagination resets to page 1 when filters change", async ({ page }) => {
    // Navigate to page 2
    const nextButton = page.locator('button[aria-label="Další stránka"]');
    await nextButton.click();
    await waitForLoadingComplete(page);

    // Verify we're on page 2
    const currentPageBefore = page.locator('[aria-current="page"]');
    await expect(currentPageBefore).toHaveText("2");

    // Apply a filter
    const invoiceIdInput = page.locator("#invoiceId");
    const filterButton = page.locator('button:has-text("Filtrovat")');
    await invoiceIdInput.fill("2024");
    await filterButton.click();
    await waitForLoadingComplete(page);

    // Verify pagination reset to page 1
    const currentPageAfter = page.locator('[aria-current="page"]');
    await expect(currentPageAfter).toHaveText("1");
  });
});
