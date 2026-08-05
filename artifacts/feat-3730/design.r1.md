# Design: Migrate `useManufacturingStockAnalysis` to the generated OpenAPI client

## Component Design

No new components are introduced. This is a rewiring of two existing call sites onto the codebase's standard generated-client pattern (as already used by `useManufactureBatch.ts`, `useManufactureOrders.ts`, `useManufactureSettings.ts`).

### `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`
- **Responsibility (unchanged):** expose `useManufacturingStockAnalysisQuery(request)` — a TanStack Query hook — as the single data-access surface for the Manufacturing Stock Analysis feature, plus re-export the request/response/enum types the page component consumes.
- **Type source (changed):** removes the six hand-declared types (`GetManufacturingStockAnalysisRequest`, `ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto`, `ManufacturingStockSummaryDto`, `GetManufacturingStockAnalysisResponse`). Imports and re-exports the equivalent generated classes/enums from `../generated/api-client`:
  - `GetManufacturingStockAnalysisResponse` (class)
  - `ManufacturingStockItemDto` (class)
  - `ManufacturingStockSummaryDto` (class)
  - `ManufacturingStockSeverity` (string enum — was numeric locally; now string-valued, matching the backend's JSON serialization)
  - `ManufacturingStockSortBy` (string enum)
  - The generated `TimePeriod` enum, imported under the local alias `GeneratedTimePeriod` to avoid colliding with the existing app-level `TimePeriod` import from `utils/timePeriod/timePeriod.ts`.
- **Request shape:** the hook's own parameter object type (accepted by `useManufacturingStockAnalysisQuery`) stays a locally-declared plain object — the generated client method takes positional scalar arguments, not a request object, so a thin local interface is still needed as the hook's public parameter contract. Its `sortBy` field types against the generated `ManufacturingStockSortBy`; its `timePeriod` field keeps typing against the app-level `TimePeriodFilter` (unchanged for all other consumers) and is converted at the boundary — see "Boundary conversion" below.
- **`queryFn` (changed):** replaces manual `URLSearchParams` construction and `(apiClient as any).http.fetch(...)` with a direct positional call:
  ```ts
  apiClient.manufacturingStockAnalysis_GetStockAnalysis(
    timePeriod, customFromDate, customToDate, productFamily,
    criticalItemsOnly, majorItemsOnly, adequateItemsOnly, unconfiguredOnly,
    searchTerm, pageNumber, pageSize, sortBy, sortDescending,
    salesMultiplier, isExport /* = false here */
  )
  ```
  Returns the awaited result directly — no manual `response.ok` check, `response.json()`, or `as Promise<...>` cast. Error propagation to `useQuery`'s `error` is unchanged: the generated method's `processManufacturingStockAnalysis_GetStockAnalysis` already throws via `throwException` on non-2xx.
- **Boundary conversion (new, single location):** immediately before the generated-method call, convert `request.timePeriod` (app `TimePeriodFilter`) to the generated `GeneratedTimePeriod` via a same-string-value cast (`request.timePeriod as unknown as GeneratedTimePeriod`), with a one-line comment explaining why the cast is safe (identical string members, nominal-typing friction only). No value-by-value mapping table.
- **`Q9M` omission preserved:** the existing conditional that omits `timePeriod` from the request when it equals `TimePeriod.Q9M` must survive the rewrite — pass `undefined` for the `timePeriod` positional argument in that case, not the literal converted value.
- **`formatDateForApi` removed** if it becomes dead code — the generated method accepts `Date | null | undefined` directly and serializes via `.toISOString()` internally, so the `YYYY-MM-DD` truncation helper is no longer needed. `customFromDate`/`customToDate` are passed as `Date` objects, not pre-formatted strings.
- **Re-export surface (unchanged shape):** `ManufacturingStockSeverity`, `ManufacturingStockSortBy`, and `ManufacturingStockItemDto` continue to be re-exported from this module as thin passthroughs of the generated symbols, so `ManufacturingStockAnalysis.tsx`'s and `ManufactureBatchPlanning.tsx`'s existing import statements keep compiling unchanged.

### `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — `handleExport`
- **Responsibility (unchanged):** build the export request from the current `filters` state and produce the `.xlsx` file via `exportToXlsx`, with identical columns, headers, ordering, and values to today.
- **Data access (changed):** replaces the manual `URLSearchParams`/`(apiClient as any).http.fetch(...)` construction with a call to the same generated method, `apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)`, using the same positional arguments derived from `filters`, with `isExport: true`.
- **Row typing (changed):** the exported-row accessors (currently `(row: any) => row.code`-style) are retyped against the generated `ManufacturingStockItemDto` class instead of `any`. No column, header, or value changes — this is a typing-only change to the accessor functions.
- **Import paths (unchanged):** no changes to this file's import statements — it continues importing `ManufacturingStockSortBy`, `ManufacturingStockSeverity`, `ManufacturingStockItemDto`, `GetManufacturingStockAnalysisRequest` from `../../api/hooks/useManufacturingStockAnalysis`, per the re-export decision above.

### Data flow (both call sites)
```
ManufacturingStockAnalysis.tsx
        │
        ├── useManufacturingStockAnalysisQuery(filters) ──▶ useManufacturingStockAnalysis.ts
        │         (TanStack Query hook, unchanged shape)        │
        │                                                       ▼
        │                                         apiClient.manufacturingStockAnalysis_GetStockAnalysis(...)
        │                                                       │
        └── handleExport() ─────────────────────────────────────┘
                  (same generated method, isExport=true)
                                                                  ▼
                                                   GET /api/manufacturing-stock-analysis
                                                   (ManufacturingStockAnalysisController — unchanged)
```
Both call sites converge on the single generated method; URL-building, query-string encoding, and response parsing/error-throwing now live in exactly one place (the NSwag-generated code), matching the pattern used everywhere else in the Manufacture module.

## Data Schemas

No backend or API contract changes. This section documents the existing generated types now consumed directly (previously hand-duplicated).

### Request (positional arguments to `apiClient.manufacturingStockAnalysis_GetStockAnalysis`)
In declared order:

| # | Parameter | Type | Notes |
|---|-----------|------|-------|
| 1 | `timePeriod` | `GeneratedTimePeriod \| undefined` | Converted from app-level `TimePeriodFilter` at the call boundary; `undefined` when the app value is `Q9M` (preserves current omit-from-query behavior) |
| 2 | `customFromDate` | `Date \| null \| undefined` | Passed as `Date` directly; serialized via `.toISOString()` inside the generated method |
| 3 | `customToDate` | `Date \| null \| undefined` | Same as above |
| 4 | `productFamily` | `string \| null \| undefined` | |
| 5 | `criticalItemsOnly` | `boolean \| undefined` | |
| 6 | `majorItemsOnly` | `boolean \| undefined` | |
| 7 | `adequateItemsOnly` | `boolean \| undefined` | |
| 8 | `unconfiguredOnly` | `boolean \| undefined` | |
| 9 | `searchTerm` | `string \| null \| undefined` | |
| 10 | `pageNumber` | `number \| undefined` | |
| 11 | `pageSize` | `number \| undefined` | |
| 12 | `sortBy` | `ManufacturingStockSortBy \| undefined` | Generated string enum, 13 members |
| 13 | `sortDescending` | `boolean \| undefined` | |
| 14 | `salesMultiplier` | `number \| undefined` | |
| 15 | `isExport` | `boolean \| undefined` | `false`/`undefined` from the query hook, `true` from `handleExport` |

Adjacent same-typed parameters (e.g. the four `*ItemsOnly` booleans, `pageNumber`/`pageSize`) are a transposition risk during the rewrite; implementers must map by position exactly as declared, not by inferred order from the old query-string keys.

### Response — `GetManufacturingStockAnalysisResponse` (generated class, extends `BaseResponse`)
- `items: ManufacturingStockItemDto[]`
- `totalCount: number`
- `pageNumber: number`
- `pageSize: number`
- `summary: ManufacturingStockSummaryDto`

### `ManufacturingStockItemDto` (generated class)
Per-product stock/consumption/severity fields, field-for-field identical to the previously hand-coded interface — see `frontend/src/api/generated/api-client.ts` for the authoritative field list. Used both for on-page rendering and, post-refactor, as the typed source for `handleExport`'s xlsx column accessors (replacing `any`-typed row access).

### `ManufacturingStockSummaryDto` (generated class)
Aggregate counts by severity, plus the resolved analysis period and product family list — unchanged shape, now sourced from the generated class instead of a hand-coded interface.

### `ManufacturingStockSeverity` (generated string enum)
`Critical | Major | Minor | Adequate | Unconfigured`. Representation change from the current hand-coded **numeric** enum (`Critical = 0, ...`) to the generated **string** enum. Safe by construction: every consumer (`ManufacturingStockAnalysis.tsx`, ~17 usages) compares against the enum member symbolically (`=== ManufacturingStockSeverity.Critical`), never against a raw numeric literal.

### `ManufacturingStockSortBy` (generated string enum)
13 members, unchanged set from the hand-coded version — now sourced from the generated client.

### `TimePeriod` enum duality
Two distinct nominal TypeScript enums with identical string members (`PreviousQuarter`, `FutureQuarter`, `Y2Y`, `PreviousSeason`, `Q9M`, `CustomPeriod`):
- App-level `TimePeriod`/`TimePeriodFilter` (`frontend/src/utils/timePeriod/timePeriod.ts`) — shared domain vocabulary, used across multiple features, unchanged and out of scope.
- Generated `TimePeriod` (`frontend/src/api/generated/api-client.ts`), imported under the local alias `GeneratedTimePeriod` in `useManufacturingStockAnalysis.ts`.

Reconciled via a single same-string-value cast at the `queryFn`/`handleExport` boundary (`request.timePeriod as unknown as GeneratedTimePeriod`), not a mapping table — the two enums are structurally identical today and this is purely a TypeScript nominal-typing friction point, not a data-modeling concern.
