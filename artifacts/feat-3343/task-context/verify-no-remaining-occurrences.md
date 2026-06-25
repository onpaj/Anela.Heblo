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
