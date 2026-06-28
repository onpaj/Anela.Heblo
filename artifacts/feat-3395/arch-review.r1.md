# Architecture Review: Bank Frontend Hook — Remove Manual DTO Definitions and Raw Fetch Calls

## Skip Design: true

## Architectural Fit Assessment

This is a pure internal-quality refactor. No new capabilities are added and no API contracts change. The work removes a local violation of the established pattern: all hooks in this codebase must use the typed generated client (`getAuthenticatedApiClient()`) rather than reaching through `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch`. The violation is self-contained to `frontend/src/api/hooks/useBankStatements.ts`; no backend changes, no new files, no data migrations.

The integration points are:

- **Generated client** — `frontend/src/api/generated/api-client.ts`: all four bank-statement typed methods already exist and are correct.
- **Auth infrastructure** — `frontend/src/api/client.ts`: `getAuthenticatedApiClient()` returns the `ApiClient` instance with auth headers wired up. Its public surface is the only entry point hooks should use.
- **Consumer component** — `frontend/src/components/customer/tabs/ImportTab.tsx`: calls `useBankStatementsList`, `useBankStatementImport`, and `useBankStatementAccounts`. It references `statement.statementDate` and `statement.importDate` through the `formatDate`/`formatDateTime` utilities, which already accept `string | Date | null | undefined`, so the `string → Date` type promotion is safe.
- **Existing tests** — `frontend/src/api/hooks/__tests__/useBankStatements.test.ts`: covers `useBankStatementAccounts`. The mock plumbing (`createMockApiClient`, `mockAuthenticatedApiClient`) mocks `(apiClient as any).http.fetch` directly. After the refactor, tests for the three migrated hooks must be rewritten to mock the typed client methods instead.

Architectural fit is complete. The change moves the file from non-conforming to conforming with zero impact on the established module boundaries.

## Proposed Architecture

### Component Overview

```
ImportTab.tsx
  └── useBankStatementsList(request)           → apiClient.bankStatements_GetBankStatements(…)
  └── useBankStatementImport()                  → apiClient.bankStatements_ImportStatements(dto)
  └── useBankStatementAccounts()               → apiClient.bankStatements_GetAccounts()

useBankStatementImportStatistics(request)       → apiClient.analytics_GetBankStatementImportStatistics(…)
  (already correct — no change)

getAuthenticatedApiClient()   ←  all hooks
  └── ApiClient (NSwag-generated, typed)
        ├── bankStatements_GetBankStatements(…) : Promise<GetBankStatementListResponse>
        ├── bankStatements_ImportStatements(dto) : Promise<BankStatementImportResultDto>
        ├── bankStatements_GetAccounts()         : Promise<BankAccountDto[]>
        └── bankStatements_GetBankStatement(id)  : Promise<BankStatementImportDto>
```

### Key Design Decisions

#### Decision 1: Use the generated typed method directly for all three hooks

**Options considered:**
- (A) Keep the raw-fetch pattern but replace `(apiClient as any)` with `getApiBaseUrl()` + `getAuthenticatedFetch()`.
- (B) Replace with the typed generated methods (`apiClient.bankStatements_*`).

**Chosen approach:** Option B — typed generated methods.

**Rationale:** Option A is the documented escape hatch for endpoints whose business outcomes cannot yet be expressed through the generated client (e.g. a 412 Precondition Failed before the controller annotation exists). No such special case applies here: all four endpoints are straightforwardly typed, return 200 on success, and the generated methods are verified present and correct. Option A would perpetuate raw fetch plumbing unnecessarily. The `api-client-generation.md` documentation explicitly names Option A an "escape hatch" and instructs developers to migrate back to the typed client as soon as the typed branch exists.

#### Decision 2: Replace the hand-written request interface with a local caller-side type; do not export the generated request class

**Options considered:**
- (A) Export `GetBankStatementListRequest` from the hook file, keeping the hook's public contract unchanged for callers.
- (B) Delete it and have callers provide arguments inline.
- (C) Keep the local interface but align its field types with what `bankStatements_GetBankStatements` expects.

**Chosen approach:** Option A with Option C applied — keep the exported `GetBankStatementListRequest` interface intact as the hook's public parameter shape, because `ImportTab.tsx` constructs it by name and changing the public shape would cascade into the component. Internally, destructure its fields and pass them positionally to the generated method.

**Rationale:** The generated `bankStatements_GetBankStatements` takes twelve positional parameters, not an object. The hook's job is to adapt the ergonomic object API that callers use into the generated method's signature. This adapter responsibility is correct and should stay. The interface itself is not a duplicate: it is the hook's input contract, not a DTO.

#### Decision 3: `BankImportRequest` stays as a local string-date interface; convert to `Date` inside the mutation

**Options considered:**
- (A) Change `BankImportRequest` to accept `Date` fields, matching `BankImportRequestDto`.
- (B) Keep `dateFrom` and `dateTo` as `string` in the hook's input interface and convert internally.

**Chosen approach:** Option B.

**Rationale:** `ImportTab.tsx` sets `dateFrom` and `dateTo` from an `<input type="date">` element, which produces `YYYY-MM-DD` strings. Converting at the call site would push date-parsing concerns into the component. The hook should accept what the component naturally produces and convert to `Date` before passing to `bankStatements_ImportStatements`. Use `new BankImportRequestDto({ accountName, dateFrom: new Date(request.dateFrom), dateTo: new Date(request.dateTo) })` so the DTO's `toJSON` serialises the dates correctly via `toISOString()`.

#### Decision 4: `useBankStatementAccounts` keeps the `AccountOption` mapping layer

**Options considered:**
- (A) Return `BankAccountDto[]` directly.
- (B) Keep the `BankAccountDto[] → AccountOption[]` transformation in the hook.

**Chosen approach:** Option B — transformation stays.

**Rationale:** `AccountOption` is a view-model type (value/label/accountNumber/provider/currency) that the component selects on. It is not a duplication of `BankAccountDto`; it is a projection. Removing it would push display-formatting concerns into `ImportTab.tsx`. The `BankAccountDto` private interface inside the hook can be deleted and replaced with the generated `BankAccountDto` class (or its `IBankAccountDto` interface) from `api-client.ts`.

## Implementation Guidance

### Directory / Module Structure

Only one file changes:

```
frontend/src/api/hooks/useBankStatements.ts   ← all changes here
```

No new files. No directory changes.

### Interfaces and Contracts

**Delete these from `useBankStatements.ts`:**

```typescript
// DELETE
interface BankStatementImportDto { … }         // duplicates generated class
interface GetBankStatementListResponse { … }   // duplicates generated class
interface BankStatementImportResult { … }      // unused alias
interface BankImportResponse { … }             // unused alias
interface BankAccountDto { … }                 // duplicates generated interface
```

**Keep these (they are hook contracts, not DTO duplicates):**

```typescript
export interface GetBankStatementListRequest { … }   // adapter input shape — keep
export interface BankImportRequest { … }              // caller convenience — keep, string dates
export interface AccountOption { … }                  // view-model — keep
export interface GetBankStatementImportStatisticsRequest { … }  // already correct — keep
```

**Add these imports:**

```typescript
import {
  GetBankStatementListResponse,
  BankStatementImportDto,
  BankStatementImportResultDto,
  BankAccountDto,
  IBankAccountDto,
  BankImportRequestDto,
} from '../generated/api-client';
```

**Re-export the generated types that consumers reference:**

```typescript
export type { GetBankStatementListResponse, BankStatementImportDto, BankStatementImportResultDto };
```

### Data Flow

**`useBankStatementsList`**

```
Component calls hook with GetBankStatementListRequest object
  → hook destructures fields
  → calls apiClient.bankStatements_GetBankStatements(
       request.id ?? null,
       request.transferId?.trim() ?? null,
       request.account?.trim() ?? null,
       request.statementDate ?? null,
       request.importDate ?? null,
       request.dateFrom ?? null,
       request.dateTo ?? null,
       request.errorsOnly ?? null,
       request.skip,
       request.take,
       request.orderBy ?? null,
       request.ascending
     )
  → returns Promise<GetBankStatementListResponse>  (generated class, Date fields properly parsed)
  → TanStack Query caches result under [...QUERY_KEYS.bankStatements, 'list', request]
```

**`useBankStatementImport`**

```
Component calls mutateAsync({ accountName, dateFrom: 'YYYY-MM-DD', dateTo: 'YYYY-MM-DD' })
  → hook creates: new BankImportRequestDto({
       accountName: request.accountName,
       dateFrom: new Date(request.dateFrom),
       dateTo: new Date(request.dateTo),
     })
  → calls apiClient.bankStatements_ImportStatements(dto)
  → returns Promise<BankStatementImportResultDto>
```

Note: The mutation currently returns `BankImportResponse` (a local alias for `{ statements: BankStatementImportDto[] }`). After the refactor it returns `BankStatementImportResultDto`, which has the same shape. `ImportTab.tsx` does not inspect the return value of `importMutation.mutateAsync` — it just awaits success — so this change is safe without touching the component.

**`useBankStatementAccounts`**

```
hook calls apiClient.bankStatements_GetAccounts()
  → returns Promise<BankAccountDto[]>
  → hook maps: (a: BankAccountDto) => ({ value: a.name, label: `${a.name} (${a.provider})`, … })
  → TanStack Query caches Promise<AccountOption[]>
```

### Tests

The existing test file mocks `(apiClient as any).http.fetch`. After the refactor, the three affected hooks call typed methods on the client. The mock in `testUtils.ts` (`createMockApiClient`) returns an object with `{ baseUrl, http: { fetch: mockFetch } }`, which means existing tests for the raw-fetch hooks will silently stop exercising the right code path.

Required test updates in `frontend/src/api/hooks/__tests__/useBankStatements.test.ts`:

1. For `useBankStatementAccounts`: the current tests call through `(apiClient as any).http.fetch`. After the refactor the hook calls `apiClient.bankStatements_GetAccounts()`. The mock must expose this method. Add `bankStatements_GetAccounts: jest.fn()` to `createMockApiClient` (or inline in the test), and have it return the test data directly (no raw-fetch response wrapper). The URL-assertion test (`expect(mockFetch).toHaveBeenCalledWith(…'/api/bank-statements/accounts'…)`) will no longer apply and should be removed.

2. Add tests for `useBankStatementsList` and `useBankStatementImport` using the same typed-method mock pattern.

The `testUtils.ts` `createMockApiClient` helper currently only models the private-field surface. It should be extended to support typed method mocks, or each test file that switches to typed methods should define its own mock inline. Either approach is acceptable; do not change the helper in a way that breaks the `useBankStatementImportStatistics` tests, which already use the typed client path.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `BankStatementImportDto.statementDate` and `importDate` change from `string` to `Date` — components that render them using string operations would break | Medium | `formatDate`/`formatDateTime` in `formatters.ts` already accept `string \| Date \| null \| undefined`. `ImportTab.tsx` routes through these formatters. Verify no component accesses `.statementDate` or `.importDate` as a raw string (e.g. substring, comparison). Grep confirms only `ImportTab.tsx` and no direct string operations. |
| `bankStatements_GetBankStatements` takes positional nulls for optional params; passing `undefined` where the generated signature expects `null` could cause query string corruption | Low | Read the generated method: it uses `if (x !== undefined && x !== null)` guards for nullable params, and throws on `null` for non-nullable ones (`skip`, `take`, `ascending`). Pass `undefined` (not `null`) for absent optional params, and pass the caller's value directly for `skip`/`take`/`ascending` (they default to `undefined` in the request interface, which the generated method handles). |
| `useBankStatementAccounts` test asserting the URL path (`/api/bank-statements/accounts`) will fail after the refactor | Low | Delete that assertion; replace with a test that asserts `bankStatements_GetAccounts` was called once with no arguments. |
| `BankImportRequestDto.dateFrom`/`dateTo` require `Date`, but `ImportTab.tsx` passes strings — the `new Date('YYYY-MM-DD')` conversion in the hook is locale-sensitive (midnight UTC vs. local midnight) | Low | `YYYY-MM-DD` strings parsed with `new Date('YYYY-MM-DD')` are treated as UTC midnight by the ECMAScript spec. The backend receives the ISO string from `toISOString()`, also UTC. Consistent and correct. |
| The mutation's return type changes from `BankImportResponse` to `BankStatementImportResultDto` | Low | `ImportTab.tsx` does not use the return value. The change is safe. |

## Specification Amendments

The spec is accurate. Two additions:

1. **`GetBankStatementListRequest` is not a duplicate and must not be deleted.** The spec lists it among interfaces to remove. This is incorrect. The interface is the hook's input contract (an ergonomic object adapter over twelve positional parameters). It should be retained and kept in the hook file. Only the four interfaces that directly mirror generated DTO shapes should be deleted.

2. **Test file must be updated as part of this task.** The spec does not mention tests. The existing tests mock the private-field path that will be removed. Leaving them in place after the refactor would produce tests that pass vacuously (the mock intercepts calls that are no longer made). Updating or replacing the test assertions is a required deliverable, not optional.

## Prerequisites

None. All four generated methods exist in the current `api-client.ts`. No backend changes, no migrations, no infrastructure changes are required.
