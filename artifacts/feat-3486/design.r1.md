# Design: Fix dead `TopProductCount` parameter in GetProductMarginSummary

## Component Design

No new components. This is a subtractive edit to an existing vertical slice — one request DTO property is removed, the generated OpenAPI client is regenerated, and the one frontend caller drops the corresponding argument. No component boundaries change.

```
ProductMarginSummary.tsx
        │ (reads data.topProducts — full list; does its own top-15/Other split;
        │  no changes required — response shape is unaffected)
        ▼
useProductMarginSummaryQuery (useProductMarginSummary.ts)
        │ calls analytics_GetProductMarginSummary(timeWindow, groupingMode,
        │                                          marginLevel, sortBy, sortDescending)
        │ — drops the `0 /* topProductCount */` positional argument
        ▼
AnalyticsController.GetProductMarginSummary([FromQuery] GetProductMarginSummaryRequest)
        │ — no signature change; [FromQuery] binds the whole DTO
        ▼
GetProductMarginSummaryHandler.Handle
        │ streams products → IMarginCalculator → GenerateTopProducts
        │ (no limit, returns all groups — never read TopProductCount, so no
        │  behavior change)
        ▼
GetProductMarginSummaryResponse { TopProducts: List<TopProductDto> (full), ... }
```

### Responsibilities and interface changes

- **`GetProductMarginSummaryRequest`** (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs`): drop the `TopProductCount` property. Remains a plain class (per project DTO convention — never a record). All other properties (`TimeWindow`, `GroupingMode`, `MarginLevel`, `SortBy`, `SortDescending`) are unchanged.
- **`AnalyticsController.GetProductMarginSummary`**: no code change. It binds the entire request object via `[FromQuery]`, so the query string simply loses one recognized parameter.
- **`GetProductMarginSummaryHandler.Handle` / `GenerateTopProducts`**: no code change. Neither method ever read `request.TopProductCount`; ranking and grouping logic is untouched.
- **`useProductMarginSummaryQuery`** (`frontend/src/api/hooks/useProductMarginSummary.ts`): update the call to the regenerated client method, removing the `0, // topProductCount = 0 means no limit` positional argument. The exact resulting argument order must match whatever the regenerated client emits (NSwag orders positional args by request-class property declaration order) — expected to become `(timeWindow, groupingMode, marginLevel, sortBy, sortDescending)`.
- **`ProductMarginSummary.tsx`**: no code change. Its client-side top-15/"Other" chart bucketing, full results table, and "Celkem skupin" total-group count already consume the complete `topProducts` array and are unaffected because the response shape does not change.
- **Generated client** (`frontend/src/api/generated/api-client.ts`): regenerated, not hand-edited, via the project's standard client-generation step (`docs/development/api-client-generation.md`). `analytics_GetProductMarginSummary` loses its `topProductCount` parameter.

## Data Schemas

### Request (query parameters)

Before:

| Param | Type | Notes |
|---|---|---|
| `timeWindow` | `string` | |
| `groupingMode` | `ProductGroupingMode` | |
| `marginLevel` | `MarginLevel` | |
| `sortBy` | `string?` | |
| `sortDescending` | `bool` | |
| `topProductCount` | `int` | **Removed** — accepted by the contract but never read by the handler |

After:

| Param | Type | Notes |
|---|---|---|
| `timeWindow` | `string` | unchanged |
| `groupingMode` | `ProductGroupingMode` | unchanged |
| `marginLevel` | `MarginLevel` | unchanged |
| `sortBy` | `string?` | unchanged |
| `sortDescending` | `bool` | unchanged |

### Response — `GetProductMarginSummaryResponse`

No change. `TopProducts: List<TopProductDto>` continues to carry the full, untruncated group list; `TopProductDto` fields are unaffected: `GroupKey`, `DisplayName`, `TotalMargin`, `ColorCode`, `M0Amount`/`M1Amount`/`M2Amount`, `M0Percentage`/`M1Percentage`/`M2Percentage`, `SellingPrice`, `PurchasePrice`, `Rank`.

### Persistence / events

None. No database schema, migration, or event payload is affected by this change.
