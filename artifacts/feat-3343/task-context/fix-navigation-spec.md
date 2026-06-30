### task: fix-navigation-spec

**Files:**
- Modify: `frontend/test/e2e/stock-operations/navigation.spec.ts`

- [ ] **Step 1: Fix URL assertion on line 13**

  Current (line 13):
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```

  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 2: Fix direct `page.goto` URL on line 96**

  Current (lines 95–96):
  ```typescript
  const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/stock-operations`);
  ```

  Replace with:
  ```typescript
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/stock-up-operations`);
  ```

  Note: the `baseUrl` line gains `process.env.PLAYWRIGHT_FRONTEND_URL ||` at the front to align with the helper's env-var fallback chain (FR-2).

- [ ] **Step 3: Commit**
  ```bash
  git add frontend/test/e2e/stock-operations/navigation.spec.ts
  git commit -m "fix(e2e): correct URL assertion and direct goto in navigation.spec.ts"
  ```
