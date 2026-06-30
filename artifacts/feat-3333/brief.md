## Module
Analytics (frontend)

## Finding
`frontend/src/api/hooks/useInvoiceImportStatistics.ts` (lines 10–21, 41–49) and `frontend/src/api/hooks/useBankStatements.ts` (lines 4–53, 74–78) both:

1. **Define custom TypeScript interfaces** that duplicate types already generated from the OpenAPI spec — e.g. `DailyInvoiceCount`, `InvoiceImportStatisticsResponse`, `BankStatementImportStatisticsDto`, `GetBankStatementImportStatisticsResponse`, etc.
2. **Bypass the generated API client** via raw `(apiClient as any).http.fetch(fullUrl, ...)` instead of calling the typed generated methods.

The generated client already provides exactly these methods:
```ts
// frontend/src/api/generated/api-client.ts:286
analytics_GetInvoiceImportStatistics(dateType, daysBack): Promise

// frontend/src/api/generated/api-client.ts:344
analytics_GetBankStatementImportStatistics(startDate, endDate, dateType): Promise
```

The correct pattern is already established in `frontend/src/api/hooks/useProductMarginSummary.ts:25`:
```ts
return apiClient.analytics_GetProductMarginSummary(timeWindow, 0, groupingMode, marginLevel, sortBy, true);
```

The `as any` cast also suppresses TypeScript compile-time safety for the HTTP layer.

## Why it matters
- When the backend contract changes (response shape, field rename, new required field), the generated client is updated automatically — but the manually-typed interfaces in these two hooks silently drift
- Any serialization mismatch (e.g. an `enum` value change, a null vs. undefined field) will only fail at runtime, not at build time
- The `(apiClient as any).http.fetch` approach bypasses any auth header injection, retry logic, or interceptors that the generated client applies — this could silently drop authentication tokens

## Suggested fix
For each hook, replace the raw fetch + custom interfaces with a call to the generated typed method:

**`useInvoiceImportStatistics.ts`** — remove custom interfaces `DailyInvoiceCount` and `InvoiceImportStatisticsResponse`; replace `queryFn` with:
```ts
const apiClient = await getAuthenticatedApiClient();
return apiClient.analytics_GetInvoiceImportStatistics(
  dateType as ImportDateType,
  daysBack ?? null
);
```

**`useBankStatements.ts`** — for `useBankStatementImportStatistics`: remove duplicate interfaces and replace `queryFn` with:
```ts
const apiClient = await getAuthenticatedApiClient();
return apiClient.analytics_GetBankStatementImportStatistics(
  request.startDate ? new Date(request.startDate) : null,
  request.endDate ? new Date(request.endDate) : null,
  request.dateType as BankStatementDateType ?? undefined
);
```

Import the generated types instead of redefining them.

---
_Filed by daily arch-review routine on 2026-06-24._
