# Pattern: E2E Test Structure

E2E tests use Playwright and run against the **staging** environment (https://heblo.stg.anela.cz).

## Directory Layout

```
frontend/test/e2e/
├── helpers/        # Shared utilities (e2e-auth-helper.ts, etc.)
├── fixtures/       # Test data (test-data.ts)
├── catalog/        # Catalog module tests
├── issued-invoices/
├── stock-operations/
├── transport/
├── manufacturing/
└── core/           # Dashboard, navigation, auth
```

## Authentication (CRITICAL)

Always use `navigateToApp()` for full auth setup:
```typescript
import { navigateToApp } from '../helpers/e2e-auth-helper';
test.beforeEach(async ({ page }) => {
  await navigateToApp(page);
  await page.goto('/your-page');
});
```

Never use `createE2EAuthSession()` alone — it only sets up the backend session, missing the frontend session, which causes the Microsoft login screen.

## Test Data

Use fixtures from `frontend/test/e2e/fixtures/test-data.ts`. Tests must **fail** (not skip) when expected data is missing — throw a clear error message.

## Running Tests

```bash
./scripts/run-playwright-tests.sh           # All modules
./scripts/run-playwright-tests.sh catalog   # Specific module
```
