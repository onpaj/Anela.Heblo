# Playwright E2E Testing Guide

This document provides comprehensive guidance for writing and running end-to-end (E2E) tests using Playwright in the Anela Heblo project.

## Overview

**Playwright** is used for E2E testing to validate user workflows and UI behavior in the deployed staging environment.

**Key characteristics:**
- Tests run against **live staging environment** (https://heblo.stg.anela.cz)
- Uses **real Microsoft Entra ID authentication** (not mock)
- Tests complete user journeys with real backend data
- Run **nightly in CI/CD** for regression testing (not in PR builds)

## Test Environment

### Target URL

**MANDATORY**: All Playwright tests MUST run against the deployed staging environment.

```typescript
// Configuration in playwright.config.ts
export default defineConfig({
  use: {
    baseURL: process.env.E2E_BASE_URL || 'https://heblo.stg.anela.cz',
  },
});
```

**Why staging, not localhost?**
- Tests real deployment environment and configuration
- Validates complete integration with Azure infrastructure
- Tests actual authentication flow with Microsoft Entra ID
- Ensures responsive design works with deployed styles
- Faster than local setup (no need to start frontend + backend)

### Test Location

```
frontend/test/e2e/
├── auth/                    # Authentication flows
├── navigation/              # Navigation and routing
├── layout/                  # Layout and responsive design
├── features/                # Feature-specific user journeys
│   ├── gift-package-disassembly.spec.ts
│   ├── manufacture-batch-planning-workflow.spec.ts
│   └── recurring-jobs-management.spec.ts
├── stock-operations/        # Stock operations tests
├── changelog/               # Changelog functionality
├── helpers/                 # Test helpers and utilities
│   ├── e2e-auth-helper.ts  # Authentication helpers (CRITICAL)
│   └── ...
├── fixtures/                # Test data fixtures
│   └── test-data.ts        # Test data definitions
└── *.spec.ts                # Test files
```

## Authentication - CRITICAL RULES

### The Problem

Without proper authentication setup, tests will encounter the **Microsoft Entra ID login screen** and fail.

**Authentication has two parts:**
1. **Backend authentication** - Service principal token for API calls
2. **Frontend session** - Session cookies and storage for React app

**BOTH are required** for tests to work.

### The Solution: Navigation Helpers

**MANDATORY**: Always use navigation helper functions that include full authentication.

#### ✅ CORRECT: Use navigateToApp()

```typescript
import { navigateToApp } from './helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  // Full authentication: backend session + frontend session
  await navigateToApp(page);

  // Now navigate to specific page
  await page.goto('/catalog');
});
```

#### ✅ CORRECT: Use Feature-Specific Navigation Helpers

```typescript
import {
  navigateToCatalog,
  navigateToTransportBoxes,
  navigateToStockOperations
} from './helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  // These helpers include full authentication setup
  await navigateToCatalog(page);
  // Already on catalog page with full auth
});
```

#### ❌ WRONG: Never Use createE2EAuthSession() Alone

```typescript
import { createE2EAuthSession } from './helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  await createE2EAuthSession(page);  // ❌ Only backend session
  await page.goto('/catalog');       // ❌ Will show Microsoft login!
});
```

### Authentication Flow Explained

**What happens with `navigateToApp()`:**

1. **Backend authentication:**
   - Calls `createE2EAuthSession(page)`
   - Requests service principal token from Microsoft Entra ID
   - Stores token for API calls

2. **Frontend session setup:**
   - Calls `navigateToAppWithServicePrincipal(page, token)`
   - Sets E2E session cookie for staging domain
   - Stores E2E token in `sessionStorage`
   - Sets E2E mode flag

3. **React app initialization:**
   - Waits for React app to load and initialize
   - Verifies user is authenticated
   - Ensures UI is ready for interaction

4. **✅ Test can now interact with authenticated app**

**Why both are needed:**
- Backend token: API calls return data instead of 401 errors
- Frontend session: React app recognizes user as authenticated
- Without frontend session: React triggers OAuth flow → Microsoft login screen

### Available Navigation Helpers

```typescript
// Generic app navigation (root page)
await navigateToApp(page);

// Feature-specific navigation (includes auth)
await navigateToCatalog(page);
await navigateToTransportBoxes(page);
await navigateToStockOperations(page);
await navigateToManufacture(page);
await navigateToPurchaseOrders(page);
```

**When to use each:**
- **navigateToApp()**: When starting at app root, then navigating manually
- **navigateToCatalog()**: When testing catalog-specific features
- **navigateToTransportBoxes()**: When testing transport box features
- **navigateToStockOperations()**: When testing stock operations
- **Never use createE2EAuthSession() alone**: Always combine with navigation helpers

### E2E Credentials Setup

**Create `frontend/.env.test` file (gitignored):**

```bash
# Azure Service Principal for E2E Testing
E2E_CLIENT_ID=<service-principal-client-id>
E2E_CLIENT_SECRET=<service-principal-client-secret>
E2E_TENANT_ID=<tenant-id>

# Staging Environment
E2E_BASE_URL=https://heblo.stg.anela.cz
```

**Load credentials in tests:**

```typescript
import { loadTestCredentials } from './helpers/credential-loader';

test.beforeAll(async () => {
  const credentials = await loadTestCredentials();
  // Credentials are now available for authentication
});
```

**CRITICAL**: Never commit `.env.test` to git! It's in `.gitignore`.

### Authentication Troubleshooting

**Symptom: Test shows Microsoft login screen**

**Fix:** Use proper navigation helpers:
```typescript
// Before (WRONG)
await createE2EAuthSession(page);
await page.goto('/catalog');

// After (CORRECT)
await navigateToCatalog(page);
```

**Symptom: 401 Unauthorized errors in console**

**Fix:** Verify E2E credentials:
```bash
# Check .env.test exists
cat frontend/.env.test

# Verify credentials are loaded
# Should see "✅ E2E environment variables validated" in test output
```

**Symptom: Authentication succeeds locally but fails in CI**

**Fix:** Ensure CI has E2E credentials configured as secrets:
- GitHub Secrets: `E2E_CLIENT_ID`, `E2E_CLIENT_SECRET`, `E2E_TENANT_ID`
- See `.github/workflows/e2e-nightly-regression.yml`

## Test Data Fixtures

### Why Use Fixtures?

Tests need **consistent, reliable data** to validate functionality. Hardcoding values leads to brittle tests.

**Benefits of fixtures:**
- ✅ Centralized test data management
- ✅ Easy to update when data changes
- ✅ Self-documenting test expectations
- ✅ Consistent across all tests

### Available Test Data

See `docs/testing/test-data-fixtures.md` for complete list.

**Categories:**
1. **Catalog Items**: Products and materials (Bisabolol, Glycerol, etc.)
2. **Purchase Orders**: Orders in various states
3. **Manufacturing Orders**: Production batches
4. **Transport Boxes**: Boxes in different states
5. **Suppliers**: Known suppliers

### Usage Example

```typescript
import { TestCatalogItems, requireTestData } from './fixtures/test-data';

test('should filter by product name', async ({ page }) => {
  await navigateToCatalog(page);

  // Use fixture instead of hardcoded value
  const product = TestCatalogItems.bisabolol;
  console.log(`🔍 Searching for: ${product.name} (${product.code})`);

  // Apply filter
  await page.fill('input[placeholder*="Název"]', product.name);
  await page.keyboard.press('Enter');

  // Verify results
  const rowCount = await page.locator('tbody tr').count();

  // CRITICAL: Fail if data not found, don't skip
  if (rowCount === 0) {
    throw new Error(
      `Test data missing: Expected to find "${product.name}" (${product.code}). ` +
      `Test fixtures may be outdated.`
    );
  }

  expect(rowCount).toBeGreaterThan(0);
});
```

### CRITICAL: Fail, Don't Skip

**MANDATORY**: Tests MUST fail when expected data is missing, never skip.

**❌ WRONG - Skipping test:**
```typescript
const rowCount = await page.locator('tbody tr').count();
if (rowCount === 0) {
  test.skip(); // ❌ Hides the problem!
}
```

**✅ CORRECT - Failing with clear message:**
```typescript
const rowCount = await page.locator('tbody tr').count();
if (rowCount === 0) {
  throw new Error(
    `Test data missing: Expected to find "${product.name}". ` +
    `Check if test data fixtures are up to date.`
  );
}
```

**Why?**
- Skipped tests hide data problems
- Failed tests alert us to fixture updates needed
- Clear error messages help debugging

## Running Tests

### Quick Start

```bash
# Run all E2E tests against staging
./scripts/run-playwright-tests.sh

# Run specific test file
./scripts/run-playwright-tests.sh catalog-ui

# Run specific test by name pattern
./scripts/run-playwright-tests.sh "should filter by product"
```

### Playwright Commands

```bash
# Install Playwright browsers (run once)
npx playwright install

# Run all tests
npx playwright test

# Run specific test file
npx playwright test catalog-ui.spec.ts

# Run tests in headed mode (visible browser)
npx playwright test --headed

# Run tests in debug mode
npx playwright test --debug

# Run tests in specific browser
npx playwright test --project=chromium
npx playwright test --project=firefox
npx playwright test --project=webkit

# View test report
npx playwright show-report

# Record interactions (codegen)
npx playwright codegen https://heblo.stg.anela.cz
```

### Test Script (Recommended)

The `./scripts/run-playwright-tests.sh` script provides:
- ✅ Automatic environment variable loading from `.env.test`
- ✅ Staging environment health check
- ✅ Clear test output with logging
- ✅ Optional test pattern filtering

```bash
#!/bin/bash
# Usage examples:

# Run all tests
./scripts/run-playwright-tests.sh

# Run specific test
./scripts/run-playwright-tests.sh transport-box-workflow

# Run tests matching pattern
./scripts/run-playwright-tests.sh "catalog filter"
```

## Writing Tests

### Test Structure

```typescript
import { test, expect } from '@playwright/test';
import { navigateToCatalog } from './helpers/e2e-auth-helper';
import { TestCatalogItems } from './fixtures/test-data';

test.describe('Catalog Filtering E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // Use navigation helper (includes full auth)
    await navigateToCatalog(page);
  });

  test('should filter by product name', async ({ page }) => {
    // Use test fixture
    const product = TestCatalogItems.bisabolol;

    // Perform action
    await page.fill('input[placeholder*="Název"]', product.name);
    await page.keyboard.press('Enter');

    // Wait for results
    await page.waitForTimeout(1000);

    // Verify results
    const rowCount = await page.locator('tbody tr').count();

    // Fail if data not found
    if (rowCount === 0) {
      throw new Error(`Test data missing: ${product.name}`);
    }

    expect(rowCount).toBeGreaterThan(0);
  });

  test('should clear filters', async ({ page }) => {
    // Apply filter
    await page.fill('input[placeholder*="Název"]', 'Test');
    await page.keyboard.press('Enter');

    // Clear filter
    await page.click('button[aria-label="Vymazat filtry"]');

    // Verify cleared
    const value = await page.inputValue('input[placeholder*="Název"]');
    expect(value).toBe('');
  });
});
```

### Best Practices

**1. Use Page Object Pattern (Optional)**
```typescript
class CatalogPage {
  constructor(private page: Page) {}

  async filterByName(name: string) {
    await this.page.fill('input[placeholder*="Název"]', name);
    await this.page.keyboard.press('Enter');
  }

  async clearFilters() {
    await this.page.click('button[aria-label="Vymazat filtry"]');
  }

  async getRowCount() {
    return this.page.locator('tbody tr').count();
  }
}

// Usage
test('should filter catalog', async ({ page }) => {
  const catalog = new CatalogPage(page);
  await navigateToCatalog(page);

  await catalog.filterByName('Bisabolol');
  expect(await catalog.getRowCount()).toBeGreaterThan(0);
});
```

**2. Use Data-Testid for Stable Selectors**
```typescript
// In React component
<button data-testid="clear-filters-btn">Vymazat filtry</button>

// In test
await page.click('[data-testid="clear-filters-btn"]');
```

**3. Wait for Network Idle or Specific Elements**
```typescript
// Wait for network to settle
await page.waitForLoadState('networkidle');

// Wait for specific element
await page.waitForSelector('tbody tr', { timeout: 10000 });

// Wait for condition
await page.waitForFunction(() => {
  return document.querySelectorAll('tbody tr').length > 0;
});
```

**4. Handle Dynamic Content**
```typescript
// Wait for loading state to disappear
await page.waitForSelector('.loading-spinner', { state: 'hidden' });

// Wait for content to appear
await page.waitForSelector('tbody tr:first-child');

// Retry logic for flaky elements
await test.step('Wait for results', async () => {
  await expect(async () => {
    const count = await page.locator('tbody tr').count();
    expect(count).toBeGreaterThan(0);
  }).toPass({ timeout: 10000 });
});
```

**5. Screenshot on Failure**
```typescript
test('should display catalog', async ({ page }) => {
  try {
    await navigateToCatalog(page);
    expect(await page.locator('h1').textContent()).toContain('Katalog');
  } catch (error) {
    await page.screenshot({ path: 'test-failure.png', fullPage: true });
    throw error;
  }
});
```

## Test Organization

### Test Categories

**Unit & Integration Tests (Jest + React Testing Library)**
- Location: `__tests__/` folders co-located with components
- Purpose: Test component logic in isolation
- Run with: `npm test`

**E2E Tests (Playwright)**
- Location: `/frontend/test/e2e/` directory
- Purpose: Test complete user journeys
- Run with: `./scripts/run-playwright-tests.sh`

### Folder Structure

```
frontend/test/e2e/
├── auth/                    # Authentication and login flows
├── navigation/              # Page navigation and routing
├── layout/                  # Responsive design and layout
├── features/                # Feature-specific journeys
│   ├── gift-package-disassembly.spec.ts
│   ├── manufacture-batch-planning-workflow.spec.ts
│   └── recurring-jobs-management.spec.ts
├── stock-operations/        # Stock operation tests
├── catalog*.spec.ts         # Catalog feature tests
├── transport*.spec.ts       # Transport box tests
└── *.spec.ts                # Other feature tests
```

**Naming conventions:**
- Test files: `{feature}-{aspect}.spec.ts`
- Test describes: `{Feature} {Aspect} E2E Tests`
- Test cases: `should {expected behavior}`

## CI/CD Integration

### Nightly Regression Testing

E2E tests run **every night at 2:00 AM CET** via GitHub Actions.

**Workflow:** `.github/workflows/e2e-nightly-regression.yml`

**Flow:**
1. Validate staging health (`/health/live`, `/health/ready`)
2. Run full Playwright E2E suite against staging
3. Upload test results (HTML report, screenshots, videos)
4. Create GitHub issue if tests fail (auto-closes on success)
5. Send Teams notification (optional)

**Configuration:**
```yaml
schedule:
  - cron: '0 1 * * *'  # 1:00 AM UTC = 2:00 AM CET

env:
  E2E_BASE_URL: https://heblo.stg.anela.cz
  E2E_CLIENT_ID: ${{ secrets.E2E_CLIENT_ID }}
  E2E_CLIENT_SECRET: ${{ secrets.E2E_CLIENT_SECRET }}
  E2E_TENANT_ID: ${{ secrets.E2E_TENANT_ID }}
```

### Not in PR Builds

**E2E tests do NOT run in PR CI** for faster feedback:
- PR builds: Jest + .NET tests only (15-20 min)
- Nightly builds: Full E2E suite (10-15 min)

**Rationale:**
- Faster PR feedback loop
- E2E tests cover integration, not unit logic
- Nightly regression catches integration issues

### Manual Trigger

Run E2E tests manually via GitHub Actions:

1. Go to Actions tab in GitHub
2. Select "E2E Nightly Regression" workflow
3. Click "Run workflow"
4. Optional: Specify test pattern filter

## Visual Testing & Recording

### Recording Interactions

Use Playwright's codegen tool to record interactions:

```bash
# Record interactions on staging
npx playwright codegen https://heblo.stg.anela.cz

# Record with specific viewport
npx playwright codegen --viewport-size=390,844 https://heblo.stg.anela.cz

# Record with device emulation
npx playwright codegen --device="iPhone 13" https://heblo.stg.anela.cz
```

**Generated code can be copy-pasted into test files.**

### Taking Screenshots

```typescript
// Screenshot entire page
await page.screenshot({ path: 'catalog.png', fullPage: true });

// Screenshot specific element
await page.locator('.catalog-table').screenshot({ path: 'table.png' });

// Screenshot with mask (hide sensitive data)
await page.screenshot({
  path: 'catalog.png',
  mask: [page.locator('.user-email')],
});
```

### Recording Videos

Videos are automatically recorded when tests fail (configured in `playwright.config.ts`):

```typescript
export default defineConfig({
  use: {
    video: 'retain-on-failure', // Only keep videos for failed tests
    screenshot: 'only-on-failure',
  },
});
```

## Debugging Tests

### Debug Mode

```bash
# Run in debug mode (opens Playwright Inspector)
npx playwright test --debug

# Debug specific test
npx playwright test catalog-ui.spec.ts --debug

# Pause on failure
npx playwright test --pause-on-failure
```

### Browser Developer Tools

```bash
# Run with visible browser and dev tools
npx playwright test --headed --debug
```

### Console Logging

```typescript
test('should display catalog', async ({ page }) => {
  // Log page console messages
  page.on('console', (msg) => console.log('PAGE LOG:', msg.text()));

  // Log network requests
  page.on('request', (req) => console.log('REQUEST:', req.url()));
  page.on('response', (res) => console.log('RESPONSE:', res.url(), res.status()));

  await navigateToCatalog(page);
});
```

### Trace Viewer

```bash
# Run with trace
npx playwright test --trace on

# View trace
npx playwright show-trace trace.zip
```

Trace includes:
- DOM snapshots
- Network activity
- Console logs
- Screenshots
- Timeline

## Common Patterns

### Waiting for Data to Load

```typescript
// Wait for loading spinner to disappear
await page.waitForSelector('.loading-spinner', { state: 'hidden' });

// Wait for table rows to appear
await page.waitForSelector('tbody tr', { timeout: 10000 });

// Wait for specific count
await expect(page.locator('tbody tr')).toHaveCount(20, { timeout: 10000 });
```

### Filling Forms

```typescript
// Fill text input
await page.fill('input[name="productName"]', 'Bisabolol');

// Select dropdown
await page.selectOption('select[name="productType"]', 'Material');

// Check checkbox
await page.check('input[type="checkbox"][name="inStock"]');

// Click button
await page.click('button[type="submit"]');
```

### Assertions

```typescript
// Element visible
await expect(page.locator('h1')).toBeVisible();

// Element text
await expect(page.locator('h1')).toHaveText('Katalog');

// Element count
await expect(page.locator('tbody tr')).toHaveCount(20);

// URL
await expect(page).toHaveURL(/.*catalog/);

// Custom condition
await expect(async () => {
  const count = await page.locator('tbody tr').count();
  expect(count).toBeGreaterThan(0);
}).toPass({ timeout: 10000 });
```

## Additional Resources

- **Test Data Fixtures**: `docs/testing/test-data-fixtures.md`
- **Setup Commands**: `docs/development/setup.md`
- **Architecture**: `docs/architecture/filesystem.md`
- **Playwright Documentation**: https://playwright.dev/
