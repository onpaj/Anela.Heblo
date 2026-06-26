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
