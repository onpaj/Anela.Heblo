import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Sidebar Navigation', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to application with full authentication
    await navigateToApp(page);

    // Wait for page to be fully loaded
    await page.waitForLoadState('domcontentloaded');
  });

  test('should display Personální section with Struktura link', async ({ page }) => {
    // Find and click on Personální section
    const personalniSection = page.getByRole('button', { name: /Personální/i });
    await expect(personalniSection).toBeVisible();

    // Expand the section
    await personalniSection.click();

    // Check if Struktura link is visible with external link icon
    const strukturaLink = page.getByRole('button', { name: /Struktura/i });
    await expect(strukturaLink).toBeVisible();

    // Verify the external link icon is present (ExternalLink component)
    const externalIcon = strukturaLink.locator('svg').last();
    await expect(externalIcon).toBeVisible();
  });

  test('should open Struktura in new window', async ({ page, context }) => {
    // Find and expand Personální section
    const personalniSection = page.getByRole('button', { name: /Personální/i });
    await personalniSection.click();

    // Create a promise that resolves when a new page is opened
    const newPagePromise = context.waitForEvent('page');

    // Click on Struktura link
    const strukturaLink = page.getByRole('button', { name: /Struktura/i });
    await strukturaLink.click();

    // Wait for the new page to open
    const newPage = await newPagePromise;

    // Wait for the new page to load
    await newPage.waitForLoadState();

    // Verify the URL is correct
    expect(newPage.url()).toBe('https://orgchart.anela.cz/');

    // Close the new page
    await newPage.close();
  });

  test('should display Personální section between Sklad and Automatizace', async ({ page }) => {
    // Get all section buttons in the sidebar
    const sectionButtons = page.locator('nav button:has(svg)').filter({ hasText: /^(Sklad|Personální|Automatizace)$/ });

    // Get text content of all sections
    const sectionTexts = await sectionButtons.allTextContents();

    // Find indices
    const skladIndex = sectionTexts.findIndex(text => text.includes('Sklad'));
    const personalniIndex = sectionTexts.findIndex(text => text.includes('Personální'));
    const automatizaceIndex = sectionTexts.findIndex(text => text.includes('Automatizace'));

    // Verify order: Personální should be between Sklad and Automatizace
    expect(skladIndex).toBeGreaterThanOrEqual(0);
    expect(personalniIndex).toBeGreaterThan(skladIndex);
    expect(automatizaceIndex).toBeGreaterThan(personalniIndex);
  });
});
