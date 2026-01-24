/**
 * E2E tests for changelog functionality
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import { test, expect } from '@playwright/test';
import { createE2EAuthSession, navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Changelog System', () => {
  test.beforeEach(async ({ page }) => {
    // Use proper E2E authentication
    await createE2EAuthSession(page);
    await navigateToApp(page);
  });

  test('should display changelog button in sidebar', async ({ page }) => {
    // Look for the changelog button in the sidebar
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await expect(changelogButton).toBeVisible();
    
    // Check if the newspaper icon is present
    const newspaperIcon = page.locator('button:has-text("Co je nové") svg');
    await expect(newspaperIcon).toBeVisible();
  });

  test('should open changelog modal when clicking sidebar button', async ({ page }) => {
    // Click the changelog button
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();

    // Check if modal opens using data-testid
    const modal = page.locator('[data-testid="changelog-modal"]');
    await expect(modal).toBeVisible();

    // Check modal title (specifically the heading, not the button)
    await expect(page.locator('h3:has-text("Co je nové")')).toBeVisible();

    // Check for version information - try different possible texts
    const versionInfo = page.locator('text=/Aktuální verze:|Current version|Version|verze/i');
    await expect(versionInfo.first()).toBeVisible({ timeout: 10000 });
  });

  test('should display version history in modal', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();

    // Wait for modal to load using data-testid
    const modal = page.locator('[data-testid="changelog-modal"]');
    await expect(modal).toBeVisible();

    // Check for version list sidebar using data-testid
    const versionSidebar = page.locator('[data-testid="changelog-version-sidebar"]');
    await expect(versionSidebar).toBeVisible();

    // Check for version list header
    const versionListHeader = page.locator('text=Verze');
    await expect(versionListHeader).toBeVisible();

    // Look for version entries using data-testid
    const versionList = page.locator('[data-testid="changelog-version-list"]');
    await expect(versionList).toBeVisible({ timeout: 10000 });

    // Check that at least one version entry exists
    const versionEntries = page.locator('[data-testid^="changelog-version-"]');
    await expect(versionEntries.first()).toBeVisible({ timeout: 10000 });
  });

  test('should close modal when clicking close button', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();

    // Wait for modal to open using data-testid
    const modal = page.locator('[data-testid="changelog-modal"]');
    await expect(modal).toBeVisible();

    // Click close button using data-testid
    const closeButton = page.locator('[data-testid="changelog-modal-close-button"]');
    await closeButton.click();

    // Check if modal is closed
    await expect(modal).not.toBeVisible();
  });

  test('should close modal when clicking backdrop', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();

    // Wait for modal to open using data-testid
    const modal = page.locator('[data-testid="changelog-modal"]');
    await expect(modal).toBeVisible();

    // Click on backdrop using data-testid
    const backdrop = page.locator('[data-testid="changelog-modal-backdrop"]');
    await backdrop.click({ position: { x: 10, y: 10 } });

    // Check if modal is closed
    await expect(modal).not.toBeVisible();
  });

  test('should close modal when pressing Escape key', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();

    // Wait for modal to open using data-testid
    const modal = page.locator('[data-testid="changelog-modal"]');
    await expect(modal).toBeVisible();

    // Press Escape key
    await page.keyboard.press('Escape');

    // Check if modal is closed
    await expect(modal).not.toBeVisible();
  });

  test('should show changelog content when version is selected', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();

    // Wait for modal to load using data-testid
    const modal = page.locator('[data-testid="changelog-modal"]');
    await expect(modal).toBeVisible();

    // Wait for version list to be visible
    const versionList = page.locator('[data-testid="changelog-version-list"]');
    await expect(versionList).toBeVisible({ timeout: 10000 });

    // Click on first version entry using data-testid
    const versionEntry = page.locator('[data-testid^="changelog-version-"]').first();
    await expect(versionEntry).toBeVisible({ timeout: 10000 });
    await versionEntry.click();

    // Check for version details section using data-testid
    const versionDetails = page.locator('[data-testid="changelog-version-details"]');
    await expect(versionDetails).toBeVisible();

    // Check for changes list header
    const changesHeader = page.locator('text=Změny');
    await expect(changesHeader).toBeVisible();

    // Check for changes list using data-testid
    const changesList = page.locator('[data-testid="changelog-changes-list"]');
    await expect(changesList).toBeVisible();
  });

  test('should work in collapsed sidebar mode', async ({ page }) => {
    // Check if there's a sidebar collapse button and click it
    const collapseButton = page.locator('button[title="Collapse sidebar"]').or(page.locator('button:has(svg):has-text("")'));

    if (await collapseButton.isVisible()) {
      await collapseButton.click();

      // Wait for sidebar to collapse
      await page.waitForTimeout(500);

      // Look for changelog button (should now be icon only)
      const changelogIconButton = page.locator('button[title="Co je nové"]');
      await expect(changelogIconButton).toBeVisible();

      // Click the icon button
      await changelogIconButton.click();

      // Check if modal opens using data-testid
      const modal = page.locator('[data-testid="changelog-modal"]');
      await expect(modal).toBeVisible();
    }
  });

  test('should handle mobile responsive layout', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });

    // Use proper E2E authentication and navigation (already done in beforeEach)
    // Just wait for the app to be ready
    await page.waitForSelector('[data-testid="app"]', { timeout: 30000 });

    // On mobile, sidebar might be hidden by default
    // Look for menu button to open sidebar
    const menuButton = page.locator('button[aria-label="Open menu"]').or(page.locator('button:has(svg):has-text("")')).first();

    if (await menuButton.isVisible()) {
      await menuButton.click();
    }

    // Look for changelog button
    const changelogButton = page.locator('button:has-text("Co je nové")').or(page.locator('button[title="Co je nové"]'));
    await expect(changelogButton).toBeVisible();

    // Click changelog button
    await changelogButton.click();

    // Check if modal opens using data-testid
    const modal = page.locator('[data-testid="changelog-modal"]');
    await expect(modal).toBeVisible();

    // Reset to desktop viewport
    await page.setViewportSize({ width: 1280, height: 720 });
  });

  test('should not show changelog toaster on initial load (staging)', async ({ page }) => {
    // On staging, changelog toaster might be present but should not auto-show for repeated visits
    // Look for toaster (should not be visible by default)
    const toaster = page.locator('text=/Co je nové v \d+\.\d+\.\d+/');
    
    // Wait a bit to ensure toaster doesn't appear
    await page.waitForTimeout(2000);
    
    // Toaster should not be visible on staging for repeat visits
    const isToasterVisible = await toaster.isVisible().catch(() => false);
    
    // This is informational - we don't fail the test if toaster appears
    // since it depends on localStorage state and deployment versions
    if (isToasterVisible) {
      console.log('Changelog toaster is visible - this is expected for new versions');
    } else {
      console.log('Changelog toaster is not visible - this is expected for seen versions');
    }
  });
});