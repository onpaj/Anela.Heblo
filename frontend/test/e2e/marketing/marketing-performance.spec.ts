import { test, expect, type Locator, type Page } from '@playwright/test';
import { navigateToMarketingPerformance } from '../helpers/e2e-auth-helper';

const NO_DATA_MESSAGE =
  'Staging has no marketing performance rows — run the recompute on staging before the nightly suite (see Task 21 step 3).';

// Column count and order come from the API's `channels` list (PerformanceTable.tsx), so a
// fixed nth() index into the row would silently point at the wrong column whenever a channel
// is added or removed. Resolve the index from the header text instead, every time it's needed.
async function findColumnIndex(table: Locator, headerName: string): Promise<number> {
  const headers = table.getByRole('columnheader');
  const count = await headers.count();
  for (let i = 0; i < count; i += 1) {
    const text = (await headers.nth(i).textContent())?.trim();
    if (text === headerName) {
      return i;
    }
  }
  throw new Error(`Could not find a "${headerName}" column header in the performance table.`);
}

async function readColumnValues(page: Page, columnIndex: number): Promise<string[]> {
  const table = page.getByTestId('performance-table');
  await expect(table).toBeVisible({ timeout: 15000 });
  const rows = table.locator('tbody tr');
  const rowCount = await rows.count();
  const values: string[] = [];
  for (let i = 0; i < rowCount; i += 1) {
    values.push((await rows.nth(i).locator('td').nth(columnIndex).textContent())?.trim() ?? '');
  }
  return values;
}

test.describe('Marketing — Analýzy', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToMarketingPerformance(page);
  });

  test('renders the monthly table with data', async ({ page }) => {
    const table = page.getByTestId('performance-table');
    const emptyState = page.getByTestId('performance-table-empty');

    // PerformanceTable.tsx renders one or the other, never both — wait for whichever appears
    // first so a genuinely empty staging dataset fails fast with a clear message instead of
    // timing out on a table that was never going to render.
    await expect(table.or(emptyState)).toBeVisible({ timeout: 15000 });

    if (await emptyState.isVisible()) {
      throw new Error(NO_DATA_MESSAGE);
    }

    const rows = table.locator('tbody tr');
    const count = await rows.count();
    if (count === 0) {
      throw new Error(NO_DATA_MESSAGE);
    }

    await expect(table.getByRole('columnheader', { name: 'PNO', exact: true })).toBeVisible();
  });

  // The naive version of this test reads a single fixed cell (e.g. the first row's revenue) and
  // asserts it changes after ticking the checkbox. That is flaky: if the first month happens to
  // have zero wholesale invoices, nothing in that cell changes even though the toggle worked
  // correctly, and the test fails for a reason unrelated to a real bug. Instead, read the whole
  // "Objednávky" (orders) column across every row and require that at least one row's value
  // changed — robust to which month(s) actually carry wholesale orders.
  test('wholesale switch changes at least one row of the Objednávky column', async ({ page }) => {
    const table = page.getByTestId('performance-table');
    await expect(table).toBeVisible({ timeout: 15000 });

    const rows = table.locator('tbody tr');
    const rowCount = await rows.count();
    if (rowCount === 0) {
      throw new Error(NO_DATA_MESSAGE);
    }

    const ordersColumnIndex = await findColumnIndex(table, 'Objednávky');
    const before = await readColumnValues(page, ordersColumnIndex);

    await page.getByLabel('včetně velkoobchodu', { exact: true }).check();

    // Toggling includeWholesale changes the React Query key, so the table briefly unmounts
    // while the new query is in flight (MarketingPerformancePage.tsx guards the table on
    // `months.data`, which is undefined for a fresh key) before remounting with fresh data.
    // Poll rather than asserting once, so that gap doesn't register as a spurious failure.
    let after: string[] = [];
    await expect(async () => {
      after = await readColumnValues(page, ordersColumnIndex);
      expect(after.length).toBe(before.length);
    }).toPass({ timeout: 15000 });

    // A retained window with no wholesale orders at all makes this assertion
    // unsatisfiable, which is a data problem, not a regression — say so instead of
    // failing on an opaque `changedRows.length > 0`.
    const changedRows = after.filter((value, index) => value !== before[index]);
    if (changedRows.length === 0) {
      throw new Error(
        'Toggling "včetně velkoobchodu" changed no order count. The staging window likely ' +
          'contains no wholesale (VatPayer) invoices, so this scenario cannot be verified. ' +
          'Check the retained IssuedInvoices range before treating this as a product bug.',
      );
    }
  });

  test('comparison view shows YTD cards and the comparison chart', async ({ page }) => {
    await page.getByLabel('Zobrazení', { exact: true }).selectOption('comparison');
    await expect(page.getByText(/\(YTD\)/).first()).toBeVisible({ timeout: 15000 });
    await expect(page.getByText(/Meziroční srovnání — /)).toBeVisible();
  });
});
