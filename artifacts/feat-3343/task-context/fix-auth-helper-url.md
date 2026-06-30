### task: fix-auth-helper-url

**Files:**
- Modify: `frontend/test/e2e/helpers/e2e-auth-helper.ts`

- [ ] **Step 1: Fix the `page.goto` URL in `navigateToStockOperations`**

  File: `frontend/test/e2e/helpers/e2e-auth-helper.ts`, line 270.

  Current code (lines 268–270):
  ```typescript
  // Direct navigation to stock operations
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/stock-operations`);
  ```

  Replace with:
  ```typescript
  // Direct navigation to stock operations
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/stock-up-operations`);
  ```

  Only the string `/stock-operations` inside the template literal changes to `/stock-up-operations`. The `baseUrl` env-var chain is already correct (`PLAYWRIGHT_FRONTEND_URL || PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz'`); do not alter it.

- [ ] **Step 2: Commit**
  ```bash
  git add frontend/test/e2e/helpers/e2e-auth-helper.ts
  git commit -m "fix(e2e): correct navigateToStockOperations URL to /stock-up-operations"
  ```
