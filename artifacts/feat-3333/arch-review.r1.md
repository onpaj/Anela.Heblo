# Architecture Review: Migrate Analytics Hooks to Generated API Client

## Skip Design: true

## Architectural Fit Assessment

This is a pure refactor with no behavioral change. Both hooks (`useInvoiceImportStatistics` and `useBankStatementImportStatistics`) already exist and are wired to the UI. The generated client already exposes the two target methods — `analytics_GetInvoiceImportStatistics` and `analytics_GetBankStatementImportStatistics` — with full type coverage. The established pattern is demonstrated by `useProductMarginSummary`, which imports generated types directly and calls a typed client method with no raw fetch.

The integration points are narrow and well-understood:
- Two hook files modified in-place
- Two chart components updated for the `date: Date` type change (was `string`)
- No backend changes, no new files, no new query keys, no new cache configuration

The refactor is entirely safe to execute in a single pass.

## Proposed Architecture

### Component Overview

```
frontend/src/api/generated/api-client.ts   (read-only — generated)
  exports: GetInvoiceImportStatisticsResponse, DailyInvoiceCount, ImportDateType
  exports: GetBankStatementImportStatisticsResponse, DailyBankStatementStatistics, BankStatementDateType
  methods: analytics_GetInvoiceImportStatistics(dateType, daysBack)
           analytics_GetBankStatementImportStatistics(startDate, endDate, dateType)

frontend/src/api/client.ts                 (read-only for this feature)
  getAuthenticatedApiClient() → ApiClient  (synchronous, no await needed)

frontend/src/api/hooks/useInvoiceImportStatistics.ts   [MODIFIED]
  - removes: DailyInvoiceCount, InvoiceImportStatisticsResponse
  - re-exports: DailyInvoiceCount, GetInvoiceImportStatisticsResponse, ImportDateType
  - queryFn calls: apiClient.analytics_GetInvoiceImportStatistics(...)

frontend/src/api/hooks/useBankStatements.ts            [MODIFIED — scoped section only]
  - removes: BankStatementImportStatisticsDto, GetBankStatementImportStatisticsResponse
  - re-exports: DailyBankStatementStatistics, GetBankStatementImportStatisticsResponse, BankStatementDateType
  - queryFn calls: apiClient.analytics_GetBankStatementImportStatistics(...)
  - retains unchanged: useBankStatementsList, useBankStatementImport, useBankStatementAccounts

frontend/src/components/charts/InvoiceImportChart.tsx  [MODIFIED]
  - prop type: DailyInvoiceCount[] (same name, now re-exported from hook — no import path change)
  - date handling: item.date is now Date — replace parseISO(item.date) with item.date directly

frontend/src/components/charts/BankStatementImportChart.tsx  [MODIFIED]
  - prop type: DailyBankStatementStatistics[] (name changes from BankStatementImportStatisticsDto)
  - date handling: item.date is now Date — replace parseISO(item.date) with item.date directly
```

### Key Design Decisions

#### Decision 1: Re-export generated types from the hook files rather than updating all import sites

**Options considered:**
- Option A: Re-export generated types from the hook files so chart components keep their existing import paths unchanged.
- Option B: Update chart component imports to point directly at `api/generated/api-client.ts`.

**Chosen approach:** Option A — re-export from the hook files.

**Rationale:** The charts import `DailyInvoiceCount` from `useInvoiceImportStatistics` and `BankStatementImportStatisticsDto` from `useBankStatements`. Option A is a single-line re-export that eliminates the change surface. Option B would touch a file purely to update an import path, adding noise without benefit. This matches the pattern already established in `useProductMarginSummary`, which re-exports `GetProductMarginSummaryResponse`, `ProductGroupingMode`, and `MarginLevel` for consumer convenience.

#### Decision 2: Type alias for renamed DTO in BankStatementImportChart

**Options considered:**
- Option A: Rename `BankStatementImportStatisticsDto` to `DailyBankStatementStatistics` in the chart prop type and update all usages in the component.
- Option B: Re-export `DailyBankStatementStatistics as BankStatementImportStatisticsDto` from `useBankStatements.ts` to keep the chart component's prop unchanged.

**Chosen approach:** Option A — update the chart component's prop type to `DailyBankStatementStatistics`.

**Rationale:** The chart component is one file with a single prop using this type. The hand-written name `BankStatementImportStatisticsDto` was a local approximation; the canonical generated name is `DailyBankStatementStatistics`. Perpetuating the old name via a re-export alias creates confusion about which name is authoritative and would require documenting the alias. Accepting the rename in the chart is a one-line prop change and three field accesses that remain structurally identical (`date`, `importCount`, `totalItemCount` are the same in both).

#### Decision 3: `date` field type change from `string` to `Date`

**Options considered:**
- Option A: Keep `string` in chart internals by calling `.toISOString()` on the generated `Date` at the hook boundary.
- Option B: Accept `Date` objects throughout — charts receive `Date`, replace `parseISO(item.date)` with direct `item.date` usage.

**Chosen approach:** Option B.

**Rationale:** The generated client already deserializes the ISO string to a `Date` object via `new Date(_data["date"].toString())` in both `DailyInvoiceCount.init` and `DailyBankStatementStatistics.init`. Wrapping it back to a string at the hook boundary to preserve `parseISO` calls would be pointless conversion. Using the `Date` directly removes a dependency on `parseISO` in the chart internals.

#### Decision 4: `dateType` parameter casting for `useInvoiceImportStatistics`

**Options considered:**
- Option A: Widen `UseInvoiceImportStatisticsParams.dateType` from literal union `'InvoiceDate' | 'LastSyncTime'` to `ImportDateType`.
- Option B: Keep the string literal union on the params interface and cast to `ImportDateType` at the call site inside `queryFn`.

**Chosen approach:** Option B — cast at call site: `apiClient.analytics_GetInvoiceImportStatistics(dateType as ImportDateType, daysBack ?? null)`.

**Rationale:** `ImportDateType` is an enum whose string values are exactly `"InvoiceDate"` and `"LastSyncTime"` — structurally identical to the existing literal union. Changing the public params interface would touch all callers of `useInvoiceImportStatistics` for zero type-safety gain since the literal union already constrains the same values. The cast is safe because the values are provably equivalent.

#### Decision 5: ISO string to `Date` conversion for `useBankStatementImportStatistics` request params

**Options considered:**
- Option A: Change `GetBankStatementImportStatisticsRequest.startDate/endDate` from `string | undefined` to `Date | null | undefined` to match the generated client signature exactly.
- Option B: Keep `startDate/endDate` as strings in the request interface and convert to `Date` objects inside `queryFn`.

**Chosen approach:** Option B — convert inside `queryFn`: `startDate ? new Date(startDate) : null`.

**Rationale:** `GetBankStatementImportStatisticsRequest` is a public interface used by callers (the bank statement analytics page). Changing its field types from `string` to `Date` would force all callers to construct `Date` objects and would break the existing `GetBankStatementImportStatisticsRequest` interface that FR-7 explicitly requires to remain unchanged.

## Implementation Guidance

### Directory / Module Structure

No new files. Modify in-place:

```
frontend/src/api/hooks/useInvoiceImportStatistics.ts
frontend/src/api/hooks/useBankStatements.ts
frontend/src/components/charts/InvoiceImportChart.tsx
frontend/src/components/charts/BankStatementImportChart.tsx
```

### Interfaces and Contracts

**`useInvoiceImportStatistics.ts` — after change**

```typescript
import {
  GetInvoiceImportStatisticsResponse,
  DailyInvoiceCount,
  ImportDateType,
} from '../generated/api-client';

// Re-export for chart consumer (no import path change needed in InvoiceImportChart)
export { DailyInvoiceCount, GetInvoiceImportStatisticsResponse, ImportDateType };

export interface UseInvoiceImportStatisticsParams {
  dateType?: 'InvoiceDate' | 'LastSyncTime'; // keep as string literal; cast inside queryFn
  daysBack?: number;
}

// queryFn body:
const apiClient = getAuthenticatedApiClient(); // synchronous — no await needed
return apiClient.analytics_GetInvoiceImportStatistics(
  dateType as ImportDateType,
  daysBack ?? null
);
```

**`useBankStatements.ts` — scoped section**

```typescript
import {
  GetBankStatementImportStatisticsResponse,
  DailyBankStatementStatistics,
  BankStatementDateType,
} from '../generated/api-client';

// Re-export for chart consumer
export { DailyBankStatementStatistics, GetBankStatementImportStatisticsResponse, BankStatementDateType };

// GetBankStatementImportStatisticsRequest — UNCHANGED (FR-7)

// queryFn body:
const apiClient = getAuthenticatedApiClient();
return apiClient.analytics_GetBankStatementImportStatistics(
  request.startDate ? new Date(request.startDate) : null,
  request.endDate ? new Date(request.endDate) : null,
  request.dateType as BankStatementDateType | undefined
);
```

**`InvoiceImportChart.tsx` — prop type and date handling**

```typescript
import { DailyInvoiceCount } from '../../api/hooks/useInvoiceImportStatistics'; // unchanged import path

// Before: format(parseISO(item.date), ...)
// After:  format(item.date!, ...)
// Also remove parseISO from date-fns import if it becomes unused
```

**`BankStatementImportChart.tsx` — prop type and date handling**

```typescript
import { DailyBankStatementStatistics } from '../../api/hooks/useBankStatements';

interface BankStatementImportChartProps {
  data: DailyBankStatementStatistics[]; // was BankStatementImportStatisticsDto
  // ...
}

// Before: parseISO(item.date)
// After:  item.date! (non-null assertion; date is always populated from backend)
// Remove parseISO from date-fns import if unused
```

### Data Flow

**Invoice import statistics (after change)**

```
useInvoiceImportStatistics({ dateType, daysBack })
  → getAuthenticatedApiClient()                    // returns ApiClient with auth fetch
  → apiClient.analytics_GetInvoiceImportStatistics(dateType as ImportDateType, daysBack ?? null)
      → builds URL with DateType= and DaysBack= query params (handled inside generated method)
      → calls this.http.fetch(url, { method: "GET" })   // auth headers injected by client wrapper
      → deserializes via GetInvoiceImportStatisticsResponse.fromJS(...)
          → DailyInvoiceCount.init → this.date = new Date(rawString)
  → returns Promise<GetInvoiceImportStatisticsResponse>

InvoiceImportChart receives data: DailyInvoiceCount[]
  → item.date is already Date — format(item.date!, 'dd.MM.', ...) works directly
```

**Bank statement import statistics (after change)**

```
useBankStatementImportStatistics({ startDate, endDate, dateType })
  → getAuthenticatedApiClient()
  → apiClient.analytics_GetBankStatementImportStatistics(
        startDate ? new Date(startDate) : null,
        endDate ? new Date(endDate) : null,
        dateType as BankStatementDateType | undefined
      )
      → builds URL with StartDate=, EndDate=, DateType= (ISO serialization handled inside generated method)
      → deserializes via GetBankStatementImportStatisticsResponse.fromJS(...)
          → DailyBankStatementStatistics.init → this.date = new Date(rawString)
  → returns Promise<GetBankStatementImportStatisticsResponse>

BankStatementImportChart receives data: DailyBankStatementStatistics[]
  → item.date is already Date — format(item.date!, ...) works directly
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated `DailyInvoiceCount.date` and `DailyBankStatementStatistics.date` are typed `Date | undefined`, not `Date`. Chart code using `item.date!` non-null assertion will not fail at runtime if the backend always populates the field, but TypeScript will not enforce it. | Low | Use non-null assertion (`item.date!`) only where the field is structurally required. Consider adding a guard in the chart's `.map()` if defensive coding is preferred: `item.date ?? new Date()`. |
| `getAuthenticatedApiClient` is synchronous — removing `await` from `const apiClient = getAuthenticatedApiClient()` is correct but the existing hooks use `await` harmlessly. If the `await` is left in, it is still correct (awaiting a non-Promise is a no-op). | Low | Remove `await` for clarity and consistency with the synchronous return type documented in `client.ts`. |
| URL casing difference: raw hooks used `/api/analytics/invoice-import-statistics` (lowercase) while the generated method builds `/api/Analytics/invoice-import-statistics` (capital A). ASP.NET Core routing is case-insensitive, so this is not a functional regression, but it is a visible change in network traffic. | Low | No action required — ASP.NET Core routing is case-insensitive by default. |
| `BankStatementImportChart` receives `data: BankStatementImportStatisticsDto[]` today. After the rename to `DailyBankStatementStatistics`, any other consumer of the chart that explicitly types its own variable as `BankStatementImportStatisticsDto` will get a compile error. | Low | Search for all `BankStatementImportChart` usages before shipping; update prop types at call sites. |
| The `statistics` field on `GetBankStatementImportStatisticsResponse` is typed `DailyBankStatementStatistics[] | undefined`, whereas the old hand-written `GetBankStatementImportStatisticsResponse.statistics` was also `BankStatementImportStatisticsDto[]` (non-optional). Chart consumers must handle undefined. | Medium | Check all usages of `response.statistics` and add a fallback (`response.statistics ?? []`) where the chart prop is populated. |

## Specification Amendments

**Amendment 1 — DTO name change in `BankStatementImportChart.tsx`**

The spec (FR-6) says "update `BankStatementImportChart.tsx` for `date: Date`" but does not name the prop-type rename. The generated type is `DailyBankStatementStatistics`, not `BankStatementImportStatisticsDto`. The chart's props interface and its import must both be updated. This is implied by FR-5 but should be made explicit.

**Amendment 2 — `statistics` vs `data` field name**

The hand-written `GetBankStatementImportStatisticsResponse` used field name `statistics: BankStatementImportStatisticsDto[]`. The generated response also uses `statistics: DailyBankStatementStatistics[]`. These names align — no rename is required at the chart prop boundary. However the spec and FR-4 should explicitly note that `response.statistics` replaces `response.statistics` (same name) to prevent implementors from assuming a rename.

**Amendment 3 — `gcTime` missing from `useBankStatementImportStatistics`**

The current hook sets only `staleTime: 5 * 60 * 1000` with no `gcTime`. NFR-2 requires no behavioral regression, so `gcTime` should remain absent after the refactor. The spec is consistent with this; no change required — noting it here to prevent an implementor from "helpfully" adding `gcTime: 10 * 60 * 1000` to match the other hook.

## Prerequisites

None. The generated client already exposes both target methods and types. No migrations, no infrastructure changes, no new dependencies.
