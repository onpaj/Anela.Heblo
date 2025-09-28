import { defineConfig, devices } from '@playwright/test';

/**
 * @see https://playwright.dev/docs/test-configuration
 */

// Load environment variables from .env.test for local development
if (!process.env.CI) {
  const path = require('path');
  const fs = require('fs');
  
  const envTestPath = path.resolve(__dirname, '.env.test');
  if (fs.existsSync(envTestPath)) {
    console.log('📁 Loading E2E environment variables from .env.test...');
    require('dotenv').config({ path: envTestPath });
    console.log('✅ E2E environment variables loaded from .env.test');
  }
}

// Validate environment variables for E2E tests
const requiredVars = ['E2E_CLIENT_ID', 'E2E_CLIENT_SECRET', 'AZURE_TENANT_ID'];
const missing = requiredVars.filter(varName => !process.env[varName]);

if (missing.length > 0) {
  console.error(`❌ Missing required E2E environment variables: ${missing.join(', ')}`);
  if (process.env.CI) {
    console.error('Please set these variables in your GitHub repository secrets.');
  } else {
    console.error('Please ensure .env.test file exists in frontend/ directory with proper credentials.');
  }
  process.exit(1);
}

console.log('✅ E2E environment variables validated successfully');

export default defineConfig({
  testDir: './test/e2e',
  /* Run tests in files in parallel */
  fullyParallel: false, // E2E tests should run sequentially
  /* Fail the build on CI if you accidentally left test.only in the source code. */
  forbidOnly: !!process.env.CI,
  /* Retry on CI only */
  retries: process.env.CI ? 2 : 1,
  /* Opt out of parallel tests on CI. */
  workers: 1, // E2E tests run one at a time
  /* Reporter to use. See https://playwright.dev/docs/test-reporters */
  reporter: 'html',
  
  /* Shared settings for all the projects below. See https://playwright.dev/docs/api/class-testoptions. */
  use: {
    /* Base URL to use in actions like `await page.goto('/')`. */
    baseURL: process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz',

    /* Collect trace when retrying the failed test. See https://playwright.dev/docs/trace-viewer */
    trace: 'on-first-retry',
    
    /* Increase timeout for E2E test actions - increased for staging environment performance */
    actionTimeout: 60000, // 60 seconds for actions
    navigationTimeout: 60000, // 60 seconds for page navigation
    
    /* Screenshot on failure */
    screenshot: 'only-on-failure',
    
    /* Video for failed tests */
    video: 'retain-on-failure',
  },

  /* Global test timeout for E2E - increased for staging environment performance */
  timeout: 300000, // 5 minutes per test

  /* Configure projects for major browsers */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  /* Run your local dev server before starting the tests */
  // We handle server startup in our custom script
  // webServer: {
  //   command: 'npm run start:automation',
  //   url: 'http://localhost:3001',
  //   reuseExistingServer: !process.env.CI,
  // },
});