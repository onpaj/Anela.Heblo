### task: replace-manual-dtos-and-raw-fetch-calls

**Files:**
- Modify: `frontend/src/api/hooks/useBankStatements.ts`

**Context:**

The file currently defines five hand-written interfaces that duplicate types already exported by the generated client, and uses `(apiClient as any).http.fetch(...)` for three hooks. This task removes all of that and replaces it with typed generated client calls.

**Types to delete** (exact names, all currently in `useBankStatements.ts`):
- `export interface BankStatementImportDto` (lines 13–23) — duplicates generated class
- `export interface GetBankStatementListResponse` (lines 25–28) — duplicates generated class
- `export interface BankStatementImportResult` (lines 136–138) — unused externally, remove
- `export interface BankImportResponse` (lines 140–142) — unused externally, remove
- `interface BankAccountDto` (lines 179–184) — private, duplicates generated class

**Types to keep** (hook-layer adapters, do not touch):
- `export interface GetBankStatementListRequest` — 12-field request shape mapping to positional params
- `export interface BankImportRequest` — string dates; hook converts to `Date` internally
- `export interface AccountOption` — view-model for UI selects
- `export interface GetBankStatementImportStatisticsRequest` — unchanged

- [ ] Step 1: Add imports from the generated client. At the top of the file, extend the existing import from `'../generated/api-client'` to include the types being brought in. The existing import line is:
  ```typescript
  import { GetBankStatementImportStatisticsResponse, DailyBankStatementStatistics, BankStatementDateType } from '../generated/api-client';
  ```
  Replace it with:
  ```typescript
  import {
    GetBankStatementImportStatisticsResponse,
    DailyBankStatementStatistics,
    BankStatementDateType,
    GetBankStatementListResponse,
    BankStatementImportDto,
    BankStatementImportResultDto,
    BankAccountDto,
    BankImportRequestDto,
  } from '../generated/api-client';
  ```

- [ ] Step 2: Add re-exports for the generated types so consumers that imported them from `useBankStatements` continue to work. Extend the existing `export type { ... }` line on line 5:
  ```typescript
  export type { GetBankStatementImportStatisticsResponse, DailyBankStatementStatistics, BankStatementDateType };
  ```
  Replace with:
  ```typescript
  export type {
    GetBankStatementImportStatisticsResponse,
    DailyBankStatementStatistics,
    BankStatementDateType,
    GetBankStatementListResponse,
    BankStatementImportDto,
    BankStatementImportResultDto,
  };
  ```

- [ ] Step 3: Delete the five hand-written interface blocks. Remove these exact blocks (preserve surrounding blank lines for readability but eliminate the interface declarations):
  - `export interface BankStatementImportDto { ... }` (lines 13–23)
  - `export interface GetBankStatementListResponse { ... }` (lines 25–28)
  - `export interface BankStatementImportResult { ... }` (lines 136–138)
  - `export interface BankImportResponse { ... }` (lines 140–142)
  - `interface BankAccountDto { ... }` (lines 179–184)

- [ ] Step 4: Replace the `useBankStatementsList` `queryFn` body. The current implementation builds a `URLSearchParams` and calls `(apiClient as any).http.fetch(...)`. Replace the entire `queryFn` implementation with:
  ```typescript
  queryFn: (): Promise<GetBankStatementListResponse> => {
    const apiClient = getAuthenticatedApiClient();
    return apiClient.bankStatements_GetBankStatements(
      request?.id ?? undefined,
      request?.transferId?.trim() ?? undefined,
      request?.account?.trim() ?? undefined,
      request?.statementDate ?? undefined,
      request?.importDate ?? undefined,
      request?.dateFrom ?? undefined,
      request?.dateTo ?? undefined,
      request?.errorsOnly ?? undefined,
      request?.skip,
      request?.take,
      request?.orderBy ?? undefined,
      request?.ascending
    );
  },
  ```
  Note: `getAuthenticatedApiClient()` is synchronous (returns `ApiClient` directly) — remove the `await` that was previously on it.

- [ ] Step 5: Replace the `useBankStatementImport` `mutationFn` body. The current implementation calls `(apiClient as any).http.fetch(...)` with a `POST` to `/api/bank-statements/import`. Replace the entire `mutationFn` implementation with:
  ```typescript
  mutationFn: async (request: BankImportRequest): Promise<BankStatementImportResultDto> => {
    const apiClient = getAuthenticatedApiClient();
    const dto = new BankImportRequestDto({
      accountName: request.accountName,
      dateFrom: new Date(request.dateFrom),
      dateTo: new Date(request.dateTo),
    });
    return apiClient.bankStatements_ImportStatements(dto);
  },
  ```

- [ ] Step 6: Replace the `useBankStatementAccounts` `queryFn` body. The current implementation calls `(apiClient as any).http.fetch(...)` to `/api/bank-statements/accounts` and manually parses JSON. Replace the entire `queryFn` implementation with:
  ```typescript
  queryFn: async (): Promise<AccountOption[]> => {
    const apiClient = getAuthenticatedApiClient();
    const accounts = await apiClient.bankStatements_GetAccounts();
    return accounts.map((a: BankAccountDto) => ({
      value: a.name ?? '',
      label: `${a.name ?? ''} (${a.provider ?? ''})`,
      accountNumber: a.accountNumber ?? '',
      provider: a.provider ?? '',
      currency: a.currency ?? '',
    }));
  },
  ```

- [ ] Step 7: Verify the file compiles without errors. Run:
  ```
  cd frontend && npx tsc --noEmit
  ```
  Fix any type errors before proceeding. Common issues to watch for:
  - `BankStatementImportDto` fields in the generated class are `Date` (not `string`) for `statementDate`/`importDate` — consumers in `ImportTab.tsx` receive these via the hook's return value, not via the deleted interface, so no breakage expected.
  - `GetBankStatementListResponse.totalCount` is `number | undefined` in the generated class vs `number` in the deleted interface — if `ImportTab.tsx` uses `.totalCount` without `!`, TypeScript may flag it. Check and add `?? 0` if needed. Do not change the generated file.

---

