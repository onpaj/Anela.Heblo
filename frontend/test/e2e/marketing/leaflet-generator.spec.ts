import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

const LEAFLET_GENERATOR_PATH = '/leaflet-generator';
const TOPIC = 'Bisabolol pro citlivou pleť';
const MIN_RESULT_LENGTH = 100;
const RESULT_TIMEOUT_MS = 30_000;

test.describe('Leaflet Generator', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToApp(page);
    await page.goto(LEAFLET_GENERATOR_PATH);
    await page.waitForLoadState('domcontentloaded');
  });

  test('generates a leaflet for a known topic', async ({ page }) => {
    // Verify the page heading is visible
    await expect(
      page.getByRole('heading', { name: 'Generátor letáků' }),
    ).toBeVisible({ timeout: 10_000 });

    // Fill in the topic field
    await page.getByLabel('Téma').fill(TOPIC);

    // Select audience — "Koncový zákazník" is the default but be explicit
    await page.getByRole('radio', { name: 'Koncový zákazník' }).check();

    // Select length — "Střední (~400 slov)"
    await page.getByRole('radio', { name: 'Střední (~400 slov)' }).check();

    // Submit the form
    await page.getByRole('button', { name: 'Vygenerovat leták' }).click();

    // Wait for the result container to appear (LLM call can take up to 25 s)
    const proseContainer = page.locator('.prose');
    await expect(proseContainer).toBeVisible({ timeout: RESULT_TIMEOUT_MS });

    // Validate that the result contains meaningful content
    const resultText = (await proseContainer.textContent()) ?? '';
    if (resultText.trim().length < MIN_RESULT_LENGTH) {
      throw new Error(
        `Leaflet generation returned insufficient content. ` +
          `Expected at least ${MIN_RESULT_LENGTH} characters but got ${resultText.trim().length}. ` +
          `Topic used: "${TOPIC}". Check that the Knowledge Base covers this topic.`,
      );
    }

    // Click the copy button and verify the label changes to confirm clipboard write
    const copyButton = page.getByRole('button', { name: 'Kopírovat' });
    await expect(copyButton).toBeVisible();
    await copyButton.click();

    await expect(page.getByRole('button', { name: 'Zkopírováno' })).toBeVisible({
      timeout: 3_000,
    });
  });
});
