# Design: Extract duplicated `HasSalesInPeriod` logic into a shared `AnalyticsProduct` extension

## Component Design

### `AnalyticsProductExtensions` (new — Domain layer)
- **File:** `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs`
- **Namespace:** `Anela.Heblo.Domain.Features.Analytics`
- **Responsibility:** Single canonical, stateless predicate for whether an `AnalyticsProduct` has any sales activity within an inclusive date range. Co-located with `AnalyticsProduct.cs`, following the existing `{Entity}Extensions` convention already used by `CarrierExtensions`, `CurrentUserExtensions`, and `ManufactureOrderExtensions`.
- **Interface:**
  ```csharp
  public static class AnalyticsProductExtensions
  {
      public static bool HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate)
          => product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
  }
  ```
- **Contract:** Pure function, no side effects, no dependencies (no repository, no config, no logging). Given `product.SalesHistory`, returns `true` if at least one `SalesDataPoint.Date` falls within `[startDate, endDate]` inclusive; `false` otherwise (including when `SalesHistory` is empty). Logically identical to the two existing private implementations being replaced — no change to comparison operators or date normalization.

### `GetMarginReportHandler` (Application layer — modified)
- **File:** `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`
- **Change:** Remove the private `static bool HasSalesInPeriod(...)` method (lines 125-128). `ProcessProductsForReport` now calls `product.HasSalesInPeriod(startDate, endDate)` (extension-method syntax) at the existing call site (line 95) instead of the removed private method.
- **Dependency:** Consumes `AnalyticsProductExtensions` via the existing `using Anela.Heblo.Domain.Features.Analytics;` import — no new using statement or project reference required.

### `GetProductMarginAnalysisHandler` (Application layer — modified)
- **File:** `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`
- **Change:** Remove the private `static bool HasSalesInPeriod(...)` method (lines 71-74). `Handle` now calls `productData.HasSalesInPeriod(request.StartDate, request.EndDate)` (extension-method syntax) at the existing call site (line 51) instead of the removed private method.
- **Dependency:** Same as above — existing `using Anela.Heblo.Domain.Features.Analytics;` import already brings the extension method into scope.

### Unaffected components
`IMarginCalculator`, `IProductFilterService`, `IReportBuilderService`, `IAnalyticsRepository`, and all MediatR request/response contracts are unchanged. No DI registration is needed for the new static extension class.

## Data Schemas

No data model, database schema, or API request/response shape changes.

- `AnalyticsProduct` and `SalesDataPoint` (`backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs`) are unmodified.
- No changes to `GetMarginReportRequest` / `GetMarginReportResponse` or `GetProductMarginAnalysisRequest` / `GetProductMarginAnalysisResponse`.
- New public surface is limited to the method signature below (Domain project); no DTO or wire-format is introduced:
  ```
  Anela.Heblo.Domain.Features.Analytics.AnalyticsProductExtensions
      .HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate) : bool
  ```
