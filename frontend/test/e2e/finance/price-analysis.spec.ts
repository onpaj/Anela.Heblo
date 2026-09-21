import { test, expect, Page } from '@playwright/test'
import { navigateToApp } from '../helpers/e2e-auth-helper'
import { waitForPageLoad } from '../helpers/wait-helpers'
import { TestCatalogItems, requireTestData } from '../fixtures/test-data'

const BASE_URL = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'
const PRICE_ANALYSIS_PATH = '/finance/price-analysis'
const RECALCULATE_ENDPOINT = '/api/pricing-simulator/recalculate'
const SCENARIOS_ENDPOINT = '/api/pricing-scenarios'

// The Price Analysis grid defaults to Product + Goods (see PricingBaselineBuilder:
// `products.Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods)`),
// so a Materiál/Polotovar fixture (most of TestCatalogItems) would never render here.
// DAR001 is a Produkt with margins data documented in test-data-fixtures.md.
const product = requireTestData(
  TestCatalogItems.darkovyBalicek,
  'DAR001 (Dárkové balení) product for the price analysis grid'
)

/**
 * Asserts a price cell holds `expected`, read through its own editor rather than the
 * cell text: the cell renders a locale-formatted amount ("1 234,00 Kč", with
 * non-breaking spaces), while the editor carries the plain number the API round-trips.
 * Leaves the grid as it found it by dismissing the editor again.
 */
async function expectCellValue(
  page: Page,
  productCode: string,
  expected: string
): Promise<void> {
  await page.getByTestId(`pricing-cell-${productCode}-Price`).click()
  await expect(page.getByTestId(`pricing-editor-value-${productCode}-Price`)).toHaveValue(
    expected
  )
  await page.getByTestId(`pricing-editor-cancel-${productCode}-Price`).click()
}

async function openPriceAnalysis(page: Page): Promise<void> {
  await page.goto(`${BASE_URL}${PRICE_ANALYSIS_PATH}`)
  await waitForPageLoad(page)

  await expect(
    page.getByRole('heading', { name: 'Analýza cen', exact: true })
  ).toBeVisible({ timeout: 60_000 })
  await expect(page.getByTestId('totals-line-Obrat')).toBeVisible({ timeout: 60_000 })

  const row = page.getByTestId(`pricing-row-${product.code}`)
  const rowCount = await row.count()
  if (rowCount === 0) {
    throw new Error(
      `Test data missing: expected product ${product.code} (${product.name}) to appear in the ` +
        'price analysis grid under the default filter (Product + Goods). Check ' +
        'frontend/test/e2e/fixtures/test-data.ts against the current staging catalog.'
    )
  }
  await expect(row).toBeVisible({ timeout: 30_000 })
}

test.describe('Price Analysis E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page)
  })

  test('edit a price, save/reload/load a scenario, then delete it', async ({ page }) => {
    await openPriceAnalysis(page)

    // --- Totals bar renders ---
    const revenueTotals = page.getByTestId('totals-line-Obrat')
    await expect(revenueTotals).toBeVisible()
    await expect(page.getByTestId('totals-line-M0')).toBeVisible()
    await expect(page.getByTestId('totals-line-M1')).toBeVisible()

    // TotalsLine renders exactly two ".font-medium" values per line: Před (index 0)
    // then Po (index 1) - see PricingTotalsBar.tsx. No dedicated testid exists at
    // that granularity, so the class selector is the stable hook available.
    const revenueValues = revenueTotals.locator('.font-medium')
    const revenueBeforeText = await revenueValues.nth(0).innerText()
    const revenueAfterTextBeforeEdit = await revenueValues.nth(1).innerText()

    // --- Edit one product's price through the cell editor, wait for recalc ---
    // A cell is a click target, not an input: it opens a transient two-field editor
    // (absolute value / change against the real state) and commits on Použít.
    const priceCell = page.getByTestId(`pricing-cell-${product.code}-Price`)
    await expect(priceCell).toBeVisible()
    await priceCell.click()

    const priceEditor = page.getByTestId(`pricing-editor-value-${product.code}-Price`)
    await expect(priceEditor).toBeVisible()
    // The editor carries the plain number; the cell itself shows it formatted as Kč.
    const originalPriceRaw = await priceEditor.inputValue()
    const originalPrice = Number(originalPriceRaw.replace(',', '.')) || 0
    const newPrice = Math.round((originalPrice + 25) * 100) / 100

    const recalcResponse = page.waitForResponse(
      (resp) => resp.url().includes(RECALCULATE_ENDPOINT) && resp.request().method() === 'POST'
    )
    await priceEditor.fill(String(newPrice))
    await page.getByTestId(`pricing-editor-apply-${product.code}-Price`).click()
    await recalcResponse

    // Obrat "po" changed; Obrat "před" (baseline) must never move from an edit -
    // that is the point of this assertion, not decoration.
    await expect(revenueValues.nth(1)).not.toHaveText(revenueAfterTextBeforeEdit)
    await expect(revenueValues.nth(0)).toHaveText(revenueBeforeText)

    // --- Save the scenario under a unique name ---
    const scenarioName = `E2E Price Analysis ${Date.now()}`
    await page.getByTestId('pricing-scenario-name-input').fill(scenarioName)

    const saveResponse = page.waitForResponse(
      (resp) =>
        resp.url().includes(SCENARIOS_ENDPOINT) &&
        resp.request().method() === 'POST' &&
        resp.status() === 200
    )
    await page.getByTestId('pricing-scenario-save').click()
    await saveResponse

    const scenarioSelect = page.getByTestId('pricing-scenario-select')
    const savedOption = scenarioSelect.locator('option', { hasText: scenarioName })
    await expect(savedOption).toHaveCount(1)
    const scenarioId = await savedOption.getAttribute('value')
    if (!scenarioId) {
      throw new Error(`Saved scenario "${scenarioName}" has no option value after saving.`)
    }

    // --- Reload the page: client-side edit state must be gone ---
    await page.reload()
    await waitForPageLoad(page)
    await expect(
      page.getByRole('heading', { name: 'Analýza cen', exact: true })
    ).toBeVisible({ timeout: 60_000 })
    await expect(page.getByTestId(`pricing-row-${product.code}`)).toBeVisible({ timeout: 30_000 })
    await expectCellValue(page, product.code, originalPriceRaw)

    // --- Load the saved scenario ---
    const reloadedSelect = page.getByTestId('pricing-scenario-select')
    await expect(reloadedSelect.locator('option', { hasText: scenarioName })).toHaveCount(1)

    const scenarioDetailResponse = page.waitForResponse(
      (resp) =>
        resp.url().includes(`${SCENARIOS_ENDPOINT}/${scenarioId}`) &&
        resp.request().method() === 'GET' &&
        resp.status() === 200
    )
    await reloadedSelect.selectOption(scenarioId)
    await scenarioDetailResponse

    // Edited price is restored from the loaded scenario.
    await expectCellValue(page, product.code, String(newPrice))

    // --- Delete the scenario (inline confirmation, never window.confirm) ---
    await page.getByTestId('pricing-scenario-delete').click()
    await expect(page.getByTestId('pricing-scenario-delete-confirm')).toBeVisible()

    const deleteResponse = page.waitForResponse(
      (resp) =>
        resp.url().includes(`${SCENARIOS_ENDPOINT}/${scenarioId}`) &&
        resp.request().method() === 'DELETE' &&
        resp.status() === 200
    )
    await page.getByTestId('pricing-scenario-delete-confirm-yes').click()
    await deleteResponse

    await expect(reloadedSelect.locator('option', { hasText: scenarioName })).toHaveCount(0)
  })
})
