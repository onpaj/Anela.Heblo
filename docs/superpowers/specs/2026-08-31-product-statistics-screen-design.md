# Product Statistics Screen: Multi-Product History Over a Date Range

## Context

Historical quantities — sold, purchased, consumed, manufactured — are visible today only on
`CatalogDetail`, one product at a time, over a fixed 13-month window. There is no way to compare
two products against each other, and no way to look at an arbitrary period.

The data already exists in the in-memory catalog cache. `CatalogAggregate` carries, per product:

| Data | Shape |
|---|---|
| `SaleHistorySummary.MonthlyData` | pre-aggregated per month, keyed `"yyyy-MM"` |
| `ConsumedHistorySummary.MonthlyData` | pre-aggregated per month, keyed `"yyyy-MM"` |
| `PurchaseHistory` | individual records with `Date` + `Amount` |
| `ManufactureHistory` | individual records with `Date` + `Amount` |

`ICatalogRepository.GetByIdsAsync(IEnumerable<string> ids)` already returns many aggregates in one
call from that cache.

**Goal**: a new screen where the user selects several products and a month range, and sees — per
metric tab — one chart line per product plus a month-by-month table.

**Non-goal**: changing `CatalogDetail`, its chart components, or any existing catalog contract.
This feature is additive: one new read-only use case and one new page.

## Decisions Taken

Settled during brainstorming; these constrain the design.

| Decision | Choice | Reason |
|---|---|---|
| Tabs | Prodeje, Nákupy, Spotřeba, Výroba | All four histories exist on the aggregate |
| Chart shape | One line per product | The point of the screen is comparison |
| Table shape | Months = rows, products = columns | Reads like a report; totals in both directions |
| Granularity | Calendar month only | Sales and consumption exist *only* as monthly summaries; any finer bucket would be a lie |
| Product picking | Multi-select autocomplete on product code/name | No category expansion |
| Metric | Quantity, all four tabs | No revenue / B2B / B2C toggles |
| Placement | `/products/statistics` under Produkty | Reuses the existing `Products_Catalog` permission |
| Export | None | YAGNI |
| Max products | 10 per query | Chart legibility and payload cap |

## Architecture

### Backend — one new vertical slice

`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductStatistics/`

```
GetProductStatisticsRequest.cs    : IRequest<GetProductStatisticsResponse>
GetProductStatisticsResponse.cs   : BaseResponse
GetProductStatisticsHandler.cs
```

Exposed on the existing `CatalogController`:

```
GET /api/catalog/product-statistics
    ?productCodes=PROD-A&productCodes=PROD-B
    &metric=Sales
    &dateFrom=2024-09
    &dateTo=2025-08
```

#### Contract

DTOs are **classes, never records** — the OpenAPI generator mishandles record parameter order.
`GetProductStatisticsResponse` **must** inherit `BaseResponse`, or the reflection contract test
fails in CI.

```csharp
public enum ProductStatisticsMetric { Sales, Purchase, Consumption, Manufacture }

public class GetProductStatisticsRequest : IRequest<GetProductStatisticsResponse>
{
    public List<string> ProductCodes { get; set; } = new();
    public ProductStatisticsMetric Metric { get; set; }
    public string DateFrom { get; set; } = null!;   // "yyyy-MM"
    public string DateTo { get; set; } = null!;     // "yyyy-MM"
}

public class GetProductStatisticsResponse : BaseResponse
{
    public List<string> Months { get; set; } = new();               // dense, ascending, "yyyy-MM"
    public List<ProductStatisticsSeriesDto> Products { get; set; } = new();
}

public class ProductStatisticsSeriesDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public List<double> Values { get; set; } = new();   // same length & order as Months
}
```

`Months` is emitted **once** for the whole response and every series is index-aligned to it. That
keeps the payload small, makes gap-filling the server's job, and lets the chart and table share one
x-axis without either re-deriving it.

Months are `"yyyy-MM"` strings, not `DateTime`. A month is not an instant; serializing it as one
would invite timezone drift between backend, JSON, and the browser.

#### Why a `metric` parameter rather than all four metrics in one payload

The alternative — return sales, purchases, consumption and manufacture together — makes tab
switching free on first load, but the DTO then carries four differently-shaped histories and every
tab pays for the three the user is not looking at.

With one metric per request the contract stays flat, and React Query caches per
`(metric, productCodes, dateFrom, dateTo)`, so returning to an already-visited tab is instant
anyway. The cost of a cache miss is a projection over an in-memory list.

#### Projection

The handler resolves aggregates via `GetByIdsAsync`, then builds the dense month list from
`dateFrom`/`dateTo` and maps each product onto it:

| Metric | Source | Value |
|---|---|---|
| `Sales` | `SaleHistorySummary.MonthlyData[key]` | `TotalAmount` |
| `Consumption` | `ConsumedHistorySummary.MonthlyData[key]` | `TotalAmount` |
| `Purchase` | `PurchaseHistory` grouped by `Date` year+month | `Sum(Amount)` |
| `Manufacture` | `ManufactureHistory` grouped by `Date` year+month | `Sum(Amount)` |

Months with no data yield `0`, not a gap — a missing month and a zero month mean the same thing
here, and a dense array keeps the frontend free of null handling.

Series are returned in the order the caller listed `productCodes`, so chart colors stay stable as
the user adds and removes products.

Product codes that resolve to nothing are **skipped silently** — they are simply absent from
`Products`. A stale bookmark naming a deleted product should still render the products that do
exist, not fail the whole request.

#### Validation

`GetProductStatisticsRequestValidator` (FluentValidation), registered **manually** in
`CatalogModule` — this project has no `AddValidatorsFromAssembly`, so both the validator and its
`ValidationBehavior<TRequest, TResponse>` pipeline entry must be added next to the existing
`GetCatalogDetailRequest` registrations, or validation silently never runs.

Rules:

- `ProductCodes`: 1–10 entries, no blanks, deduplicated.
- `DateFrom` / `DateTo`: match `^\d{4}-(0[1-9]|1[0-2])$`.
- `DateFrom <= DateTo`.
- `DateFrom` floored at `CatalogConstants.HISTORY_FLOOR_DATE` (2020-01); anything earlier is
  clamped rather than rejected, matching how `GetCatalogDetailHandler.ComputeFromDate` treats the
  history floor.

The 10-product cap is the one rule that bounds the response size, so it belongs in the validator
rather than in the frontend alone.

### Frontend

```
frontend/src/components/pages/ProductStatistics.tsx           shell: tabs + filter + content
frontend/src/components/product-statistics/
    ProductStatisticsFilter.tsx        multi-product picker + from/to month inputs
    ProductStatisticsChart.tsx         chart.js <Line>, one dataset per product
    ProductStatisticsTable.tsx         months = rows, products = columns
    productStatisticsColors.ts         stable palette, index-keyed
frontend/src/api/hooks/useProductStatistics.ts                React Query hook
```

Every file stays well under the 400-line guidance; the page shell owns state, the three children
are presentational.

#### Filter

Filter state lives in `ProductStatistics.tsx` and is **shared across all four tabs** — switching
from Prodeje to Spotřeba keeps the product selection and range, and only re-queries.

Product picking extends the existing `frontend/src/components/common/CatalogAutocomplete.tsx` with
an `isMulti` mode. The component already imports react-select's `MultiValue` and `ActionMeta`
without using them, so this is the natural place for it rather than a second, near-duplicate
component. The change is additive: `isMulti?: boolean` plus a `values`/`onSelectMany` pair, with
the existing single-select props and every current call site untouched.

Range is two `<input type="month">` fields, defaulting to the **last 13 months** — the same window
`CatalogDetail` shows today, so the new screen opens on a familiar picture.

#### Data hook

`useProductStatistics(productCodes, metric, dateFrom, dateTo)` wraps the generated client method,
`enabled` only when at least one product is selected and the range is valid. Query key includes all
four inputs.

The generated client **throws** on non-200 — `if (!response.success)` branches are dead code in
this codebase. Errors surface through React Query's `error` and are rendered with the shared
`ErrorState` component; `errorCode` on a caught `SwaggerException` is a string.

#### Chart

Same chart.js `<Line>` setup as `ProductChart.tsx`, with N datasets instead of one. Labels come
from the response's `Months` — **not** from `ChartHelpers.generateMonthLabels()`, which hardcodes
13 months and cannot express a variable range. `ChartHelpers.tsx` is left untouched.

Colors are assigned by index from a fixed palette in `productStatisticsColors.ts`, both themes
accounted for. Legend sits on top with chart.js's built-in click-to-hide.

Journal-entry point markers from `ProductChart` are **not** carried over: they annotate one
product's timeline and have no meaning on a multi-product chart.

Empty state (no products selected, or every series all-zero) reuses the existing
`BarChart3` + "Žádná data" pattern from `ProductChart`.

#### Table

Rows are months, **newest first** — the inverse of the chart's ascending axis, but consistent with
every other table in this app. Columns: `Měsíc`, one per selected product, then `Celkem` per row.
A footer row totals each product column and the grand total.

Wrapped in an `overflow-x-auto` container so ten product columns degrade to horizontal scroll
rather than breaking the layout.

### Routing and access

| File | Change |
|---|---|
| `frontend/src/App.tsx` | `<Route path="/products/statistics" element={guard("/products/statistics", <ProductStatistics />)} />` |
| `frontend/src/components/layout/Sidebar.tsx` | item under Produkty: `{ id: "statistiky-produktu", name: "Statistiky", href: "/products/statistics", key: "/products/statistics" }` |
| `access-matrix.json` | route entry `{ "path": "/products/statistics", "requires": [{ "feature": "Products_Catalog", "level": "Read" }] }` |

No new feature key, so no group seeding and no role changes — anyone who can see the catalog can
see its statistics.

## Error Handling

| Situation | Behavior |
|---|---|
| No products selected | Query disabled; page shows a "vyberte produkty" prompt. No request fired. |
| More than 10 products | Picker caps the selection with an inline message; validator rejects as a backstop |
| Invalid or inverted range | Inline field error; query stays disabled |
| Unknown product code | Skipped server-side; absent from the response and from chart/table |
| Product with no history in range | Present as an all-zero series — the user asked about it, so it stays visible |
| Backend failure | React Query `error` → shared `ErrorState`, with retry |

## Testing

**Backend** (`backend/test/`):

- Handler: one test per metric verifying values land in the right month slot.
- Handler: dense gap-filling — a product with data in only one month of a six-month range.
- Handler: purchase/manufacture records within a single month are summed, not overwritten.
- Handler: unknown product code omitted, known ones still returned.
- Handler: series order matches request order.
- Handler: month boundaries — first and last month of the range are inclusive.
- Validator: empty selection, 11 products, malformed month, inverted range, pre-2020 floor clamp.
- Controller contract test alongside the existing catalog controller tests.

**Frontend** (`react-scripts test`, not `npx jest`):

- `useProductStatistics`: query key composition; disabled when selection is empty.
- `ProductStatisticsChart`: one dataset per product; labels taken from `Months`; empty state.
- `ProductStatisticsTable`: row totals, column totals, zero months rendered as `0`.
- `ProductStatisticsFilter`: selection cap; range validation.
- `CatalogAutocomplete`: existing single-select tests still pass; new multi-select behavior covered.

**E2E**: none in this change. The suite runs nightly against deployed staging and cannot validate
uncommitted frontend work.

## Out of Scope

- Export to CSV/Excel.
- Category or product-group expansion in the picker.
- Revenue, B2B/B2C, margin, or price metrics.
- Quarter/year bucketing.
- URL-encoded filter state.
- Any change to `CatalogDetail`, `ProductChart`, `ProductSummaryTabs`, or `ChartHelpers`.

## File Manifest

**New — backend**

```
Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsRequest.cs
Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsResponse.cs
Features/Catalog/UseCases/GetProductStatistics/GetProductStatisticsHandler.cs
Features/Catalog/Contracts/ProductStatisticsSeriesDto.cs
Features/Catalog/Contracts/ProductStatisticsMetric.cs
Features/Catalog/Validators/GetProductStatisticsRequestValidator.cs
```

**New — frontend**

```
components/pages/ProductStatistics.tsx
components/product-statistics/ProductStatisticsFilter.tsx
components/product-statistics/ProductStatisticsChart.tsx
components/product-statistics/ProductStatisticsTable.tsx
components/product-statistics/productStatisticsColors.ts
api/hooks/useProductStatistics.ts
```

**Modified**

```
backend/src/Anela.Heblo.API/Controllers/CatalogController.cs        + endpoint
backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs + validator & behavior
frontend/src/components/common/CatalogAutocomplete.tsx              + isMulti mode
frontend/src/App.tsx                                                + route
frontend/src/components/layout/Sidebar.tsx                          + menu item
access-matrix.json                                                  + route entry
```

The TypeScript client is regenerated on build; `dotnet msbuild -t:GenerateFrontendClientManual`
forces it between builds.

## Validation Before Completion

- BE: `dotnet build` + `dotnet format`
- FE: `CI=false npm run build` + `npm run lint` — `npx tsc --noEmit` false-greens in this repo
- All touched tests pass
