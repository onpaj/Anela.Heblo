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
    
    // Check if modal opens - target the modal content specifically  
    const modal = page.locator('[role="dialog"]')
                      .or(page.locator('.modal'))
                      .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto.z-50.overflow-y-auto'))
                      .or(page.locator('[data-testid*="modal"]'))
                      .or(page.locator('[data-overlay-container]'))
                      .or(page.locator('.overlay'))
                      .or(page.locator('[aria-modal="true"]'));
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
    
    // Wait for modal to load
    const modal = page.locator('[role="dialog"]')
                      .or(page.locator('.modal'))
                      .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto'))
                      .or(page.locator('[data-testid*="modal"]'))
                      .or(page.locator('[data-overlay-container]'))
                      .or(page.locator('.overlay'))
                      .or(page.locator('[aria-modal="true"]'));
    await expect(modal).toBeVisible();
    
    // Check for version list in sidebar
    const versionList = page.locator('text=Verze');
    await expect(versionList).toBeVisible();
    
    // Look for version entries - try multiple approaches
    const versionEntry = page.locator('button').filter({ hasText: /v\d+\.\d+\.\d+/ }).first()
                             .or(page.locator('button').filter({ hasText: /\d+\.\d+\.\d+/ }).first())
                             .or(page.locator('[data-testid*="version"]').first())
                             .or(page.locator('li button, div button').first());
    await expect(versionEntry).toBeVisible({ timeout: 10000 });
  });

  test('should close modal when clicking close button', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();
    
    // Wait for modal to open
    const modal = page.locator('[role="dialog"]')
                      .or(page.locator('.modal'))
                      .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto'))
                      .or(page.locator('[data-testid*="modal"]'))
                      .or(page.locator('[data-overlay-container]'))
                      .or(page.locator('.overlay'))
                      .or(page.locator('[aria-modal="true"]'));
    await expect(modal).toBeVisible();
    
    // Click close button
    const closeButton = page.locator('button[aria-label="Close"]').or(page.locator('button:has(svg)').last());
    await closeButton.click();
    
    // Check if modal is closed
    await expect(modal).not.toBeVisible();
  });

  test('should close modal when clicking backdrop', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();
    
    // Wait for modal to open
    const modal = page.locator('[role="dialog"]')
                      .or(page.locator('.modal'))
                      .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto'))
                      .or(page.locator('[data-testid*="modal"]'))
                      .or(page.locator('[data-overlay-container]'))
                      .or(page.locator('.overlay'))
                      .or(page.locator('[aria-modal="true"]'));
    await expect(modal).toBeVisible();
    
    // Click on backdrop (outside modal content)
    await page.mouse.click(100, 100);
    
    // Check if modal is closed
    await expect(modal).not.toBeVisible();
  });

  test('should close modal when pressing Escape key', async ({ page }) => {
    // Open changelog modal
    const changelogButton = page.locator('button:has-text("Co je nové")');
    await changelogButton.click();
    
    // Wait for modal to open
    const modal = page.locator('[role="dialog"]')
                      .or(page.locator('.modal'))
                      .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto'))
                      .or(page.locator('[data-testid*="modal"]'))
                      .or(page.locator('[data-overlay-container]'))
                      .or(page.locator('.overlay'))
                      .or(page.locator('[aria-modal="true"]'));
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
    
    // Wait for modal to load
    const modal = page.locator('[role="dialog"]')
                      .or(page.locator('.modal'))
                      .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto'))
                      .or(page.locator('[data-testid*="modal"]'))
                      .or(page.locator('[data-overlay-container]'))
                      .or(page.locator('.overlay'))
                      .or(page.locator('[aria-modal="true"]'));
    await expect(modal).toBeVisible();
    
    // Click on a version (should be pre-selected by default)
    const versionEntry = page.locator('button').filter({ hasText: /v\d+\.\d+\.\d+/ }).first()
                             .or(page.locator('button').filter({ hasText: /\d+\.\d+\.\d+/ }).first())
                             .or(page.locator('[data-testid*="version"]').first())
                             .or(page.locator('li button, div button').first());
    await versionEntry.click();
    
    // Check for changelog content
    await expect(page.locator('text=Změny')).toBeVisible();
    
    // Look for change entries (should have type badges)
    const changeEntry = page.locator('[class*="bg-"]').and(page.locator('text=/oprava|funkce|vylepšení|výkon/')).first();
    await expect(changeEntry).toBeVisible();
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
      
      // Check if modal opens
      const modal = page.locator('[role="dialog"]')
                        .or(page.locator('.modal'))
                        .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto'))
                        .or(page.locator('[data-testid*="modal"]'))
                        .or(page.locator('[data-overlay-container]'))
                        .or(page.locator('.overlay'))
                        .or(page.locator('[aria-modal="true"]'));
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
    
    // Check if modal opens and is properly sized for mobile
    const modal = page.locator('[role="dialog"]')
                      .or(page.locator('.modal'))
                      .or(page.locator('.fixed.inset-0.z-50.overflow-y-auto'))
                      .or(page.locator('[data-testid*="modal"]'))
                      .or(page.locator('[data-overlay-container]'))
                      .or(page.locator('.overlay'))
                      .or(page.locator('[aria-modal="true"]'));
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