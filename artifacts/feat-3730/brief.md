## Module
Manufacture (frontend)

## Finding
`frontend/src/api/hooks/useManufacturingStockAnalysis.ts` declares six TypeScript types locally that are not imported from the auto-generated client (`frontend/src/api/generated/api-client.ts`):

| Hand-coded type | Lines |
|---|---|
| `GetManufacturingStockAnalysisRequest` (interface) | 28–43 |
| `ManufacturingStockSortBy` (enum) | 45–59 |
| `ManufacturingStockSeverity` (enum) | 61–67 |
| `ManufacturingStockItemDto` (interface) | 69–91 |
| `ManufacturingStockSummaryDto` (interface) | 92–103 |
| `GetManufacturingStockAnalysisResponse` (interface) | 105–111 |

The hook also bypasses the generated client's method and constructs the request URL manually (line 134, 168):
```ts
const relativeUrl = `/api/manufacturing-stock-analysis`;
// ... URL-encoded params built by hand ...
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}${queryString ? `?${queryString}` : ""}`;
const response = await (apiClient as any).http.fetch(fullUrl, { method: "GET", ... });
return response.json() as Promise<GetManufacturingStockAnalysisResponse>;
```

Every other Manufacture hook (`useManufactureBatch.ts`, `useManufactureOrders.ts`, `useManufactureSettings.ts`) uses the typed generated client methods (e.g. `apiClient.manufactureBatch_GetBatchTemplate(...)`).

The hand-coded approach means:
- The TypeScript types can silently drift from the backend's actual response if a field is added, renamed, or removed in the `GetManufacturingStockAnalysisHandler` response.
- The `ManufacturingStockSortBy` and `ManufacturingStockSeverity` enums are maintained in two places (backend C# and frontend TS) with no enforcement that they stay in sync.
- The `cast as Promise<GetManufacturingStockAnalysisResponse>` on line 181 is an unchecked type assertion — the compiler accepts any shape.

## Why it matters
The project generates a TypeScript client from OpenAPI on each build precisely to prevent type drift between backend and frontend. This hook bypasses that safety net entirely. The `/api/manufacturing-stock-analysis` endpoint is clearly an API controller action (the backend has `GetManufacturingStockAnalysisHandler` with `GetManufacturingStockAnalysisRequest` and `GetManufacturingStockAnalysisResponse`), so a generated client method should exist.

## Suggested fix
Check why the endpoint is absent from the generated client (likely a missing `[ProducesResponseType]` on the controller action or a missing OpenAPI `[HttpGet]` attribute), fix the omission, rebuild the generated client, then:

1. Replace the hand-coded interfaces with imports from `api/generated/api-client`.
2. Replace the manual `fetch` call with the typed generated method (e.g. `apiClient.manufactureStockAnalysis_GetAnalysis(...)`).
3. Delete the six locally-declared types and the manual URL construction.

The `useManufacturingStockAnalysis.ts` helper functions (`getManufacturingSeverityColorClass`, `formatWarehouseStock`, etc.) that reference `ManufacturingStockSeverity` and `ManufacturingStockItemDto` should then import those types from the generated module.

---
_Filed by daily arch-review routine on 2026-07-23._
