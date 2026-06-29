# Code Review: replace-manual-dtos-and-raw-fetch-calls

## Summary
All five hand-written interface declarations that duplicated generated types have been removed and replaced with imports from `../generated/api-client`. The three raw `(apiClient as any)` fetch calls have been replaced with typed generated client method calls. All seven functional requirements and the NFR are satisfied.

## Review Result: PASS

### task: replace-manual-dtos-and-raw-fetch-calls
**Status:** PASS

## Overall Notes
- FR-1: `BankStatementImportDto` is no longer declared locally; it is imported from `../generated/api-client` (line 8) and re-exported via `export type { ... }` (line 19). Satisfied.
- FR-2: `GetBankStatementListResponse` is no longer declared locally; imported (line 7) and re-exported (line 18). Satisfied.
- FR-3: `GetBankStatementListRequest` is retained as a hook-layer adapter interface (lines 29-42) — this is correct per spec ("adapter is OK").
- FR-4: `useBankStatementsList` calls `apiClient.bankStatements_GetBankStatements(...)` directly with no `(apiClient as any)` cast (lines 68-81). Satisfied.
- FR-5: `useBankStatementImport` constructs a `new BankImportRequestDto(...)` with `dateFrom` and `dateTo` converted via `new Date(...)` (lines 97-101) and calls `apiClient.bankStatements_ImportStatements(dto)` (line 102). Satisfied.
- FR-6: `useBankStatementAccounts` calls `apiClient.bankStatements_GetAccounts()` with no cast (line 122). Satisfied.
- FR-7: `GetBankStatementListResponse`, `BankStatementImportDto`, and `BankStatementImportResultDto` are all re-exported from the file (lines 14-21). Satisfied.
- NFR-3: No `(apiClient as any)` references remain anywhere in the file. Satisfied.
- The implementation note that `getAuthenticatedApiClient()` is synchronous is consistent with the call sites (no `await` on the client constructor call, only on the method call in `useBankStatementAccounts`).
- `BankAccountDto` is imported from the generated client and used for the `.map()` type annotation (line 123) but is not re-exported — this is correct since it is an internal implementation detail of the hook.
- The `?? ''` fallbacks for `string | undefined` generated fields (lines 124-128) are appropriate defensive coding and do not violate any spec requirement.
