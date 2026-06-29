# Code Review: update-tests-for-typed-client-methods

## Summary

The test file has been correctly updated to use a typed inline mock object in place of the raw `mockFetch` + `createMockApiClient` approach. All five acceptance criteria are satisfied: no legacy mock references remain, tests now target the typed client methods, and the method-call assertion test uses both `toHaveBeenCalledTimes(1)` and `toHaveBeenCalledWith()`. The implementation is clean and consistent across every test case.

## Review Result: PASS

### task: update-tests-for-typed-client-methods
**Status:** PASS

## Overall Notes

- `createMockApiClient` is gone from imports; the only remaining imports are `mockAuthenticatedApiClient` and `createQueryClientWrapper` from `testUtils`, which is correct.
- The typed inline mock at lines 9-13 explicitly declares `bankStatements_GetAccounts`, `bankStatements_GetBankStatements`, and `bankStatements_ImportStatements` as `jest.Mock`, satisfying the shape requirement. The two unused methods (`GetBankStatements`, `ImportStatements`) are included proactively, which is fine — they avoid type-narrowing issues if `mockAuthenticatedApiClient` expects the full client shape.
- `jest.clearAllMocks()` is correctly moved into `beforeEach`; the former `afterEach` block has been removed, which is the idiomatic Jest pattern.
- All four data tests (lines 27-104) consistently use `mockClient.bankStatements_GetAccounts.mockResolvedValue([...])` rather than touching `http.fetch`.
- The method-call assertion test (lines 106-116) calls `toHaveBeenCalledTimes(1)` and `toHaveBeenCalledWith()` (no-argument form, correct since `GetAccounts` takes no parameters). Both assertions are present and accurate.
- No `mockFetch` reference appears anywhere in the file.
