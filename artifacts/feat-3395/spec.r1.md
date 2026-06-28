# Specification: Bank Frontend Hook — Remove Manual DTO Definitions and Raw Fetch Calls

## Summary

Three React query hooks in `frontend/src/api/hooks/useBankStatements.ts` manually duplicate TypeScript interfaces that are already present in the generated client (`api-client.ts`) and bypass the typed client methods by reaching into private fields via `as any` casts. The fix is to delete the hand-written interfaces and `as any` plumbing and replace them with the generated typed methods, which already exist in the current client.

## Background

`BankStatementsController` exposes four endpoints: `GET /api/bank-statements`, `POST /api/bank-statements/import`, `GET /api/bank-statements/accounts`, and `GET /api/bank-statements/{id}`. All four appear in the OpenAPI spec and have been generated into `frontend/src/api/generated/api-client.ts` as:

- `bankStatements_GetBankStatements(...)` → returns `GetBankStatementListResponse`
- `bankStatements_ImportStatements(request: BankImportRequestDto)` → returns `BankStatementImportResultDto`
- `bankStatements_GetAccounts()` → returns `BankAccountDto[]`
- `bankStatements_GetBankStatement(id)` → returns `BankStatementImportDto`

Despite this, `useBankStatements.ts` re-declares four interfaces (`BankStatementImportDto`, `GetBankStatementListResponse`, `GetBankStatementListRequest`, `BankAccountDto`) and calls the backend via `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch(...)`. This creates two distinct risks:

1. **Silent type drift**: date fields are typed as `string` in the manual interfaces (`statementDate: string`, `importDate: string`, `dateFrom: string`, `dateTo: string`) but the generated `BankStatementImportDto` types them as `Date` and `BankImportRequestDto` types `dateFrom`/`dateTo` as `Date`. A change to the backend shape (new field, renamed field) will not produce a TypeScript error under the current arrangement.
2. **Private-API fragility**: `(apiClient as any).http` and `(apiClient as any).baseUrl` are NSwag internal fields. NSwag can rename or remove them at any regeneration without warning.

The analytics hook in the same file (`useBankStatementImportStatistics`, line 52) already demonstrates the correct pattern using `getAuthenticatedApiClient()` and a typed method call. This task standardises the remaining three hooks to match.

## Functional Requirements

### FR-1: Replace manual interface `BankStatementImportDto` with generated type

Delete the locally declared `export interface BankStatementImportDto` (lines 13–23 of `useBankStatements.ts`) and import `BankStatementImportDto` from `../generated/api-client` instead.

**Acceptance criteria:**
- No local `interface BankStatementImportDto` exists in `useBankStatements.ts`.
- `BankStatementImportDto` is imported from `../generated/api-client`.
- `statementDate` and `importDate` fields are typed as `Date` (matching the generated class), not `string`.
- TypeScript build (`npm run build`) passes with no type errors.

### FR-2: Replace manual interface `GetBankStatementListResponse` with generated type

Delete the locally declared `export interface GetBankStatementListResponse` (lines 25–28) and import `GetBankStatementListResponse` from `../generated/api-client`.

**Acceptance criteria:**
- No local `interface GetBankStatementListResponse` exists in `useBankStatements.ts`.
- The imported `GetBankStatementListResponse` is the generated class (which extends `BaseResponse` and has `items?: BankStatementImportDto[]` and `totalCount?: number`).
- TypeScript build passes with no type errors.

### FR-3: Replace manual interface `GetBankStatementListRequest` with generated parameters

Delete the locally declared `export interface GetBankStatementListRequest` (lines 30–43). The generated method `bankStatements_GetBankStatements` accepts individual query parameters rather than a request object, so the local `GetBankStatementListRequest` interface is used only to collect those parameters in the hook's argument. The interface can be kept as a local (non-exported) argument bag if it aids readability, but it must not duplicate or conflict with the generated types. Alternatively, keep it as a local hook-argument type that is clearly scoped to the hook (not re-exported as if it were a backend contract).

**Acceptance criteria:**
- No `export interface GetBankStatementListRequest` declaration remains in `useBankStatements.ts` that purports to mirror the backend contract.
- If an argument-bag type is retained for the hook's parameter, it is either: (a) kept local (not exported), or (b) clearly documented as a frontend-only shim, not re-exported alongside the generated contract types.
- TypeScript build passes with no type errors.

### FR-4: Replace raw `fetch` in `useBankStatementsList` with generated typed method

Rewrite the `queryFn` of `useBankStatementsList` to call `apiClient.bankStatements_GetBankStatements(...)` with individual arguments, dropping the manual `URLSearchParams` construction and all `(apiClient as any)` casts.

**Acceptance criteria:**
- `useBankStatementsList` calls `apiClient.bankStatements_GetBankStatements(id, transferId, account, statementDate, importDate, dateFrom, dateTo, errorsOnly, skip, take, orderBy, ascending)` using the generated method signature.
- No `(apiClient as any)` reference remains in this hook.
- No `URLSearchParams` manual construction remains in this hook.
- The return type resolves to the generated `GetBankStatementListResponse` without a manual cast.
- TypeScript build passes with no type errors.

### FR-5: Replace raw `fetch` in `useBankStatementImport` with generated typed method

Rewrite the `mutationFn` of `useBankStatementImport` to call `apiClient.bankStatements_ImportStatements(request)` where `request` is a `BankImportRequestDto` instance from the generated client.

**Acceptance criteria:**
- `useBankStatementImport` calls `apiClient.bankStatements_ImportStatements(...)` using the generated method.
- The `BankImportRequest` argument bag (lines 130–134) used by callers may remain as a frontend-only shim but must map `dateFrom`/`dateTo` strings to `Date` objects before constructing the `BankImportRequestDto` (the generated DTO requires `Date`, not `string`).
- `BankImportResponse` (lines 140–142) may be removed if callers can consume `BankStatementImportResultDto` directly; otherwise it can remain as a local alias clearly distinct from the generated type.
- No `(apiClient as any)` reference remains in this hook.
- TypeScript build passes with no type errors.

### FR-6: Replace raw `fetch` in `useBankStatementAccounts` with generated typed method

Rewrite the `queryFn` of `useBankStatementAccounts` to call `apiClient.bankStatements_GetAccounts()` and map the returned `BankAccountDto[]` to `AccountOption[]`, using the generated `BankAccountDto` type.

**Acceptance criteria:**
- `useBankStatementAccounts` calls `apiClient.bankStatements_GetAccounts()`.
- The private `interface BankAccountDto` (line 179) is removed; its role is fulfilled by the generated `BankAccountDto` imported from `../generated/api-client`.
- No `(apiClient as any)` reference remains in this hook.
- TypeScript build passes with no type errors.

### FR-7: Retain re-exported generated types at the module boundary

The hook file currently re-exports `GetBankStatementImportStatisticsResponse`, `DailyBankStatementStatistics`, and `BankStatementDateType` from the generated client (line 5) so callers need only import from the hooks module. Any generated types that are now consumed in place of the deleted manual interfaces should be re-exported from `useBankStatements.ts` on the same `export type { ... }` line if they are consumed by UI components via the hooks module.

**Acceptance criteria:**
- Any generated type that was previously accessible to UI components via the manual interface export continues to be accessible — either because the generated class is re-exported, or because the UI components are updated to import directly from `api-client.ts`.
- No UI component that currently imports from `useBankStatements.ts` has a broken import after the change.
- TypeScript build passes with no type errors.

## Non-Functional Requirements

### NFR-1: No behaviour change

This is a pure type-layer and HTTP-layer refactor. The HTTP calls made at runtime must remain semantically identical to what they are today: same URLs, same method verbs, same query parameter names, same request bodies, same response shapes consumed.

**Acceptance criteria:**
- The bank statements list page, import flow, and account selector render correctly against staging after the change.
- No network requests differ in URL, verb, or body structure (verify via browser DevTools or E2E test).

### NFR-2: Build cleanliness

**Acceptance criteria:**
- `npm run build` passes with zero TypeScript errors and zero ESLint errors.
- `dotnet build` is not affected (backend is untouched).

### NFR-3: No new `as any` or private-field access

**Acceptance criteria:**
- A grep for `(apiClient as any)` in `useBankStatements.ts` returns zero matches after the change.
- No new `as any` casts are introduced anywhere in the file.

## Data Model

No backend data model changes. The relevant types are already in the generated client:

| Generated type | Location in `api-client.ts` | Replaces manual declaration |
|---|---|---|
| `BankStatementImportDto` | class at ~line 16421 | local `interface BankStatementImportDto` (line 13) |
| `GetBankStatementListResponse` | class at ~line 16533 | local `interface GetBankStatementListResponse` (line 25) |
| `BankImportRequestDto` | class at ~line 16489 | local `interface BankImportRequest` (line 130) |
| `BankStatementImportResultDto` | class at ~line 16377 | local `interface BankImportResponse` (line 140) |
| `BankAccountDto` | class at ~line 16329 | local `interface BankAccountDto` (line 179) |

Notable type difference to handle during migration: the manual `BankImportRequest` interface declares `dateFrom: string` and `dateTo: string`, but the generated `BankImportRequestDto` declares them as `Date`. The hook's mutation function must convert the caller-supplied ISO date strings to `Date` objects before constructing the `BankImportRequestDto` instance (e.g. `new Date(request.dateFrom)`).

Similarly, the manual `BankStatementImportDto` declares `statementDate: string` and `importDate: string`, while the generated class types them as `Date`. Any UI component that formats these fields must already handle this or will need a trivial `date.toISOString()` / date-formatting adjustment.

## API / Interface Design

No API changes. The three hook signatures visible to UI components remain unchanged:

```typescript
useBankStatementsList(request?: GetBankStatementListRequest): UseQueryResult<GetBankStatementListResponse>
useBankStatementImport(): UseMutationResult<BankImportResponse, Error, BankImportRequest>
useBankStatementAccounts(): UseQueryResult<AccountOption[]>
```

Internally, these now delegate to:
- `apiClient.bankStatements_GetBankStatements(id, transferId, account, statementDate, importDate, dateFrom, dateTo, errorsOnly, skip, take, orderBy, ascending)`
- `apiClient.bankStatements_ImportStatements(new BankImportRequestDto({ accountName, dateFrom: new Date(dateFrom), dateTo: new Date(dateTo) }))`
- `apiClient.bankStatements_GetAccounts()`

The `getAuthenticatedApiClient()` helper from `../client` is used for all three calls, matching the analytics hook pattern already in the same file.

## Dependencies

- `frontend/src/api/generated/api-client.ts` — must be up to date; confirm `bankStatements_GetBankStatements`, `bankStatements_ImportStatements`, and `bankStatements_GetAccounts` are present (they are, as of the current generated file).
- `frontend/src/api/client.ts` — `getAuthenticatedApiClient()` helper, already imported in the file.
- No new npm packages required.
- No backend changes required.

## Out of Scope

- Adding the `GET /api/bank-statements/{id}` endpoint hook (`useBankStatementById`) — not currently present and not requested.
- Modifying any other hook file.
- Backend controller or DTO changes.
- Adding `[ProducesResponseType]` annotations for business-outcome status codes (e.g. 409) — none of these three endpoints use non-2xx business outcomes.
- Changing the `AccountOption` mapping shape or the `useBankStatementImportStatistics` hook.
- Renaming or restructuring the hook module.

## Open Questions

None.

## Status: COMPLETE
