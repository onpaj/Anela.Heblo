import { test, expect, devices } from '@playwright/test'
import { navigateToApp } from '../helpers/e2e-auth-helper'
import { waitForPageLoad } from '../helpers/wait-helpers'

const MOBILE_VIEWPORT = devices['iPhone 12'].viewport

test.describe('Financial Overview — mobile viewport', () => {
  test.use({ viewport: MOBILE_VIEWPORT })

  test.beforeEach(async ({ page }) => {
    await navigateToApp(page)
    const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'
    await page.goto(`${baseUrl}/finance/overview`)
    await waitForPageLoad(page)
  })

  test('KPI cards and chart heading are visible', async ({ page }) => {
    await expect(page.getByText('Celkové příjmy')).toBeVisible()
    await expect(page.getByText('Celkové náklady')).toBeVisible()
    await expect(page.getByText(/Finanční přehled -/)).toBeVisible()
  })

  test('filters panel is collapsed by default and expands on tap', async ({ page }) => {
    const toggleBtn = page.getByText(/Filtry & období/)
    await expect(toggleBtn).toBeVisible()
    await expect(page.getByLabel('Časové období:')).not.toBeVisible()
    await toggleBtn.click()
    await expect(page.getByLabel('Časové období:')).toBeVisible()
  })

  test('monthly data is collapsed by default and shows cards when expanded', async ({ page }) => {
    const expanderBtn = page.getByRole('button', { name: /Měsíční data/ })
    await expect(expanderBtn).toBeVisible()
    await expanderBtn.click()
    await expect(page.getByText('Příjmy').first()).toBeVisible()
    await expect(page.getByText('Náklady').first()).toBeVisible()
  })
})
