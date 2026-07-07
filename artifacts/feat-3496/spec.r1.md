# Specification: Extract duplicated `HasSalesInPeriod` logic into a shared `AnalyticsProduct` extension

## Summary
`GetMarginReportHandler` and `GetProductMarginAnalysisHandler` (Analytics module) each define a private, bit-for-bit identical `HasSalesInPeriod` method that checks whether an `AnalyticsProduct` has any `SalesDataPoint` within a date range. This spec covers extracting that logic into a single public extension method on `AnalyticsProduct`, and updating both handlers to call it, with no change in behavior.

## Background
The Analytics module already follows a pattern of extracting shared calculation/filtering logic into named, injectable services (`IMarginCalculator`, `IProductFilterService`, `IReportBuilderService`). `HasSalesInPeriod`, however, is duplicated as a private static method in two handlers:

- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs:125-128`
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs:71-74`

Both implementations are:
```csharp
return product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
```
differing only in the parameter name (`product` vs. `productData`). Because the method is `private`, this duplication is invisible to any consumer and easy for a reviewer to miss; if the date-range semantics ever change (e.g., exclusive end date, UTC normalization, timezone handling), a fix applied to one handler and not the other would silently produce inconsistent results between the margin report and the single-product margin analysis endpoints. Consolidating into one canonical implementation removes this risk and aligns the code with the module's existing pattern of factoring out shared logic.

`AnalyticsProduct` and `SalesDataPoint` are defined in `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs`. This is a plain domain model (not persisted directly, built by the repository layer for analytics reads), making it a natural extension-method target.

## Functional Requirements

### FR-1: Single canonical `HasSalesInPeriod` implementation
Add a new static class `AnalyticsProductExtensions` in the Domain layer, in namespace `Anela.Heblo.Domain.Features.Analytics`, file `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs`, containing:

```csharp
public static class AnalyticsProductExtensions
{
    public static bool HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate)
        => product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
}
```

The method body must be logically identical to the two existing private implementations (same inclusive `>=` / `<=` comparison, same `SalesHistory.Any` predicate). No date-range semantics change (no exclusive end date, no UTC normalization) — this is a pure move, not a behavior fix.

**Acceptance criteria:**
- `AnalyticsProductExtensions.HasSalesInPeriod` exists as a public static extension method on `AnalyticsProduct` in the Domain project, in the `Anela.Heblo.Domain.Features.Analytics` namespace.
- Given a product whose `SalesHistory` contains at least one `SalesDataPoint` with `Date` between `startDate` and `endDate` inclusive, the method returns `true`.
- Given a product whose `SalesHistory` is empty, or contains only dates strictly before `startDate` or strictly after `endDate`, the method returns `false`.
- Boundary dates (`Date == startDate`, `Date == endDate`) are treated as in-period (inclusive), matching current behavior.

### FR-2: Remove duplicate private methods and call the extension instead
Update both handlers to remove their private `HasSalesInPeriod` method and call the new extension method instead.

- In `GetMarginReportHandler.cs`, `ProcessProductsForReport` currently calls `HasSalesInPeriod(product, startDate, endDate)` (line 95) against the private static method (lines 125-128). Replace the private method with a call to the extension: `product.HasSalesInPeriod(startDate, endDate)` (or the existing static-call syntax `AnalyticsProductExtensions.HasSalesInPeriod(product, startDate, endDate)` — extension-method call syntax is preferred per FR-3), and delete the private method.
- In `GetProductMarginAnalysisHandler.cs`, `Handle` currently calls `HasSalesInPeriod(productData, request.StartDate, request.EndDate)` (line 51) against the private static method (lines 71-74). Replace with `productData.HasSalesInPeriod(request.StartDate, request.EndDate)`, and delete the private method.
- Both handlers need a `using Anela.Heblo.Domain.Features.Analytics;` import to bring the extension method into scope (already present as a `using` in both files, since both already reference `AnalyticsProduct` / `Domain.Features.Analytics.AnalyticsProduct`).

**Acceptance criteria:**
- No `HasSalesInPeriod` method remains defined in either `GetMarginReportHandler.cs` or `GetProductMarginAnalysisHandler.cs`.
- Both handlers compile and call `AnalyticsProductExtensions.HasSalesInPeriod` (via extension-method syntax) at their existing call sites.
- Existing unit tests in `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs` and `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs` continue to pass unmodified (no behavior change means no test assertions should need updating).

### FR-3: Extension-method call style
Both call sites should use C# extension-method invocation syntax (`product.HasSalesInPeriod(...)`) rather than static-method invocation syntax (`AnalyticsProductExtensions.HasSalesInPeriod(product, ...)`), for readability and to match the idiomatic style suggested in the brief.

**Acceptance criteria:**
- Call sites read as `product.HasSalesInPeriod(startDate, endDate)` / `productData.HasSalesInPeriod(request.StartDate, request.EndDate)`.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. The extension method compiles to the same static method call as before (C# extension methods are syntactic sugar); `SalesHistory.Any(...)` short-circuits on first match exactly as today.

### NFR-2: Security
Not applicable. This is a private-method-to-extension-method refactor of existing pure logic with no new data exposure, no new inputs from outside the process, and no change to authorization/validation.

## Data Model
No data model changes. `AnalyticsProduct` and `SalesDataPoint` (`backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProduct.cs`) are unchanged. This refactor only adds a stateless extension method operating on the existing `AnalyticsProduct.SalesHistory` collection.

## API / Interface Design
No public API (controller/endpoint) changes. This is an internal code-organization change:

- New public surface: `Anela.Heblo.Domain.Features.Analytics.AnalyticsProductExtensions.HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate) : bool`, in the Domain project.
- Removed: two private static methods, one each in `GetMarginReportHandler` and `GetProductMarginAnalysisHandler` (Application project).
- No changes to `GetMarginReportRequest`/`GetMarginReportResponse`, `GetProductMarginAnalysisRequest`/`GetProductMarginAnalysisResponse`, or any MediatR request/response contracts.

## Dependencies
- Domain project (`Anela.Heblo.Domain`) must be referenced by the Application project (`Anela.Heblo.Application`) — already the case, since both handlers already use `Anela.Heblo.Domain.Features.Analytics.AnalyticsProduct`.
- No new external library or service dependency introduced.
- No dependency on `IMarginCalculator` or `IProductFilterService` — those are unaffected by this change; they are referenced in the brief only as examples of the module's existing "extract shared logic" pattern.

## Out of Scope
- Any change to date-range comparison semantics (e.g., switching to exclusive end date, half-open intervals, or UTC/timezone normalization). This refactor is a pure code move; any semantic change is a separate, follow-up piece of work.
- Adding new unit tests specifically for `AnalyticsProductExtensions.HasSalesInPeriod` beyond what's needed to confirm existing handler tests still pass — recommended as a fast-follow, but not required to satisfy this spec's acceptance criteria (existing handler-level tests already exercise the boundary/inclusive-date behavior indirectly).
- Any other duplicated logic in the Analytics module not called out in the brief (e.g., other private helpers in these or other handlers).
- Converting `IMarginCalculator` or `IProductFilterService` to also expose this logic as an injectable service — the brief explicitly proposes a static extension method, not a new DI service, and this spec follows that direction.

## Open Questions
None.

## Status: COMPLETE
