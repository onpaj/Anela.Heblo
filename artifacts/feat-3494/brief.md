## Module
FinancialOverview

## Finding
`frontend/src/api/hooks/useFinancialOverview.ts` lines 36–37 access an internal implementation detail of the generated NSwag client instead of calling its typed endpoint method:

```typescript
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
```

`(apiClient as any).http` is the raw internal HTTP handler of the NSwag-generated client. The generated `api-client.ts` should expose a typed `financialOverviewGet(...)` method (NSwag naming pattern) that wraps exactly this call — with correct serialization, auth headers, and error handling wired in.

Note: the first cast `(apiClient as any).baseUrl` follows the project rule for absolute URL construction and is acceptable. The second cast `(apiClient as any).http.fetch` is the problem.

## Why it matters
1. **Middleware bypass** — any request interceptor, token-refresh hook, or telemetry wired into the generated client's `http` pipeline is silently skipped.
2. **Fragile** — the internal `http` property name is an NSwag implementation detail; a client regeneration or template change can rename or remove it with no compile-time error.
3. **Type-unsafe** — the `as any` casts suppress TypeScript errors; an incorrect URL or missing query parameter won't be caught at build time.

## Suggested fix
Use the generated typed method directly (adjust the name to match what NSwag emits):

```typescript
queryFn: async () => {
  const apiClient = getAuthenticatedApiClient();
  return await apiClient.financialOverviewGet(
    months,
    includeStockData,
    excludedDepartments.length > 0 ? excludedDepartments : undefined,
    includeCurrentMonth
  );
},
```

If the generated method signature does not match the query-string shape, update the NSwag template rather than going around the client. If the generated client genuinely lacks a method for this endpoint, that is a generation gap to fix at the source (the OpenAPI spec or generation config), not a reason to use raw fetch.

---
_Filed by daily arch-review routine on 2026-07-05._
