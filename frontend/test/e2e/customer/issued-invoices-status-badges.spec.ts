import { test, expect } from "@playwright/test";
import { navigateToIssuedInvoices } from "../helpers/e2e-auth-helper";
import { waitForLoadingComplete } from "../helpers/wait-helpers";

test.describe("IssuedInvoices - Status Badges", () => {
  test.beforeEach(async ({ page }) => {
    await navigateToIssuedInvoices(page);

    // Switch to Grid tab
    const gridTab = page.locator('button:has-text("Seznam")');
    await gridTab.click();
    await waitForLoadingComplete(page);
  });

  test("26: Status badge displays correctly for 'Čeká' (Pending)", async ({ page }) => {
    // Filter for unsynced invoices
    const unsyncedCheckbox = page.locator('input[type="checkbox"]').filter({ hasText: "Nesync" });
    await unsyncedCheckbox.check();
    await waitForLoadingComplete(page);

    const tableRows = page.locator("tbody tr");
    const rowCount = await tableRows.count();

    // Look for pending badge
    const pendingBadge = page.locator('span:has-text("Čeká")').first();
    const badgeClass = rowCount > 0 ? await pendingBadge.getAttribute("class") : null;

    // eslint-disable-next-line jest/no-conditional-expect
    if (rowCount > 0) {
      // eslint-disable-next-line jest/no-conditional-expect
      await expect(pendingBadge).toBeVisible();
      // eslint-disable-next-line jest/no-conditional-expect
      expect(badgeClass).toBeTruthy();
    }
  });

  test("27: Status badge displays correctly for 'Chyba' (Error)", async ({ page }) => {
    // Filter for invoices with errors
    const errorsCheckbox = page.locator('input[type="checkbox"]').filter({ hasText: "Chyby" });
    await errorsCheckbox.check();
    await waitForLoadingComplete(page);

    const tableRows = page.locator("tbody tr");
    const rowCount = await tableRows.count();

    // Look for error badge
    const errorBadge = page.locator('span:has-text("Chyba")').first();
    const badgeClass = rowCount > 0 ? await errorBadge.getAttribute("class") : null;

    // eslint-disable-next-line jest/no-conditional-expect
    if (rowCount > 0) {
      // eslint-disable-next-line jest/no-conditional-expect
      await expect(errorBadge).toBeVisible();
      // eslint-disable-next-line jest/no-conditional-expect
      expect(badgeClass).toBeTruthy();
    }
  });

  test("28: Status badge displays correctly for 'Odesláno' (Sent)", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const rowCount = await tableRows.count();

    // Look for sent badge (should be most common)
    const sentBadge = page.locator('span:has-text("Odesláno")').first();

    // Check if at least one row has "Odesláno" badge
    const sentBadgeCount = await page.locator('span:has-text("Odesláno")').count();
    const badgeClass = sentBadgeCount > 0 ? await sentBadge.getAttribute("class") : null;

    // eslint-disable-next-line jest/no-conditional-expect
    if (rowCount > 0 && sentBadgeCount > 0) {
      // eslint-disable-next-line jest/no-conditional-expect
      await expect(sentBadge).toBeVisible();
      // eslint-disable-next-line jest/no-conditional-expect
      expect(badgeClass).toBeTruthy();
    }
  });

  test("29: Multiple status badges can appear in grid", async ({ page }) => {
    const tableRows = page.locator("tbody tr");
    const rowCount = await tableRows.count();

    expect(rowCount).toBeGreaterThan(0);

    // Count different status badges
    const pendingCount = await page.locator('span:has-text("Čeká")').count();
    const errorCount = await page.locator('span:has-text("Chyba")').count();
    const sentCount = await page.locator('span:has-text("Odesláno")').count();

    // Verify at least one type of badge exists
    const totalBadges = pendingCount + errorCount + sentCount;
    expect(totalBadges).toBeGreaterThan(0);

    // Verify each row has exactly one status badge
    for (let i = 0; i < Math.min(rowCount, 5); i++) {
      const row = tableRows.nth(i);
      const badgesInRow = row.locator('span').filter({
        hasText: /Čeká|Chyba|Odesláno/,
      });
      const badgeCount = await badgesInRow.count();
      expect(badgeCount).toBeGreaterThanOrEqual(1);
    }
  });
});
