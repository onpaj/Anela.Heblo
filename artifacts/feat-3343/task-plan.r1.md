# Fix Stock-Operations E2E Navigation URL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all E2E test files in the `stock-operations` module that navigate to the non-existent `/stock-operations` route, replacing it with the real application route `/stock-up-operations`.

**Architecture:** This is a pure test-file fix — no application source is touched. The shared navigation helper `navigateToStockOperations` in `e2e-auth-helper.ts` has the wrong `page.goto` URL, and every spec file has a corresponding URL assertion that must match. Correcting the helper plus fixing the assertions in all seven spec files restores all 56 tests.

**Tech Stack:** Playwright E2E tests (TypeScript), running against staging via `./scripts/run-playwright-tests.sh`.

---

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

---

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

---

### task: fix-spec-url-assertions

**Files:**
- Modify: `frontend/test/e2e/stock-operations/badges.spec.ts`
- Modify: `frontend/test/e2e/stock-operations/accept.spec.ts`
- Modify: `frontend/test/e2e/stock-operations/state-filter.spec.ts`
- Modify: `frontend/test/e2e/stock-operations/source-filter.spec.ts`
- Modify: `frontend/test/e2e/stock-operations/sorting.spec.ts`
- Modify: `frontend/test/e2e/stock-operations/retry.spec.ts`
- Modify: `frontend/test/e2e/stock-operations/panel.spec.ts`

Each of these files has a single `.toContain('/stock-operations')` assertion in its `test.beforeEach` block. The fix is identical in every file.

- [ ] **Step 1: Fix `badges.spec.ts` line 15**

  Current:
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```
  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 2: Fix `accept.spec.ts` line 13**

  Current:
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```
  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 3: Fix `state-filter.spec.ts` line 14**

  Current:
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```
  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 4: Fix `source-filter.spec.ts` line 13**

  Current:
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```
  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 5: Fix `sorting.spec.ts` line 14**

  Current:
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```
  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 6: Fix `retry.spec.ts` line 15**

  Current:
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```
  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 7: Fix `panel.spec.ts` line 18**

  Current:
  ```typescript
  expect(page.url()).toContain('/stock-operations');
  ```
  Replace with:
  ```typescript
  expect(page.url()).toContain('/stock-up-operations');
  ```

- [ ] **Step 8: Commit**
  ```bash
  git add \
    frontend/test/e2e/stock-operations/badges.spec.ts \
    frontend/test/e2e/stock-operations/accept.spec.ts \
    frontend/test/e2e/stock-operations/state-filter.spec.ts \
    frontend/test/e2e/stock-operations/source-filter.spec.ts \
    frontend/test/e2e/stock-operations/sorting.spec.ts \
    frontend/test/e2e/stock-operations/retry.spec.ts \
    frontend/test/e2e/stock-operations/panel.spec.ts
  git commit -m "fix(e2e): correct URL assertions in stock-operations spec files"
  ```

---

### task: verify-no-remaining-occurrences

**Files:**
- No changes — verification only.

- [ ] **Step 1: Grep for any remaining `/stock-operations` occurrences (must exclude `/stock-up-operations`)**

  Run from the project root:
  ```bash
  grep -r '/stock-operations' frontend/test/e2e/ | grep -v '/stock-up-operations'
  ```

  Expected result: **no output**. If any lines appear, fix them before proceeding.

- [ ] **Step 2: Confirm `/stock-up-operations` is present in all expected files**

  ```bash
  grep -r '/stock-up-operations' frontend/test/e2e/
  ```

  Expected: occurrences in `e2e-auth-helper.ts` and all eight spec files (`navigation.spec.ts`, `badges.spec.ts`, `accept.spec.ts`, `state-filter.spec.ts`, `source-filter.spec.ts`, `sorting.spec.ts`, `retry.spec.ts`, `panel.spec.ts`).

- [ ] **Step 3: Run the E2E suite against staging**

  ```bash
  ./scripts/run-playwright-tests.sh
  ```

  All 56 `stock-operations` tests should pass. No app source files should have been modified.
