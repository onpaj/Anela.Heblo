# Design: Replace raw `http.fetch` bypass in `useFinancialOverviewQuery`

## Component Design

### `useFinancialOverviewQuery` (`frontend/src/api/hooks/useFinancialOverview.ts`)
The hook's public contract is unchanged and is the boundary this design preserves exactly:

- **Signature:** `useFinancialOverviewQuery(months = 6, includeStockData = true, excludedDepartments: string[] = [], includeCurrentMonth = false): UseQueryResult<GetFinancialOverviewResponse, Error>`
- **Responsibility:** Given filter parameters, return a TanStack Query result wrapping a `GetFinancialOverviewResponse` for the FinancialOverview page. The hook owns the `queryKey`, `staleTime` (5 min), and `gcTime` (10 min) — none of these change.
- **Internal collaborator swap:** the `queryFn` stops performing manual `URLSearchParams` construction and a raw `(apiClient as any).http.fetch(...)` call, and instead delegates the request entirely to the generated client method:

  ```typescript
  queryFn: async () => {
    const apiClient = getAuthenticatedApiClient();
    return await apiClient.financialOverview_GetFinancialOverview(
      months,
      includeStockData,
      excludedDepartments,
      includeCurrentMonth,
    );
  },
  ```

  `excludedDepartments` is forwarded unmodified (no ternary/ guard) — the generated method already no-ops on an empty array.
- **Removed responsibilities:** query-string construction, `response.ok` checking, `response.json()` parsing, and the `as GetFinancialOverviewResponse` cast. These become the generated method's responsibility.
- **Retained responsibilities:** re-exporting `GetFinancialOverviewResponse`, `MonthlyFinancialDataDto`, `FinancialSummaryDto`, `StockChangeDto`, `StockSummaryDto` for consumers; no change to what is re-exported or how.

### `ApiClient.financialOverview_GetFinancialOverview` (`frontend/src/api/generated/api-client.ts:3809`, generated, not modified)
Pre-existing generated method, now the sole call path for this endpoint:

- **Interface:** `financialOverview_GetFinancialOverview(months: number | null | undefined, includeStockData: boolean | undefined, excludedDepartments: string[] | null | undefined, includeCurrentMonth: boolean | undefined): Promise<GetFinancialOverviewResponse>`
- **Responsibility:** build the query string (including repeated `excludedDepartments` params), issue the GET via `this.http.fetch` (the same authenticated fetcher `getAuthenticatedApiClient()` wires up with auth headers, 401 handling, and error toasts), and resolve/reject based on status:
  - 200/204 → resolves `GetFinancialOverviewResponse.fromJS(...)`.
  - other statuses → `throwException(...)`, producing a `SwaggerException` (extends `Error`) or `ProblemDetails`-derived error.
- **Consumers:** `useFinancialOverviewQuery`'s `queryFn` is the only caller relevant to this change.

### Consuming component (`FinancialOverview.tsx` and children)
No changes. They continue to consume `useFinancialOverviewQuery`'s `data`/`error`/loading state exactly as before; the `Error` shape they receive on failure remains an `Error` instance with a populated `message`, satisfying existing error-rendering logic without modification.

### Out of scope for this design
`ManufacturingStockAnalysis.tsx` and `TransportBoxDetail.tsx` contain similar `.http.fetch` bypasses but are explicitly out of scope (per spec and arch-review); they are not touched or redesigned here.

## Data Schemas

No schema changes of any kind. All types are pre-existing and unmodified:

- **Request shape (wire format), unchanged:**
  `GET /api/FinancialOverview?months={number}&includeStockData={bool}&excludedDepartments={string}&excludedDepartments={string}...&includeCurrentMonth={bool}`
  - `months`: number, default `6`
  - `includeStockData`: boolean, default `true`
  - `excludedDepartments`: zero or more repeated string params, default none (empty array serializes to no params)
  - `includeCurrentMonth`: boolean, default `false`

- **Response shape, unchanged** — `GetFinancialOverviewResponse` (`frontend/src/api/generated/api-client.ts:20378`) and its nested DTOs:
  - `MonthlyFinancialDataDto`
  - `FinancialSummaryDto`
  - `StockChangeDto`
  - `StockSummaryDto`

  Deserialization moves from a manual `response.json() as GetFinancialOverviewResponse` cast to the generated `GetFinancialOverviewResponse.fromJS(...)`, which produces structurally identical instances for the same JSON payload.

- **Error shape, unchanged in effect:** previously `new Error(\`Failed to fetch financial overview: ${response.statusText}\`)`; now a `SwaggerException` (extends `Error`) or `ProblemDetails`-derived exception thrown by the generated method's `throwException(...)`. Both satisfy `useQuery<GetFinancialOverviewResponse, Error>` and expose a non-empty `message`.

- **No database schema changes, no backend contract changes, no OpenAPI/NSwag regeneration.** This design touches only the internal implementation of one `queryFn`.
