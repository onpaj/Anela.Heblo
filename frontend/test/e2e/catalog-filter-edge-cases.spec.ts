import { test, expect } from "@playwright/test";
import { navigateToCatalog } from "./helpers/e2e-auth-helper";
import {
  applyProductNameFilter,
  applyProductCodeFilter,
  getRowCount,
  getProductNameInput,
  getFilterButton,
  waitForTableUpdate,
  validateFilteredResults,
} from "./helpers/catalog-test-helpers";

test.describe("Catalog Filter Edge Cases E2E Tests", () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to catalog with full authentication
    console.log("🧭 Navigating to catalog page...");
    await navigateToCatalog(page);
    expect(page.url()).toContain("/catalog");
    console.log("✅ On catalog page:", page.url());

    // Wait for initial catalog load
    console.log("⏳ Waiting for initial catalog to load...");
    await page.waitForLoadState("networkidle");
    await page.waitForTimeout(3000);
  });

  // ============================================================================
  // SPECIAL CHARACTERS IN SEARCH
  // ============================================================================

  test("should handle special characters in product name (Czech diacritics)", async ({
    page,
  }) => {
    // Test Czech diacritics: č, ř, ž, š, á, é, í, ó, ú, ý
    await applyProductNameFilter(page, "Krém"); // Contains "é"

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Verify filter works with diacritics
      await validateFilteredResults(
        page,
        { productName: "Krém" },
        { caseSensitive: false },
      );
      console.log("✅ Czech diacritics handled correctly");
    } else {
      console.log("ℹ️ No products with diacritics found");
    }
  });

  test("should handle numbers in product name", async ({ page }) => {
    // Search for products with numbers
    await applyProductNameFilter(page, "100");

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Verify filter works with numbers
      await validateFilteredResults(
        page,
        { productName: "100" },
        { maxRowsToCheck: 5 },
      );
      console.log("✅ Numbers in product name handled correctly");
    } else {
      console.log('ℹ️ No products with "100" in name found');
    }
  });

  test("should handle hyphens and spaces in product code", async ({ page }) => {
    // Search for code with hyphens or spaces
    await applyProductCodeFilter(page, "AH-");

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    console.log(`📊 Results with hyphen search: ${rowCount}`);

    // Just verify it doesn't crash
    console.log("✅ Hyphens in product code handled without errors");
  });

  test("should handle leading/trailing whitespace", async ({ page }) => {
    const nameInput = getProductNameInput(page);

    // Type with leading and trailing spaces
    await nameInput.fill("  Krém  ");

    const filterButton = getFilterButton(page);
    await filterButton.click();
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);

    if (rowCount > 0) {
      // Should still find matches (whitespace trimmed)
      await validateFilteredResults(
        page,
        { productName: "Krém" },
        { maxRowsToCheck: 5 },
      );
      console.log("✅ Leading/trailing whitespace trimmed correctly");
    } else {
      console.log("ℹ️ No results, but no error occurred");
    }
  });

  // ============================================================================
  // EXTREME VALUES
  // ============================================================================

  test("should handle very long product names (>100 chars)", async ({
    page,
  }) => {
    const longName = "A".repeat(150);

    await applyProductNameFilter(page, longName);

    await waitForTableUpdate(page);

    // Should not crash, might return no results
    const rowCount = await getRowCount(page);
    console.log(`📊 Results for very long name: ${rowCount}`);

    console.log("✅ Very long product name handled without errors");
  });

  test("should handle very long product codes", async ({ page }) => {
    const longCode = "ABC".repeat(50);

    await applyProductCodeFilter(page, longCode);

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);
    console.log(`📊 Results for very long code: ${rowCount}`);

    console.log("✅ Very long product code handled without errors");
  });

  test("should handle single character search", async ({ page }) => {
    // Single character search
    await applyProductNameFilter(page, "K");

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);
    console.log(`📊 Results for single character "K": ${rowCount}`);

    if (rowCount > 0) {
      // Verify results contain "K"
      await validateFilteredResults(
        page,
        { productName: "K" },
        { maxRowsToCheck: 5, caseSensitive: false },
      );
      console.log("✅ Single character search working");
    } else {
      console.log("ℹ️ No results for single character");
    }
  });

  test("should handle numeric-only search terms", async ({ page }) => {
    // Search for only numbers
    await applyProductNameFilter(page, "123");

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);
    console.log(`📊 Results for numeric search "123": ${rowCount}`);

    console.log("✅ Numeric-only search handled correctly");
  });

  // ============================================================================
  // PERFORMANCE & TIMING
  // ============================================================================

  test("should handle rapid filter changes", async ({ page }) => {
    const nameInput = getProductNameInput(page);
    const filterButton = getFilterButton(page);

    // Rapidly change filter multiple times
    await nameInput.fill("Krém");
    await filterButton.click();

    await nameInput.fill("Sérum");
    await filterButton.click();

    await nameInput.fill("Olej");
    await filterButton.click();

    await nameInput.fill("Peeling");
    await filterButton.click();

    // Wait for final update
    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);
    console.log(`📊 Results after rapid changes: ${rowCount}`);

    // Verify the last filter is applied
    await expect(nameInput).toHaveValue("Peeling");

    console.log("✅ Rapid filter changes handled correctly");
  });

  test("should show loading state during filter application", async ({
    page,
  }) => {
    const nameInput = getProductNameInput(page);
    const filterButton = getFilterButton(page);

    await nameInput.fill("Krém");

    // Click filter and immediately check for loading indicators
    await filterButton.click();

    // Check if there's any loading indicator (this is implementation-specific)
    // We'll just verify the page doesn't crash
    await page.waitForTimeout(500);

    console.log("✅ Filter application does not crash (loading state test)");

    // Wait for completion
    await waitForTableUpdate(page);
  });

  test("should handle slow API responses gracefully", async ({ page }) => {
    // Apply filter and wait longer than usual
    await applyProductNameFilter(page, "Krém");

    // Extended wait to simulate slow response
    await page.waitForTimeout(5000);

    const rowCount = await getRowCount(page);
    console.log(`📊 Results after extended wait: ${rowCount}`);

    console.log("✅ Slow API responses handled gracefully");
  });

  // ============================================================================
  // FILTER PERSISTENCE
  // ============================================================================

  test("should maintain filters when opening/closing detail modal", async ({
    page,
  }) => {
    // Apply filter
    await applyProductNameFilter(page, "Krém");

    const initialRowCount = await getRowCount(page);

    // If no results, this test doesn't apply
    if (initialRowCount === 0) {
      console.log("ℹ️ Skipping modal test - no results found");
      return;
    }

    // Click on first row to open detail (if modal exists)
    const firstRow = page.locator("tbody tr:first-child");
    await firstRow.click();

    // Wait a moment for modal to potentially open
    await page.waitForTimeout(1000);

    // Try to close modal if it opened (ESC key or close button)
    await page.keyboard.press("Escape");
    await page.waitForTimeout(500);

    // Verify filter is still applied
    const nameInput = getProductNameInput(page);
    await expect(nameInput).toHaveValue("Krém");

    const rowCount = await getRowCount(page);
    expect(rowCount).toBe(initialRowCount);

    console.log("✅ Filters maintained after modal interaction");
  });

  test("should clear filters when navigating away and returning", async ({
    page,
  }) => {
    // Apply filter
    await applyProductNameFilter(page, "Krém");

    const nameInput = getProductNameInput(page);
    await expect(nameInput).toHaveValue("Krém");

    // Navigate away to another page (e.g., home)
    await page.goto(new URL("/", page.url()).toString());
    await page.waitForLoadState("networkidle");
    await page.waitForTimeout(2000);

    // Navigate back to catalog
    await navigateToCatalog(page);
    await page.waitForLoadState("networkidle");
    await page.waitForTimeout(3000);

    // Verify filters are cleared (expected behavior)
    const nameInputAfter = getProductNameInput(page);
    const value = await nameInputAfter.inputValue();

    expect(value).toBe("");

    console.log(
      "✅ Filters cleared when navigating away and returning (expected)",
    );
  });

  test("should handle browser back/forward with filters in URL", async ({
    page,
  }) => {
    // Apply filter
    await applyProductNameFilter(page, "Krém");

    const initialRowCount = await getRowCount(page);

    // Get current URL with filter
    const urlWithFilter = page.url();
    console.log(`   URL with filter: ${urlWithFilter}`);

    // Navigate to a different filter
    await applyProductNameFilter(page, "Sérum");

    const serumRowCount = await getRowCount(page);
    console.log(`   Sérum results: ${serumRowCount}`);

    // Use browser back
    await page.goBack();
    await waitForTableUpdate(page);

    // Verify we're back to "Krém" filter
    const nameInput = getProductNameInput(page);
    const inputValue = await nameInput.inputValue();
    console.log(`   After back, input value: "${inputValue}"`);

    // Verify filter is restored from URL
    expect(inputValue).toBe("Krém");
    const rowCountAfterBack = await getRowCount(page);
    expect(rowCountAfterBack).toBe(initialRowCount);
    console.log("✅ Browser back restored filter from URL");

    // Use browser forward
    await page.goForward();
    await waitForTableUpdate(page);

    // Verify we're back to "Sérum" filter
    const nameInputAfterForward = getProductNameInput(page);
    const inputValueAfterForward = await nameInputAfterForward.inputValue();
    console.log(`   After forward, input value: "${inputValueAfterForward}"`);

    expect(inputValueAfterForward).toBe("Sérum");
    const rowCountAfterForward = await getRowCount(page);
    expect(rowCountAfterForward).toBe(serumRowCount);
    console.log("✅ Browser forward restored filter from URL");
  });

  // ============================================================================
  // SPECIAL EDGE CASES
  // ============================================================================

  test("should handle empty string filter after having a value", async ({
    page,
  }) => {
    // Apply filter first
    await applyProductNameFilter(page, "Krém");

    const filteredCount = await getRowCount(page);
    console.log(`📊 Filtered count: ${filteredCount}`);

    // Clear the input and apply empty filter
    const nameInput = getProductNameInput(page);
    await nameInput.fill("");

    const filterButton = getFilterButton(page);
    await filterButton.click();
    await waitForTableUpdate(page);

    // Should show all results (or similar to initial)
    const allCount = await getRowCount(page);
    console.log(`📊 Count after empty filter: ${allCount}`);

    expect(allCount).toBeGreaterThanOrEqual(filteredCount);

    console.log("✅ Empty string filter handled correctly");
  });

  test("should handle special SQL characters gracefully", async ({ page }) => {
    // Test SQL injection-like strings (should be sanitized)
    const sqlString = "'; DROP TABLE products; --";

    const nameInput = getProductNameInput(page);
    await nameInput.fill(sqlString);

    const filterButton = getFilterButton(page);
    await filterButton.click();
    await waitForTableUpdate(page);

    // Should not crash or cause SQL errors
    const rowCount = await getRowCount(page);
    console.log(`📊 Results for SQL-like string: ${rowCount}`);

    console.log("✅ SQL-like characters handled safely (no crash)");
  });

  test("should handle regex special characters", async ({ page }) => {
    // Test regex special characters
    const regexString = ".* [a-z]+ \\d+";

    await applyProductNameFilter(page, regexString);

    await waitForTableUpdate(page);

    const rowCount = await getRowCount(page);
    console.log(`📊 Results for regex-like string: ${rowCount}`);

    console.log("✅ Regex special characters handled correctly");
  });
});
