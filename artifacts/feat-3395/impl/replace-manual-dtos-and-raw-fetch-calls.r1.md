# Implementation: replace-manual-dtos-and-raw-fetch-calls

## What was implemented

Removed five hand-written interface definitions that duplicated types already exported by the generated API client, and replaced three raw `(apiClient as any).http.fetch(...)` calls with typed generated client method calls. The hook file now imports and re-exports the canonical types from `'../generated/api-client'`.

## Files created/modified

- `frontend/src/api/hooks/useBankStatements.ts` — replaced import line with multi-type import block; added re-exports for `GetBankStatementListResponse`, `BankStatementImportDto`, `BankStatementImportResultDto`; deleted `BankStatementImportDto`, `GetBankStatementListResponse`, `BankStatementImportResult`, `BankImportResponse`, and `BankAccountDto` interface declarations; replaced `useBankStatementsList` queryFn (URL-param building + raw fetch) with `apiClient.bankStatements_GetBankStatements(...)` call; replaced `useBankStatementImport` mutationFn (raw POST fetch) with `apiClient.bankStatements_ImportStatements(new BankImportRequestDto(...))` call; replaced `useBankStatementAccounts` queryFn (raw GET fetch + manual JSON parse) with `apiClient.bankStatements_GetAccounts()` call followed by `.map()`

## Tests

- `npx tsc --noEmit` completed with no type errors (only pre-existing tsconfig deprecation warnings about `target=ES5` and `moduleResolution=node10` which are unrelated to this change).

## How to verify

1. `cd frontend && npx tsc --noEmit` — should produce no type errors beyond the pre-existing tsconfig deprecation warnings.
2. Confirm no `(apiClient as any)` references remain in `useBankStatements.ts`.
3. Run `npm run build` in `frontend/` — should complete without errors.
4. Smoke-test the Import tab in the running app: bank statement list loads, import runs, accounts populate in the dropdown.

## Notes

- `GetBankStatementListResponse.totalCount` is `number | undefined` in the generated class; `ImportTab.tsx` accesses it as `data?.totalCount || 0` which safely handles `undefined`, so no changes were needed there.
- The `BankAccountDto` fields (`name`, `accountNumber`, `provider`, `currency`) are all `string | undefined` in the generated class, so the map in `useBankStatementAccounts` uses `?? ''` fallbacks — matching the task spec exactly.
- `getAuthenticatedApiClient()` is synchronous; all three replaced hooks now call it without `await`, consistent with the rest of the file.

## PR Summary

Replaces five hand-written interface definitions and three raw `fetch` calls in `useBankStatements.ts` with typed calls on the auto-generated API client. This eliminates a maintenance burden (manual types diverging from the backend contract) and removes `(apiClient as any)` casts.

### Changes

- `frontend/src/api/hooks/useBankStatements.ts` — import generated types (`GetBankStatementListResponse`, `BankStatementImportDto`, `BankStatementImportResultDto`, `BankAccountDto`, `BankImportRequestDto`); re-export the first three for consumers; delete five duplicate interface declarations; replace raw fetch implementations in `useBankStatementsList`, `useBankStatementImport`, and `useBankStatementAccounts` with typed generated client calls

## Status
DONE
