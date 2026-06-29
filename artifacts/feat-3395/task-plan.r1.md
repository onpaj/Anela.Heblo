# Bank Frontend Hook — Remove Manual DTOs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Replace manually-defined TypeScript interfaces and `as any` raw-fetch calls in `useBankStatements.ts` with generated client types and typed methods.

**Architecture:** Pure frontend refactor — no backend changes. Only `frontend/src/api/hooks/useBankStatements.ts` and its test file change. All required generated types and methods already exist in `api-client.ts`.

**Tech Stack:** TypeScript, React Query, NSwag-generated client

---

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

### task: update-tests-for-typed-client-methods

**Files:**
- Modify: `frontend/src/api/hooks/__tests__/useBankStatements.test.ts`

**Context:**

The current test file mocks the private `http.fetch` field via `createMockApiClient()` (which creates `{ baseUrl, http: { fetch: mockFetch } }`). After the hook is rewritten to use typed methods, the test must mock those methods on the real `ApiClient` instance returned by `getAuthenticatedApiClient`.

The `createMockApiClient` / `mockAuthenticatedApiClient` helpers in `testUtils.ts` set up `(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient)`. We keep using this pattern but replace the mock client structure with one that exposes the typed method names.

- [ ] Step 1: Replace the `beforeEach` setup in `useBankStatementAccounts` describe block. Remove the `mockFetch` variable and the raw mock client. Instead spy on the real typed method. Rewrite the `beforeEach` as:
  ```typescript
  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = {
      bankStatements_GetAccounts: jest.fn(),
      bankStatements_GetBankStatements: jest.fn(),
      bankStatements_ImportStatements: jest.fn(),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);
  });
  ```
  Remove the `let mockFetch: jest.Mock;` variable declaration since it is no longer needed.

- [ ] Step 2: Update each test that called `mockFetch.mockResolvedValue(...)` to call the appropriate method mock instead.

  For `useBankStatementAccounts` tests, replace every:
  ```typescript
  mockFetch.mockResolvedValue({
    ok: true,
    json: () => Promise.resolve([...accountData...])
  });
  ```
  with:
  ```typescript
  mockClient.bankStatements_GetAccounts.mockResolvedValue([...accountData...]);
  ```

  The raw `BankAccountDto`-shaped objects remain the same (the generated method already returns parsed objects). Example for the first test:
  ```typescript
  mockClient.bankStatements_GetAccounts.mockResolvedValue([
    { name: 'ComgateCZK', accountNumber: '2301495165/2010', provider: 'Comgate', currency: 'CZK' }
  ]);
  ```

- [ ] Step 3: Remove or rewrite the endpoint URL assertion test. The test `'should call /api/bank-statements/accounts endpoint'` currently asserts `mockFetch` was called with a URL containing `/api/bank-statements/accounts`. After the refactor the hook calls a typed method, not a URL — so the URL is no longer observable at this layer. Replace that test with a method-call assertion:
  ```typescript
  it('should call bankStatements_GetAccounts on the api client', async () => {
    mockClient.bankStatements_GetAccounts.mockResolvedValue([]);

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useBankStatementAccounts(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.bankStatements_GetAccounts).toHaveBeenCalledTimes(1);
    expect(mockClient.bankStatements_GetAccounts).toHaveBeenCalledWith();
  });
  ```

- [ ] Step 4: Remove the `createMockApiClient` and `mockAuthenticatedApiClient` imports from `testUtils` if they are no longer used by this test file. Check the import line:
  ```typescript
  import { createMockApiClient, mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';
  ```
  After the rewrite, `createMockApiClient` is replaced by an inline object literal, but `mockAuthenticatedApiClient` is still used (it sets `(getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient)`). Keep `mockAuthenticatedApiClient` and `createQueryClientWrapper`. Remove `createMockApiClient` from the import.

  Call `mockAuthenticatedApiClient(mockClient)` in `beforeEach` after constructing the inline mock object:
  ```typescript
  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = {
      bankStatements_GetAccounts: jest.fn(),
      bankStatements_GetBankStatements: jest.fn(),
      bankStatements_ImportStatements: jest.fn(),
    };
    mockAuthenticatedApiClient(mockClient);
  });
  ```

- [ ] Step 5: Run the tests to confirm they pass:
  ```
  cd frontend && npx jest src/api/hooks/__tests__/useBankStatements.test.ts --no-coverage
  ```
  Fix any failures. If a test fails because `result.current.isSuccess` is false, check that the mock method is configured in `beforeEach` (not missing from the mock object) and that the mock returns a resolved promise.

---

### task: build-and-lint-verification

**Files:**
- No file changes — verification only

**Context:**

The project rules require `npm run build` and `npm run lint` to pass before a task is declared done. The TypeScript compile check in task 1 step 7 catches type errors early; this task runs the full build to catch any issues missed by `tsc --noEmit` alone (e.g. Vite/Rollup transform errors) and the ESLint pass to catch `as any` that may have been accidentally left behind.

- [ ] Step 1: Run the frontend build:
  ```
  cd /home/user/Anela.Heblo/frontend && npm run build
  ```
  Resolve any errors. Do not modify generated files. If a type narrowing error surfaces from `GetBankStatementListResponse.totalCount` being `number | undefined`, the fix is in `ImportTab.tsx` (add `?? 0`) — not in the generated client.

- [ ] Step 2: Run the linter:
  ```
  cd /home/user/Anela.Heblo/frontend && npm run lint
  ```
  Resolve any `@typescript-eslint/no-explicit-any` violations. If any `as any` patterns remain from the old fetch code, remove them. Do not introduce new `as any` casts.

- [ ] Step 3: Confirm the deleted interfaces are not re-introduced by the build step (OpenAPI client generation runs during build). The generated `api-client.ts` is regenerated from the backend OpenAPI spec — it will re-emit the generated types but will not re-create the hand-written interfaces that were in `useBankStatements.ts`. Verify by checking that `useBankStatements.ts` still imports from `'../generated/api-client'` after the build completes.
