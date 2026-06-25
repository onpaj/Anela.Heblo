# Specification: Migrate Analytics Hooks to Generated API Client

## Summary

Two React Query hooks in the Analytics frontend module — `useInvoiceImportStatistics` and `useBankStatementImportStatistics` — bypass the generated OpenAPI TypeScript client by using raw `(apiClient as any).http.fetch(...)` calls and redefining types already exported by the generated client. This refactor replaces those raw calls with typed generated-client method invocations and removes the duplicate hand-written interfaces, bringing these two hooks into line with the established pattern used by `useProductMarginSummary`.

## Background

The project auto-generates a TypeScript API client from the OpenAPI spec at build time (`frontend/src/api/generated/api-client.ts`). All frontend hooks are expected to consume this client via its typed methods, ensuring that any backend contract change (field rename, type change, added required field) propagates to a TypeScript compile error rather than a silent runtime failure.

Two hooks were written against an older or ad-hoc pattern that pre-dates or ignores the generated client:

- `frontend/src/api/hooks/useInvoiceImportStatistics.ts` — defines `DailyInvoiceCount` and `InvoiceImportStatisticsResponse` manually, fetches via raw HTTP.
- `frontend/src/api/hooks/useBankStatements.ts` — defines `BankStatementImportStatisticsDto` and `GetBankStatementImportStatisticsResponse` manually for `useBankStatementImportStatistics`; `useBankStatementsList`, `useBankStatementImport`, and `useBankStatementAccounts` also use raw fetch but are out of scope for this change (see Out of Scope).

The `(apiClient as any)` cast additionally bypasses any auth header injection, retry logic, or middleware the generated HTTP client applies, creating a potential silent auth regression.

## Functional Requirements

### FR-1: Replace raw fetch in `useInvoiceImportStatistics`

The `queryFn` inside `useInvoiceImportStatistics` must be rewritten to call `apiClient.analytics_GetInvoiceImportStatistics(dateType, daysBack)` from the generated client.

**Acceptance criteria:**
- `queryFn` calls `apiClient.analytics_GetInvoiceImportStatistics(dateType as ImportDateType, daysBack ?? null)`.
- The return type of `queryFn` is `Promise<GetInvoiceImportStatisticsResponse>` where `GetInvoiceImportStatisticsResponse` is imported from `../generated/api-client`.
- No `(apiClient as any)` cast remains in the file.
- No manual `URLSearchParams` construction remains in the file.
- `staleTime` and `gcTime` values are unchanged (5 min / 10 min).

### FR-2: Remove duplicate hand-written interfaces from `useInvoiceImportStatistics.ts`

The locally-defined interfaces `DailyInvoiceCount` and `InvoiceImportStatisticsResponse` must be deleted from `useInvoiceImportStatistics.ts`.

**Acceptance criteria:**
- `useInvoiceImportStatistics.ts` no longer exports or defines `DailyInvoiceCount` or `InvoiceImportStatisticsResponse`.
- `DailyInvoiceCount` and `GetInvoiceImportStatisticsResponse` (the generated equivalents) are re-exported from the hook file so that existing chart consumers (`InvoiceImportChart.tsx`) do not need import-path changes.
- `ImportDateType` is also re-exported for callers that previously imported the string-literal union `'InvoiceDate' | 'LastSyncTime'` from this file (if any).

### FR-3: Update `InvoiceImportChart.tsx` to use generated `DailyInvoiceCount`

`InvoiceImportChart.tsx` line 15 imports `DailyInvoiceCount` from `../../api/hooks/useInvoiceImportStatistics`. The generated `DailyInvoiceCount` has `date: Date` (not `string`). The chart currently calls `parseISO(item.date)` which expects a string. This type incompatibility must be resolved.

**Acceptance criteria:**
- `InvoiceImportChart.tsx` receives `data: DailyInvoiceCount[]` where `DailyInvoiceCount` is the generated class (`date?: Date`).
- All `parseISO(item.date)` calls are replaced with direct `Date` usage (e.g. `item.date` passed directly to `format(...)` and `isWeekend(...)`).
- The `date` field used for the tooltip `parseISO` calls is similarly updated.
- Chart behavior is functionally unchanged (same display output for identical backend data).
- TypeScript compiles without errors.

### FR-4: Replace raw fetch in `useBankStatementImportStatistics`

The `queryFn` inside `useBankStatementImportStatistics` must be rewritten to call `apiClient.analytics_GetBankStatementImportStatistics(startDate, endDate, dateType)` from the generated client.

**Acceptance criteria:**
- `queryFn` calls `apiClient.analytics_GetBankStatementImportStatistics(startDate, endDate, dateType)` where `startDate` and `endDate` are `Date | null` and `dateType` is `BankStatementDateType | undefined`.
- `request.startDate` and `request.endDate` (ISO strings) are converted to `new Date(...)` or `null` before passing.
- `request.dateType` is cast to `BankStatementDateType` when present, or `undefined` when absent.
- The return type of `queryFn` is `Promise<GetBankStatementImportStatisticsResponse>` from the generated client.
- No `(apiClient as any)` cast remains for this hook's `queryFn`.
- `staleTime` (5 min) is unchanged.

### FR-5: Remove duplicate hand-written interfaces from `useBankStatements.ts` (statistics-related only)

The locally-defined `BankStatementImportStatisticsDto` and `GetBankStatementImportStatisticsResponse` must be deleted from `useBankStatements.ts`.

**Acceptance criteria:**
- `useBankStatements.ts` no longer exports or defines `BankStatementImportStatisticsDto` or the local `GetBankStatementImportStatisticsResponse`.
- The generated `GetBankStatementImportStatisticsResponse` and `DailyBankStatementStatistics` (the generated equivalent of `BankStatementImportStatisticsDto`) are re-exported from `useBankStatements.ts` to avoid breaking import paths in chart consumers.
- `BankStatementDateType` is re-exported.

### FR-6: Update `BankStatementImportChart.tsx` to use generated `DailyBankStatementStatistics`

`BankStatementImportChart.tsx` line 15 imports `BankStatementImportStatisticsDto` from `../../api/hooks/useBankStatements`. The generated equivalent is `DailyBankStatementStatistics` with `date: Date` (not `string`). The chart calls `parseISO(item.date)` which must be updated.

**Acceptance criteria:**
- `BankStatementImportChart.tsx` receives `data: DailyBankStatementStatistics[]` (or the re-exported type) where `date?: Date`.
- All `parseISO(item.date)` calls are replaced with direct `Date` usage.
- Chart behavior is functionally unchanged.
- TypeScript compiles without errors.

### FR-7: Retain `GetBankStatementImportStatisticsRequest` parameter interface

The `GetBankStatementImportStatisticsRequest` interface (with `startDate?: string`, `endDate?: string`, `dateType?: string`) is the external API surface of `useBankStatementImportStatistics`. It must be retained unchanged as a hook parameter type; the conversion from string dates to `Date` objects happens inside the `queryFn`.

**Acceptance criteria:**
- `GetBankStatementImportStatisticsRequest` remains exported from `useBankStatements.ts` with the same shape.
- Call sites of `useBankStatementImportStatistics` require no changes.

## Non-Functional Requirements

### NFR-1: Type safety

After this change, any backend-driven change to the analytics response shape (field rename, type change, removal of a field) must produce a TypeScript compile error in the affected hook or chart. The `(apiClient as any)` pattern must not remain in either of the two targeted hooks for the statistics queries.

### NFR-2: No behavioral regression

HTTP request URLs, query parameters, cache keys, and `staleTime`/`gcTime` values must remain functionally equivalent. The generated client constructs URLs using `DateType=` (capital D) as the parameter name, matching what the backend expects. Verify that query-key arrays remain the same shape so existing cache invalidation logic is unaffected.

### NFR-3: Build must pass

`npm run build` and `npm run lint` must succeed with zero TypeScript errors and zero lint violations after the change.

## Data Model

No data model changes. The key type mappings between old hand-written interfaces and generated classes are:

**Invoice statistics:**

| Hand-written (hook file) | Generated (api-client.ts) | Notes |
|---|---|---|
| `DailyInvoiceCount.date: string` | `DailyInvoiceCount.date?: Date` | Breaking: chart must stop calling `parseISO()` |
| `DailyInvoiceCount.count: number` | `DailyInvoiceCount.count?: number` | Now optional in generated type |
| `DailyInvoiceCount.isBelowThreshold: boolean` | `DailyInvoiceCount.isBelowThreshold?: boolean` | Now optional |
| `InvoiceImportStatisticsResponse` | `GetInvoiceImportStatisticsResponse extends BaseResponse` | `success`, `errorCode`, `params` come from `BaseResponse`; `errorCode` is `ErrorCodes` enum not `string` |

**Bank statement statistics:**

| Hand-written (hook file) | Generated (api-client.ts) | Notes |
|---|---|---|
| `BankStatementImportStatisticsDto.date: string` | `DailyBankStatementStatistics.date?: Date` | Breaking: chart must stop calling `parseISO()` |
| `BankStatementImportStatisticsDto.importCount: number` | `DailyBankStatementStatistics.importCount?: number` | Now optional |
| `BankStatementImportStatisticsDto.totalItemCount: number` | `DailyBankStatementStatistics.totalItemCount?: number` | Now optional |
| `GetBankStatementImportStatisticsResponse.statistics` | `GetBankStatementImportStatisticsResponse.statistics?: DailyBankStatementStatistics[]` | Field name unchanged |

## API / Interface Design

No API changes. The refactor is purely on the frontend client side.

**`useInvoiceImportStatistics.ts` — revised exports:**
```ts
// Generated types re-exported for consumer convenience
export { GetInvoiceImportStatisticsResponse, DailyInvoiceCount, ImportDateType } from '../generated/api-client';

// Hook parameter interface (unchanged)
export interface UseInvoiceImportStatisticsParams {
  dateType?: ImportDateType;   // was 'InvoiceDate' | 'LastSyncTime' string literal
  daysBack?: number;
}
```

**`useBankStatements.ts` — revised exports for statistics types:**
```ts
export {
  GetBankStatementImportStatisticsResponse,
  DailyBankStatementStatistics,
  BankStatementDateType,
} from '../generated/api-client';
```

The `GetBankStatementImportStatisticsRequest` interface stays in the hook file unchanged.

**Chart component prop type updates (summary):**
- `InvoiceImportChart` props: `data: DailyInvoiceCount[]` — `DailyInvoiceCount` now from generated client, `date` field is `Date`.
- `BankStatementImportChart` props: `data: DailyBankStatementStatistics[]` — replaces `BankStatementImportStatisticsDto`, `date` field is `Date`.

## Dependencies

- `frontend/src/api/generated/api-client.ts` — provides the typed methods and classes. Must not be modified.
- `frontend/src/api/client.ts` — provides `getAuthenticatedApiClient()` and `QUERY_KEYS`. No changes required.
- `frontend/src/components/charts/InvoiceImportChart.tsx` — must be updated to handle `date: Date`.
- `frontend/src/components/charts/BankStatementImportChart.tsx` — must be updated to handle `date: Date`.
- No new npm packages required.

## Out of Scope

The following hooks in `useBankStatements.ts` also use `(apiClient as any).http.fetch(...)` but are **not** covered by this change:
- `useBankStatementsList` — raw fetch against `/api/bank-statements`
- `useBankStatementImport` — raw POST to `/api/bank-statements/import`
- `useBankStatementAccounts` — raw fetch against `/api/bank-statements/accounts`

These share the same anti-pattern but do not have corresponding generated client methods at this time. They should be addressed in a follow-up once generated methods exist or when the generated client is extended.

Changes to backend code, OpenAPI spec, or database are out of scope.

E2E test changes are out of scope unless a test directly depends on the hook's exported interfaces (there are no known E2E tests for analytics chart hooks).

## Open Questions

None.

## Status: COMPLETE
