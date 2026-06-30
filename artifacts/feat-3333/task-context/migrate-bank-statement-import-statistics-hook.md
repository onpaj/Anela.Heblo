### task: migrate-bank-statement-import-statistics-hook

**Goal:** Replace the raw fetch call in `useBankStatements.ts` with `apiClient.analytics_GetBankStatementImportStatistics(...)`, remove duplicate interfaces, re-export generated equivalents, and retain `GetBankStatementImportStatisticsRequest` with string dates (converting to `Date` inside `queryFn`).

**Files to change:**
- `frontend/src/api/hooks/useBankStatements.ts` — replace raw fetch with typed client call; remove duplicate interfaces (except `GetBankStatementImportStatisticsRequest`); add re-exports; convert string dates to `Date` before passing to client method; add `?? []` fallback for `statistics` field

**Implementation steps:**
1. Remove the hand-written duplicate interfaces (all except `GetBankStatementImportStatisticsRequest`).
2. Add re-export statements for the generated equivalents from the generated client barrel.
3. Keep `GetBankStatementImportStatisticsRequest` with `string` date fields as-is (it is part of the public API of this hook file per FR-7).
4. In the `queryFn`, convert the string date fields from `GetBankStatementImportStatisticsRequest` to `Date` objects before calling `apiClient.analytics_GetBankStatementImportStatistics(...)`.
5. Add `?? []` fallback when accessing the `statistics` field of the response (since the generated type declares it `DailyBankStatementStatistics[] | undefined`).
6. Ensure no `(apiClient as any)` usage remains.

**Acceptance criteria:**
- No TypeScript errors in this file.
- `GetBankStatementImportStatisticsRequest` interface is still exported with string date fields.
- No `(apiClient as any)` usage remains.
- `npm run build` passes.

**Dependencies:** none
