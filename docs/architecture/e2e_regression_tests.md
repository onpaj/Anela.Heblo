# E2E Regression Tests for Staging Environment

## Overview

This document defines end-to-end regression tests that run against the deployed staging environment at **https://heblo.stg.anela.cz** using Service Principal authentication. All E2E tests are located in `/frontend/test/e2e/` directory and subfolders.

## Test Environment

**MANDATORY**: All E2E tests MUST run against the deployed staging environment:
- **Target URL**: https://heblo.stg.anela.cz
- **Authentication**: Real Microsoft Entra ID authentication via Service Principal
- **Data**: Live staging data (tests can create their own test data)
- **No local environment setup required**

## Configuration

Tests use credentials from `/frontend/.env.test`:
- `AZURE_CLIENT_ID` - Service Principal Application ID  
- `AZURE_CLIENT_SECRET` - Service Principal Secret
- `AZURE_TENANT_ID` - Azure Tenant ID
- `PLAYWRIGHT_BASE_URL` - https://heblo.stg.anela.cz (staging environment URL)
- `PLAYWRIGHT_FRONTEND_URL` - https://heblo.stg.anela.cz (same as base URL for deployed environment)

## Authentication Strategy

**MANDATORY**: All E2E tests MUST use the shared authentication helper for staging environment authentication.

### Shared E2E Authentication Helper

**Location**: `/frontend/test/e2e/helpers/e2e-auth-helper.ts`  
**Reference implementation**: `/frontend/test/e2e/catalog-ui.spec.ts`

```typescript
// Import shared auth helper and navigation methods in all E2E tests
import { createE2EAuthSession, navigateToCatalog, navigateToPurchase } from './helpers/e2e-auth-helper';

test.describe('Feature E2E Tests', () => {
  test.beforeEach(async ({ page }) => {
    // MANDATORY: Use shared auth helper before each test
    await createE2EAuthSession(page);
  });

  test('should test catalog workflow', async ({ page }) => {
    // RECOMMENDED: Use shared navigation helpers
    await navigateToCatalog(page);
    
    // Test runs as authenticated user - session cookie automatically used
    // Focus on functionality testing, navigation handled by helper
  });

  test('should test purchase workflow', async ({ page }) => {
    // Use appropriate navigation helper for each feature
    await navigateToPurchase(page);
    
    // Test purchase functionality...
  });
});
```

### Backend Test Endpoint

**POST `https://heblo.stg.anela.cz/api/e2etest/auth`** (staging environment only):
- Uses Service Principal authentication with client_credentials flow
- Creates authenticated session cookies for test execution
- Staging environment only for security

### Benefits of Shared Auth Helper

- ✅ **Consistent authentication**: All tests use same reliable auth pattern
- ✅ **Centralized maintenance**: Auth logic updates in one place  
- ✅ **Shared navigation**: Common navigation patterns (sidebar, routing)
- ✅ **Error handling**: Standardized error handling and debugging
- ✅ **Code reuse**: Avoid duplicating auth code across test files

## Running Tests

**MANDATORY**: Always run tests against deployed staging environment at https://heblo.stg.anela.cz

```bash
# Run all E2E regression tests against staging
./scripts/run-playwright-tests.sh

# Run specific test against staging  
./scripts/run-playwright-tests.sh catalog-ui

# Alternative: Direct playwright commands
npx playwright test test/e2e/ --config=playwright.config.ts

# Run with headed browser for debugging
npx playwright test test/e2e/ --headed

# Run with debug mode
npx playwright test test/e2e/ --debug

# Generate test from interactions
npx playwright codegen https://heblo.stg.anela.cz
```

## Test Structure and Main Application Workflows

**MANDATORY**: All E2E tests are located in `/frontend/test/e2e/` directory and subfolders. Main application workflows must be covered with regression tests:

### Core Application Workflows (Required Coverage)

#### 1. Authentication & Navigation (`/test/e2e/auth/`)
- **Authentication flow**: Service Principal → E2E session → User session
- **Navigation**: Sidebar navigation, menu interactions, routing
- **User management**: Login state, user info display, logout
- **Reference**: `catalog-ui.spec.ts` - demonstrates full auth pattern

#### 2. Catalog Management (`/test/e2e/catalog/`)
- **Product catalog**: View products, search, filter, pagination
- **Material catalog**: View materials, categories, specifications
- **Data integration**: Shoptet products + ABRA materials unified view
- **API integration**: Catalog data loading, real-time updates

#### 3. Manufacturing Workflow (`/test/e2e/manufacture/`)
- **2-step production**: Materials → Semi-products → Products
- **Batch planning**: Manufacturing schedules, resource allocation
- **Production tracking**: Status updates, completion workflows
- **Quality control**: Validation steps, approval processes

#### 4. Purchase Management (`/test/e2e/purchase/`)
- **Purchase orders**: Create, edit, approve purchase orders
- **Material shortage**: Detection and notification workflows
- **Supplier management**: Supplier selection, pricing history
- **Delivery tracking**: Expected vs actual delivery dates

#### 5. Transport & Packaging (`/test/e2e/transport/`)
- **Box-level tracking**: EAN code generation and scanning
- **Package management**: Box creation, item allocation
- **Shipping workflows**: Packaging confirmation, dispatch
- **Stock updates**: Automatic Shoptet stock synchronization

#### 6. Invoice Automation (`/test/e2e/invoices/`)
- **Shoptet integration**: Automated invoice scraping
- **ABRA Flexi integration**: Invoice data transfer
- **Processing workflows**: Validation, matching, approval
- **Error handling**: Failed imports, manual interventions

### Test Organization Structure

```
/frontend/test/e2e/
├── helpers/                        # MANDATORY: Shared helper functions
│   ├── e2e-auth-helper.ts         # Authentication and common navigation
│   ├── test-data-helper.ts        # Test data creation utilities
│   └── assertion-helper.ts        # Common assertions and validations
├── auth/
│   ├── authentication.spec.ts     # Login/logout workflows
│   └── navigation.spec.ts         # Sidebar, routing, permissions
├── catalog/
│   ├── catalog-ui.spec.ts          # Product/material catalog (reference implementation)
│   ├── catalog-search.spec.ts      # Search and filtering
│   └── catalog-integration.spec.ts # Shoptet + ABRA data integration
├── manufacture/
│   ├── production-workflow.spec.ts # 2-step production process
│   ├── batch-planning.spec.ts      # Manufacturing schedules
│   └── quality-control.spec.ts     # Validation and approval
├── purchase/
│   ├── purchase-orders.spec.ts     # PO creation and management
│   ├── shortage-detection.spec.ts  # Material shortage workflows
│   └── supplier-management.spec.ts # Supplier and pricing
├── transport/
│   ├── packaging.spec.ts           # Box creation and EAN codes
│   ├── shipping.spec.ts           # Dispatch workflows
│   └── stock-sync.spec.ts         # Shoptet stock updates
├── invoices/
│   ├── shoptet-scraping.spec.ts   # Automated invoice collection
│   ├── abra-integration.spec.ts   # ABRA Flexi data transfer
│   └── processing.spec.ts         # Invoice processing workflows
└── integration/
    ├── end-to-end-flow.spec.ts    # Complete business process
    └── cross-module.spec.ts       # Inter-module dependencies
```

### Shared Helper Structure

**MANDATORY**: All tests must use shared helper functions from `/frontend/test/e2e/helpers/`:

#### `e2e-auth-helper.ts` - Authentication and Navigation
**Authentication:**
- `createE2EAuthSession(page)` - Service Principal authentication (MANDATORY in beforeEach)

**Navigation Methods (RECOMMENDED):**
- `navigateToCatalog(page)` - Navigate to catalog page with error handling
- `navigateToManufacture(page)` - Navigate to manufacturing section
- `navigateToPurchase(page)` - Navigate to purchase management  
- `navigateToTransport(page)` - Navigate to transport section
- `navigateToInvoices(page)` - Navigate to invoice management
- `navigateToAuth(page)` - Navigate to authentication section
- `navigateToSettings(page)` - Navigate to settings page

#### Other Helper Files (when needed)
- **`test-data-helper.ts`** - Test data creation utilities
- **`assertion-helper.ts`** - Common assertions and validations

### Test Data Strategy

**Each test can create its own data** to ensure reliable, isolated testing:

```typescript
test('should create and process purchase order', async ({ page }) => {
  // Create test data within the test
  const testSupplier = await createTestSupplier(page, {
    name: `Test Supplier ${Date.now()}`,
    email: 'test@example.com'
  });
  
  const testMaterial = await createTestMaterial(page, {
    code: `MAT-${Date.now()}`,
    name: 'Test Material',
    supplier: testSupplier.id
  });
  
  // Execute test workflow with created data
  await createPurchaseOrder(page, {
    supplier: testSupplier.id,
    materials: [testMaterial.id]
  });
  
  // Validate results
  await expect(page.locator('[data-testid="po-created"]')).toBeVisible();
  
  // Optional cleanup (or use unique identifiers that don't conflict)
});
```

## Setup Requirements

### Prerequisites
1. **Service Principal pre-configured** in Azure AD with API permissions for staging backend
2. **Credentials configured** in `/frontend/.env.test` (gitignored for security):
   ```
   AZURE_CLIENT_ID=your-service-principal-id
   AZURE_CLIENT_SECRET=your-service-principal-secret  
   AZURE_TENANT_ID=your-tenant-id
   PLAYWRIGHT_BASE_URL=https://heblo.stg.anela.cz
   PLAYWRIGHT_FRONTEND_URL=https://heblo.stg.anela.cz
   ```
3. **Backend test endpoint** `/api/e2etest/auth` configured in staging environment only

### Backend Requirements
- **E2ETestController** in staging environment with Service Principal authentication
- **POST `https://heblo.stg.anela.cz/api/e2etest/auth`** endpoint:
  - Bearer token validation (Microsoft issuer, correct audience)
  - App ID validation (matches expected Service Principal)
  - Synthetic user session creation with required claims
  - Standard application session cookie generation
- **Environment restriction**: Endpoint disabled in production for security

### Test Execution Requirements
- **No local servers needed**: Tests run directly against https://heblo.stg.anela.cz
- **No manual authentication**: Fully automated Service Principal flow
- **Real data environment**: Staging environment with live application state
- **Data isolation**: Tests create their own data or use unique identifiers

## Benefits of This Approach

✅ **Real environment testing**: Tests run against actual deployed staging environment
✅ **Authentic authentication**: Real Microsoft Entra ID authentication via Service Principal  
✅ **No local setup complexity**: No need to start local servers or automation environment
✅ **Fast execution**: Single auth call per test, then standard session cookies
✅ **Complete workflow coverage**: End-to-end business process validation
✅ **Data flexibility**: Each test can create its own test data
✅ **Production-like behavior**: Staging environment mirrors production deployment
✅ **Secure token validation**: Full Azure AD verification, not mock authentication
✅ **Maintenance efficiency**: Tests validate actual deployed functionality

## Usage Guidelines

- **Always run against staging**: Never run E2E tests against local or production environments
- **Use `./scripts/run-playwright-tests.sh`**: Preferred method for running tests
- **MANDATORY: Use shared auth helper**: Import `createE2EAuthSession` from `./helpers/e2e-auth-helper`
- **Use shared navigation helpers**: Import navigation functions instead of duplicating logic
- **Follow catalog-ui.spec.ts pattern**: Reference implementation for authentication and structure
- **Create test data within tests**: Ensure test isolation and reliability
- **Cover main workflows**: Focus on core business processes and user journeys
- **Validate real integrations**: Test actual Shoptet and ABRA Flexi connections