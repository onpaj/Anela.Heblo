import { test, expect } from '@playwright/test';
import { navigateToApp } from '../helpers/e2e-auth-helper';

test.describe('Gift Package Disassembly Workflow', () => {
  test.beforeEach(async ({ page }) => {
    console.log('🎁 Starting gift package disassembly workflow test setup...');

    try {
      // Navigate to application with full authentication
      console.log('🚀 Navigating to application...');
      await navigateToApp(page);

      // Wait for app to load
      await page.waitForLoadState('domcontentloaded');
      await page.waitForTimeout(3000); // Give extra time for React components to initialize

      console.log('✅ Gift package disassembly test setup completed successfully');
    } catch (error) {
      console.log(`❌ Setup failed: ${error.message}`);
      throw error;
    }
  });
});
