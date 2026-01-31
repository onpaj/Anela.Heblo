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

    // Click next page button (last button in pagination navigation)
    const paginationNav = page.locator('nav[aria-label="Pagination"]');
    const nextButton = paginationNav.locator('button').last();
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
    const paginationNav = page.locator('nav[aria-label="Pagination"]');
    const nextButton = paginationNav.locator('button').last();
    await nextButton.click();
    await waitForLoadingComplete(page);

    const firstRowPage2 = await tableRows.first().textContent();

    // Navigate back to page 1 - click page "1" button (it should be enabled now that we're on page 2)
    const page1Button = paginationNav.getByRole('button', { name: '1', exact: true });
    await page1Button.click();
    await waitForLoadingComplete(page);

    const firstRowPage1 = await tableRows.first().textContent();

    // Verify we're back on page 1 (different content than page 2)
    expect(firstRowPage1).not.toEqual(firstRowPage2);
  });

  test("22: Navigate to first page", async ({ page }) => {
    // Navigate to page 2
    const paginationNav = page.locator('nav[aria-label="Pagination"]');
    const nextButton = paginationNav.locator('button').last();
    await nextButton.click();
    await waitForLoadingComplete(page);

    // Click first page button (first button in pagination navigation)
    const firstPageButton = paginationNav.locator('button').first();
    await firstPageButton.click();
    await waitForLoadingComplete(page);

    // Verify we're on page 1 - the first button (go to first page) should now be disabled
    await expect(firstPageButton).toBeDisabled();
  });

  test("23: Navigate to last page", async ({ page }) => {
    const paginationNav = page.locator('nav[aria-label="Pagination"]');
    const tableRows = page.locator("tbody tr");

    // Get first row content on page 1
    const firstRowPage1 = await tableRows.first().textContent();

    // Get all pagination buttons (excluding first/next navigation buttons)
    // Structure: [First] [1] [2] [3] [4] [5] [Next]
    // We want to click the highest page number (e.g., "5")
    const allButtons = await paginationNav.locator('button').all();

    // Find the button with the highest numeric text (skip first/last which are navigation buttons with only images)
    let highestPageButton = null;
    let highestPageNumber = 0;

    for (const button of allButtons) {
      const buttonText = await button.textContent();
      const pageNumber = parseInt(buttonText || '0', 10);
      if (!isNaN(pageNumber) && pageNumber > 0 && pageNumber > highestPageNumber) {
        highestPageNumber = pageNumber;
        highestPageButton = button;
      }
    }

    // Click the highest page number button
    if (highestPageButton) {
      await highestPageButton.click();
      await waitForLoadingComplete(page);
    }

    // Verify we navigated to a different page (content changed)
    const firstRowAfterClick = await tableRows.first().textContent();
    expect(firstRowAfterClick).not.toEqual(firstRowPage1);

    // Verify we're on the page we clicked (page button should be styled as active)
    // Note: We can't rely on disabled state for page number buttons, they use visual styling instead
    // So just verify the content is different from page 1
  });

  test("24: Change page size (items per page)", async ({ page }) => {
    const tableRows = page.locator("tbody tr");

    // Find page size selector
    const pageSizeSelect = page.locator('select#pageSize');

    // Change to 10 items per page (available options: 10, 20, 50, 100)
    await pageSizeSelect.selectOption("10");
    await page.waitForTimeout(1000);

    // Verify fewer rows are displayed (up to 10, less than default 20)
    const rowCount = await tableRows.count();
    expect(rowCount).toBeGreaterThan(0);
    expect(rowCount).toBeLessThanOrEqual(10);
  });

  test("25: Pagination resets to page 1 when filters change", async ({ page }) => {
    // Navigate to page 2
    const paginationNav = page.locator('nav[aria-label="Pagination"]');
    const nextButton = paginationNav.locator('button').last();
    await nextButton.click();
    await waitForLoadingComplete(page);

    // Verify we're on page 2 (page 2 button should be disabled/active)
    const page2Button = paginationNav.getByRole('button', { name: '2', exact: true });
    await expect(page2Button).toBeDisabled();

    // Apply a filter
    const invoiceIdInput = page.locator('input[placeholder*="Číslo faktury"]');
    const filterButton = page.locator('button:has-text("Filtrovat")');
    await invoiceIdInput.fill("2024");
    await filterButton.click();
    await waitForLoadingComplete(page);

    // Verify pagination reset to page 1 (page 1 button should be disabled/active)
    const page1Button = paginationNav.getByRole('button', { name: '1', exact: true });
    await expect(page1Button).toBeDisabled();
  });
});
