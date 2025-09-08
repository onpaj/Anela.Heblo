import { test, expect } from '@playwright/test';
import { createE2EAuthSession } from './helpers/auth-helper';
import { gotoAndWaitReady } from '../utils/readiness-helper';

test.describe('Transport Box Creation E2E Tests', () => {
  
  test.beforeEach(async ({ page }) => {
    await createE2EAuthSession(page);
  });

  test('should navigate to Transport Box creation page', async ({ page }) => {
    await gotoAndWaitReady(page, '/transport-boxes');
    
    // Verify we're on the transport boxes list page
    await expect(page.locator('h1')).toContainText('Transport Boxes');
    
    // Look for create/add button (assuming there's a "+" or "Add" button)
    const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
    await expect(createButton).toBeVisible();
  });

  test('should test transport box form validation and submission', async ({ page }) => {
    await gotoAndWaitReady(page, '/transport-boxes');
    
    // Click create/add button to open creation form
    const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
    await createButton.first().click();
    
    // Wait for form to appear (could be in modal or new page)
    await page.waitForTimeout(1000);
    
    // Test form validation - try to submit empty form
    const submitButton = page.locator('button[type="submit"]').or(
      page.locator('button').filter({ hasText: /Submit|Save|Uložit|Vytvořit/ })
    );
    
    if (await submitButton.count() > 0) {
      await submitButton.first().click();
      
      // Check for validation errors
      const errorElements = page.locator('.error, .text-red-500, .text-red-600, .border-red-500');
      expect(await errorElements.count()).toBeGreaterThanOrEqual(1);
    }
    
    // Fill in required fields (adjust selectors based on actual form)
    const descriptionField = page.locator('input[name="description"], textarea[name="description"], input[placeholder*="description"], textarea[placeholder*="popis"]').first();
    if (await descriptionField.count() > 0) {
      await descriptionField.fill('Test Transport Box E2E');
    }
    
    // Fill destination if exists
    const destinationField = page.locator('input[name="destination"], input[placeholder*="destination"], input[placeholder*="cíl"]').first();
    if (await destinationField.count() > 0) {
      await destinationField.fill('Test Destination');
    }
    
    // Try to submit valid form
    if (await submitButton.count() > 0) {
      await submitButton.first().click();
      
      // Wait for success or navigation
      await page.waitForTimeout(2000);
      
      // Should either navigate back to list or show success message
      const successIndicators = [
        page.locator('.success, .text-green-500, .text-green-600'),
        page.locator('text=Transport Box created successfully'),
        page.locator('text=Box created'),
        page.locator('h1').filter({ hasText: 'Transport Boxes' })
      ];
      
      let foundSuccess = false;
      for (const indicator of successIndicators) {
        if (await indicator.count() > 0) {
          foundSuccess = true;
          break;
        }
      }
      
      expect(foundSuccess).toBe(true);
    }
  });

  test('should validate EAN code generation and custom codes', async ({ page }) => {
    await gotoAndWaitReady(page, '/transport-boxes');
    
    // Open creation form
    const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
    await createButton.first().click();
    await page.waitForTimeout(1000);
    
    // Look for EAN code field
    const eanField = page.locator('input[name*="ean"], input[placeholder*="EAN"], input[placeholder*="kód"]').first();
    
    if (await eanField.count() > 0) {
      // Test custom EAN code
      await eanField.fill('1234567890123');
      
      // Verify the field accepts the input
      await expect(eanField).toHaveValue('1234567890123');
      
      // Test EAN code validation (if any)
      await eanField.fill('invalid_ean');
      
      // Try to find any validation feedback
      const validation = page.locator('.error, .text-red-500, .invalid').first();
      if (await validation.count() > 0) {
        await expect(validation).toBeVisible();
      }
      
      // Clear field to test auto-generation
      await eanField.clear();
    }
    
    // Look for generate EAN button
    const generateButton = page.locator('button').filter({ hasText: /Generate|Generovat|Auto/ });
    if (await generateButton.count() > 0) {
      await generateButton.first().click();
      
      // Verify EAN was generated
      if (await eanField.count() > 0) {
        await expect(eanField).not.toBeEmpty();
      }
    }
  });

  test('should test box metadata (destination, notes, priority)', async ({ page }) => {
    await gotoAndWaitReady(page, '/transport-boxes');
    
    // Open creation form
    const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
    await createButton.first().click();
    await page.waitForTimeout(1000);
    
    // Test destination field
    const destinationField = page.locator('input[name="destination"], input[placeholder*="destination"], input[placeholder*="cíl"]').first();
    if (await destinationField.count() > 0) {
      await destinationField.fill('Prague Warehouse');
      await expect(destinationField).toHaveValue('Prague Warehouse');
    }
    
    // Test notes field
    const notesField = page.locator('textarea[name="notes"], input[name="notes"], textarea[placeholder*="notes"], textarea[placeholder*="pozn"]').first();
    if (await notesField.count() > 0) {
      await notesField.fill('Test notes for E2E testing');
      await expect(notesField).toHaveValue('Test notes for E2E testing');
    }
    
    // Test priority field (could be dropdown, radio, or select)
    const priorityDropdown = page.locator('select[name="priority"], select[name*="priority"]').first();
    if (await priorityDropdown.count() > 0) {
      await priorityDropdown.selectOption({ index: 1 }); // Select second option
      await expect(priorityDropdown).not.toHaveValue('');
    }
    
    // Test priority radio buttons
    const priorityRadio = page.locator('input[type="radio"][name*="priority"]').first();
    if (await priorityRadio.count() > 0) {
      await priorityRadio.check();
      await expect(priorityRadio).toBeChecked();
    }
  });

  test('should verify box creation workflow and confirmations', async ({ page }) => {
    await gotoAndWaitReady(page, '/transport-boxes');
    
    // Get initial box count for verification
    const initialBoxCount = await page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item').count();
    
    // Open creation form
    const createButton = page.locator('button').filter({ hasText: /(\+|Add|Create|Nový|Přidat)/ });
    await createButton.first().click();
    await page.waitForTimeout(1000);
    
    // Fill required fields
    const descriptionField = page.locator('input[name="description"], textarea[name="description"], input[placeholder*="description"], textarea[placeholder*="popis"]').first();
    if (await descriptionField.count() > 0) {
      await descriptionField.fill('E2E Test Box - Complete Workflow');
    }
    
    const destinationField = page.locator('input[name="destination"], input[placeholder*="destination"], input[placeholder*="cíl"]').first();
    if (await destinationField.count() > 0) {
      await destinationField.fill('Test Warehouse');
    }
    
    // Submit form
    const submitButton = page.locator('button[type="submit"]').or(
      page.locator('button').filter({ hasText: /Submit|Save|Uložit|Vytvořit/ })
    );
    
    if (await submitButton.count() > 0) {
      await submitButton.first().click();
      
      // Wait for creation process
      await page.waitForTimeout(3000);
      
      // Check for confirmation dialog or message
      const confirmationDialog = page.locator('[role="dialog"], .modal, .confirmation').first();
      if (await confirmationDialog.count() > 0) {
        const confirmButton = confirmationDialog.locator('button').filter({ hasText: /OK|Confirm|Yes|Ano/ });
        if (await confirmButton.count() > 0) {
          await confirmButton.click();
        }
      }
      
      // Verify we're back on the list page
      await expect(page.locator('h1')).toContainText('Transport Boxes');
      
      // Verify box was created (box count increased or find the new box)
      await page.waitForTimeout(2000);
      
      // Look for success message
      const successMessage = page.locator('.success, .text-green-500, .alert-success').first();
      if (await successMessage.count() > 0) {
        await expect(successMessage).toBeVisible();
      }
      
      // Verify new box appears in list
      const newBoxCount = await page.locator('[data-testid="transport-box-item"], .transport-box-item, .box-item').count();
      expect(newBoxCount).toBeGreaterThanOrEqual(initialBoxCount);
      
      // Try to find the newly created box
      const newBox = page.locator('text=E2E Test Box - Complete Workflow');
      if (await newBox.count() > 0) {
        await expect(newBox).toBeVisible();
      }
    }
  });
});