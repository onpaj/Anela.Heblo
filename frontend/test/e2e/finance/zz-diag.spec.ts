import { test } from '@playwright/test'
import { navigateToApp } from '../helpers/e2e-auth-helper'
import { waitForPageLoad } from '../helpers/wait-helpers'

test.describe('diag-perm', () => {
  test.use({ viewport: { width: 1440, height: 900 } })
  test('dump', async ({ page }) => {
    page.on('response', async (r) => {
      if (!r.url().includes('/api/')) return
      let snippet = ''
      try { snippet = (await r.text()).slice(0, 900) } catch { snippet = '<no body>' }
      console.log(`[HTTP ${r.status()}] ${r.url()}\n    ${snippet}`)
    })
    await navigateToApp(page)
    const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'
    await page.goto(`${baseUrl}/finance/overview`)
    await waitForPageLoad(page)
  })
})
