# Implementation: update-tests-for-typed-client-methods

## What was implemented

Updated `frontend/src/api/hooks/__tests__/useBankStatements.test.ts` to align with the typed client method calls introduced in the previous task. The old test pattern relied on `createMockApiClient()` which returned `{ baseUrl, http: { fetch: mockFetch } }` and intercepted raw HTTP fetch calls. After the hook was rewritten to call typed generated-client methods, the test must mock those typed methods directly.

Changes made:

1. **Removed `createMockApiClient` import** — no longer needed; replaced by an inline typed mock object.
2. **Replaced `beforeEach` setup** — eliminated `let mockFetch: jest.Mock` and the `createMockApiClient()` call. Now uses a strongly-typed inline mock client with three method mocks:
   - `bankStatements_GetAccounts: jest.fn()`
   - `bankStatements_GetBankStatements: jest.fn()`
   - `bankStatements_ImportStatements: jest.fn()`
   Moved `jest.clearAllMocks()` into `beforeEach` and removed the now-redundant `afterEach`.
3. **Updated all data-returning tests** — replaced `mockFetch.mockResolvedValue({ ok: true, json: () => ... })` with `mockClient.bankStatements_GetAccounts.mockResolvedValue([...])`. The generated typed method returns deserialized objects directly, so the `ok`/`json` envelope is gone.
4. **Replaced URL assertion test** — the test `'should call /api/bank-statements/accounts endpoint'` that asserted `mockFetch` was called with a URL string is replaced with `'should call bankStatements_GetAccounts on the api client'`, which asserts the typed method was called once with no arguments.

All 5 tests pass.

## Files created/modified

- `frontend/src/api/hooks/__tests__/useBankStatements.test.ts` — rewritten test setup and all test cases to use typed method mocks.

## Tests

```
PASS src/api/hooks/__tests__/useBankStatements.test.ts
  useBankStatements - Account Listing
    useBankStatementAccounts
      ✓ should return Comgate CZK account from backend (72 ms)
      ✓ should return ShoptetPay account from backend (55 ms)
      ✓ should return all configured accounts including ShoptetPay (57 ms)
      ✓ should expose value and label for every account (56 ms)
      ✓ should call bankStatements_GetAccounts on the api client (54 ms)

Tests: 5 passed, 5 total
```

## How to verify

```bash
cd /home/user/worktrees/feature-3395-Arch-Review-Bank-Frontend-Hook-Redefines-Dto-Types/frontend
# node_modules must be available (symlink or install)
npm test -- --watchAll=false --testPathPattern="src/api/hooks/__tests__/useBankStatements.test.ts" --no-coverage
```

## Notes

- `mockAuthenticatedApiClient` from `testUtils.ts` is kept — it sets `(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient)`, which works with any client shape.
- The `afterEach(() => jest.clearAllMocks())` block was removed; `jest.clearAllMocks()` is now called at the start of `beforeEach`, which is equivalent and keeps teardown logic co-located with setup.
- Node modules are not installed in the worktree; tests were run by temporarily symlinking `node_modules` from the main project checkout.

## PR Summary

Update `useBankStatements` tests to mock the typed generated-client methods instead of the underlying raw `http.fetch`, matching the hook's refactored implementation.

### Changes
- `frontend/src/api/hooks/__tests__/useBankStatements.test.ts` — replace raw fetch mock with typed method mocks (`bankStatements_GetAccounts`, `bankStatements_GetBankStatements`, `bankStatements_ImportStatements`); replace URL-based assertion test with method-call assertion test.

## Status
DONE
