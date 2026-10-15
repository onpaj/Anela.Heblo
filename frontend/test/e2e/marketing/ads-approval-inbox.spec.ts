import { test, expect, type Page } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';
import { waitForLoadingComplete } from '../helpers/wait-helpers';

// Read-only by design. On Staging the E2E session (AuthenticationType `E2ETest`) is an
// app-only identity (C4 ruling R16), so approve/reject are refused and nothing can be seeded
// without leaving Pending proposals behind. Approve/reject/diff/reasoning are covered by unit tests.

const NONEXISTENT_PROPOSAL_ID = 2147483647;
const PAGE_TIMEOUT_MS = 15000;
const CHANNEL_DENIED_CODE = 'MarketingAdsApprovalChannelDenied';

async function gotoAdsPage(page: Page, path: string): Promise<void> {
  await page.goto(path);
}

function proposalListOrEmpty(page: Page) {
  return page.getByTestId('proposal-list').or(page.getByTestId('proposal-list-empty'));
}

// Staging's ads DB may be unconfigured, so a tab may legitimately show an error message instead of data.
function tableOrEmptyOrError(page: Page, tableTestId: string) {
  return page
    .getByTestId(tableTestId)
    .or(page.getByText('Zatím žádné záznamy.'))
    .or(page.locator('div.text-red-600'))
    .first();
}

test.describe('Marketing — reklamy (návrhy, aktivita, nastavení)', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    await waitForLoadingComplete(page);
  });

  test('inbox renders the pending and history tabs', async ({ page }) => {
    await gotoAdsPage(page, '/marketing/ads/proposals');

    await expect(page.getByRole('heading', { name: 'Návrhy změn v reklamách', exact: true })).toBeVisible({
      timeout: PAGE_TIMEOUT_MS,
    });
    await expect(page.getByRole('tab', { name: 'Ke schválení', exact: true })).toHaveAttribute('aria-selected', 'true');
    await expect(proposalListOrEmpty(page)).toBeVisible({ timeout: PAGE_TIMEOUT_MS });

    await page.getByRole('tab', { name: 'Historie', exact: true }).click();

    await expect(page).toHaveURL(/tab=history/);
    await expect(proposalListOrEmpty(page)).toBeVisible({ timeout: PAGE_TIMEOUT_MS });
  });

  test('the E2E session may not approve or reject (app-only identity, C4 R16)', async ({ page }) => {
    // If this starts returning 404, the E2E identity gained an approving channel (the channel check
    // runs before the proposal lookup). That is a security regression to investigate, not a test to relax.
    const approve = await page.request.post(`/api/marketing-ads/proposals/${NONEXISTENT_PROPOSAL_ID}/approve`, {
      data: { id: NONEXISTENT_PROPOSAL_ID, version: 1 },
    });
    const reject = await page.request.post(`/api/marketing-ads/proposals/${NONEXISTENT_PROPOSAL_ID}/reject`, {
      data: { id: NONEXISTENT_PROPOSAL_ID, version: 1, reason: 'E2E: kontrola oprávnění' },
    });

    for (const response of [approve, reject]) {
      expect(response.status()).toBe(403);
      const body = await response.json();
      expect(body.errorCode).toBe(CHANNEL_DENIED_CODE);
    }
  });

  test('activity page renders all three tabs', async ({ page }) => {
    await gotoAdsPage(page, '/marketing/ads/activity');
    const heading = page.getByRole('heading', { name: 'Aktivita v reklamách', exact: true });
    await expect(heading).toBeVisible({ timeout: PAGE_TIMEOUT_MS });
    await expect(tableOrEmptyOrError(page, 'audit-table')).toBeVisible({ timeout: PAGE_TIMEOUT_MS });

    await page.getByRole('tab', { name: 'Změny v účtech', exact: true }).click();
    await expect(page.getByLabel('Platforma', { exact: true })).toBeVisible();
    await expect(page.getByText('Posledních 30 dní.')).toBeVisible();
    await expect(tableOrEmptyOrError(page, 'change-events-table')).toBeVisible({ timeout: PAGE_TIMEOUT_MS });
    await expect(heading).toBeVisible();

    await page.getByRole('tab', { name: 'Běhy agentů', exact: true }).click();
    await expect(tableOrEmptyOrError(page, 'agent-runs-table')).toBeVisible({ timeout: PAGE_TIMEOUT_MS });
    await expect(heading).toBeVisible();
  });

  test('settings page renders for an admin', async ({ page }) => {
    // Never click a kill-switch, autonomy or limit control here: this suite runs against shared staging.
    await gotoAdsPage(page, '/marketing/ads/settings');

    await expect(page.getByRole('heading', { name: 'Nastavení reklam', exact: true })).toBeVisible({
      timeout: PAGE_TIMEOUT_MS,
    });
    await expect(page.getByTestId('kill-switch')).toBeVisible({ timeout: PAGE_TIMEOUT_MS });
    await expect(page.getByTestId('autonomy-table')).toBeVisible({ timeout: PAGE_TIMEOUT_MS });
    await expect(page.getByTestId('limits-table')).toBeVisible({ timeout: PAGE_TIMEOUT_MS });
  });
});
