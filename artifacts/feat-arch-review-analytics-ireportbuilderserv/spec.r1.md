# Specification: Decouple IReportBuilderService from UseCase Response Types

## Summary
The `IReportBuilderService` interface in `Features/Analytics/Services/` currently returns nested classes defined inside two specific UseCase response objects, inverting the intended dependency direction (Services should be depended on by UseCases, not the other way around). This refactor introduces neutral DTOs in `Features/Analytics/Contracts/`, updates the service interface and implementation to use them, and adds one-line projections inside each UseCase handler. Behavior, response shapes, API contracts, and tests semantics remain unchanged.

## Background
Per `docs/architecture/filesystem.md` and `docs/architecture/development_guidelines.md`, the `Services/` folder holds domain services and business logic shared across use cases, while `UseCases/` contains per-request handlers with their request/response shapes. The `Contracts/` folder is the canonical home for shared DTOs (it already contains `AnalysisMarginData`, `TopProductDto`, `MonthlyProductMarginDto`, `ProductMarginSegmentDto`).

Today, `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` imports `Features.Analytics.UseCases.GetMarginReport` and `Features.Analytics.UseCases.GetProductMarginAnalysis` and references three nested types on those responses:

- `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown`
- `GetMarginReportResponse.CategoryMarginSummary`
- `GetMarginReportResponse.ProductMarginSummary`

This produces three concrete problems:
1. The `Services` namespace has a compile-time dependency on `UseCases` — the wrong direction.
2. A future use case that needs the same computation cannot consume the service without taking a semantically-wrong type name from an unrelated use case.
3. Renaming or restructuring either response forces a change to the shared service.

This refactor was filed by the daily arch-review routine on 2026-05-27.

## Functional Requirements

### FR-1: Introduce shared contract DTOs
Add three new public classes (not records — per the project rule that DTOs must be classes) in `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/`, each in its own file:

- `MonthlyMarginBreakdownDto.cs`
- `CategoryMarginSummaryDto.cs`
- `ProductMarginSummaryDto.cs`

Field shapes mirror the existing nested types exactly (same names, same types, same defaults).

**Acceptance criteria:**
- File `Contracts/MonthlyMarginBreakdownDto.cs` exists with public class `MonthlyMarginBreakdownDto` containing exactly: `DateTime Month`, `decimal MarginAmount`, `decimal Revenue`, `decimal Cost`, `int UnitsSold`.
- File `Contracts/CategoryMarginSummaryDto.cs` exists with public class `CategoryMarginSummaryDto` containing exactly: `string Category` (default `string.Empty`), `decimal TotalMargin`, `decimal TotalRevenue`, `decimal AverageMarginPercentage`, `int ProductCount`, `int TotalUnitsSold`.
- File `Contracts/ProductMarginSummaryDto.cs` exists with public class `ProductMarginSummaryDto` containing exactly the 15 properties present today on `GetMarginReportResponse.ProductMarginSummary` (`ProductId`, `ProductName`, `Category`, `MarginAmount`, `M0Amount`, `M1Amount`, `M2Amount`, `M0Percentage`, `M1Percentage`, `M2Percentage`, `SellingPrice`, `PurchasePrice`, `MarginPercentage`, `Revenue`, `Cost`, `UnitsSold`) with identical types and string defaults.
- Namespace of all three is `Anela.Heblo.Application.Features.Analytics.Contracts`.
- All three classes compile and are publicly accessible.

### FR-2: Update `IReportBuilderService` to return Contract DTOs
Change the interface in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/ReportBuilderService.cs` so that signatures reference only `Contracts/` and `Domain/` types — no `UseCases/` imports remain.

**Acceptance criteria:**
- `BuildMonthlyBreakdown(...)` returns `List<MonthlyMarginBreakdownDto>`.
- `BuildCategorySummaries(...)` returns `List<CategoryMarginSummaryDto>`.
- `BuildProductSummary(...)` returns `ProductMarginSummaryDto`.
- File `Services/ReportBuilderService.cs` no longer contains any `using Anela.Heblo.Application.Features.Analytics.UseCases.*` directives.
- The implementation constructs and returns the new DTOs with field-for-field copies of the values it currently produces — no logic changes to margin/revenue/cost computations or sort orders.

### FR-3: Map Contract DTOs to UseCase response types in handlers
Each call site in the two UseCase handlers must project from the new contract DTO to the existing nested response type so that public API/response shapes remain identical.

Sites that must change:
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — calls to `BuildProductSummary` and `BuildCategorySummaries`, plus the internal `ReportData` class collections.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — assignment of `response.MonthlyBreakdown` from `BuildMonthlyBreakdown`.

**Acceptance criteria:**
- Each call to a builder method is followed by (or wrapped in) a projection from `*Dto` to the corresponding nested response type via either object-initializer mapping or a small private static helper in the handler file.
- `GetMarginReportHandler.ProductSummaries` (the response field) remains `List<GetMarginReportResponse.ProductMarginSummary>` and `CategorySummaries` remains `List<GetMarginReportResponse.CategoryMarginSummary>` — no public response shape changes.
- `GetProductMarginAnalysisHandler.Response.MonthlyBreakdown` remains `List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown>`.
- The internal helper class `ReportData` (defined at the bottom of `GetMarginReportHandler.cs`) is updated consistently: either it holds `*Dto` lists and the final response builder maps once at the end, or it continues to hold nested response types and the per-product mapping happens inside the loop. Implementer chooses whichever produces fewer mapping calls; both are acceptable as long as no public type leaks change.
- The internal sort `OrderByDescending(p => p.M2Percentage)` in `GetMarginReportHandler.ProcessProductsForReport` continues to operate on objects that expose `M2Percentage` (works on either the DTO or the nested type since both have the property).

### FR-4: Preserve all existing behavior
The refactor is strictly mechanical. No business logic, validation, error handling, ordering, rounding, or response field semantics may change.

**Acceptance criteria:**
- `GetMarginReportHandlerTests.cs` passes without test edits to assertions about response contents.
- `GetProductMarginAnalysisHandlerTests.cs` passes without test edits to assertions about response contents.
- Any test that previously referenced `GetMarginReportResponse.ProductMarginSummary`, `GetMarginReportResponse.CategoryMarginSummary`, or `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` as the return type of an `IReportBuilderService` method must be updated to reference the new DTO type; assertion values remain identical.
- `dotnet build` succeeds.
- `dotnet format` reports no remaining changes.
- No OpenAPI-generated TypeScript client diff outside of expected no-op (the response classes the API exposes do not change).

### FR-5: No DI / registration changes
`ReportBuilderService` is registered the same way it is today in `AnalyticsModule.cs`. The service implementation class name and namespace do not change.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` is unchanged.
- `IReportBuilderService` is still bound to `ReportBuilderService` with the same lifetime.

## Non-Functional Requirements

### NFR-1: Performance
The change introduces at most one additional allocation per item already produced (the projection from DTO to nested response type). For the workloads in scope (single-product monthly breakdowns up to ~36 months; margin reports capped at `MaxProducts`), the overhead is negligible. No measurable regression in handler runtime is permitted (>5% would warrant investigation).

### NFR-2: Architectural integrity
After the refactor:
- `Features/Analytics/Services/` contains zero references to `Features/Analytics/UseCases/`.
- `Features/Analytics/Contracts/` contains zero references to `Features/Analytics/UseCases/`.
- Dependency arrows point inward: `UseCases → Services → Contracts → Domain`.

### NFR-3: Code style
- DTOs follow the existing style of `Contracts/TopProductDto.cs` (public class, no record, settable auto-properties, `string` defaults `= string.Empty`).
- One DTO per file. File name matches class name.
- No XML-doc comments are required (existing contract DTOs do not have them).

## Data Model
No domain or database changes. Three new application-layer DTOs:

| DTO | Mirrors | Fields |
|---|---|---|
| `MonthlyMarginBreakdownDto` | `GetProductMarginAnalysisResponse.MonthlyMarginBreakdown` | `Month`, `MarginAmount`, `Revenue`, `Cost`, `UnitsSold` |
| `CategoryMarginSummaryDto` | `GetMarginReportResponse.CategoryMarginSummary` | `Category`, `TotalMargin`, `TotalRevenue`, `AverageMarginPercentage`, `ProductCount`, `TotalUnitsSold` |
| `ProductMarginSummaryDto` | `GetMarginReportResponse.ProductMarginSummary` | `ProductId`, `ProductName`, `Category`, `MarginAmount`, `M0Amount`, `M1Amount`, `M2Amount`, `M0Percentage`, `M1Percentage`, `M2Percentage`, `SellingPrice`, `PurchasePrice`, `MarginPercentage`, `Revenue`, `Cost`, `UnitsSold` |

The existing nested response types remain on `GetMarginReportResponse` and `GetProductMarginAnalysisResponse` and continue to define the public HTTP/OpenAPI surface.

## API / Interface Design

### Internal interface change
```csharp
// before
public interface IReportBuilderService
{
    List<GetProductMarginAnalysisResponse.MonthlyMarginBreakdown> BuildMonthlyBreakdown(...);
    List<GetMarginReportResponse.CategoryMarginSummary> BuildCategorySummaries(...);
    GetMarginReportResponse.ProductMarginSummary BuildProductSummary(...);
}

// after
public interface IReportBuilderService
{
    List<MonthlyMarginBreakdownDto> BuildMonthlyBreakdown(...);
    List<CategoryMarginSummaryDto> BuildCategorySummaries(...);
    ProductMarginSummaryDto BuildProductSummary(...);
}
```

### External HTTP API
Unchanged. The MediatR responses `GetMarginReportResponse` and `GetProductMarginAnalysisResponse` retain their existing nested types and field shapes; OpenAPI spec is untouched.

### Handler-side mapping pattern
A simple object-initializer projection inside each handler. Example (illustrative, not normative):

```csharp
var dto = _reportBuilderService.BuildProductSummary(product, marginData);
var productSummary = new GetMarginReportResponse.ProductMarginSummary
{
    ProductId = dto.ProductId,
    ProductName = dto.ProductName,
    // ... 1:1 field copy
};
```

Implementer may extract this into a private static `ToResponse(dto)` method per handler if it improves readability.

## Dependencies
- .NET 8 SDK and existing project references — no new packages.
- No frontend changes — the OpenAPI-generated TypeScript client is unchanged.
- No infrastructure, configuration, or DI registration changes.
- Touches existing test files only insofar as they declare the old nested return types for `IReportBuilderService` methods.

## Out of Scope
- Renaming the existing nested response types.
- Splitting or restructuring `GetMarginReportResponse` / `GetProductMarginAnalysisResponse`.
- Changing margin/revenue/cost computation logic, ordering, or rounding.
- Introducing AutoMapper or any mapping library — manual projection is sufficient and matches existing handler style.
- Replacing the nested response types with the new DTOs at the HTTP boundary (would be a breaking API change).
- Touching other Analytics services (e.g., `IProductFilterService`) or other features.
- Adding new use cases that consume the refactored service.
- Adding XML documentation comments to the new DTOs.

## Open Questions
None.

## Status: COMPLETE