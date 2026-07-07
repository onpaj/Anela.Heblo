# Implementation: extract-has-sales-in-period-extension

## What was implemented
Extracted the duplicated private `HasSalesInPeriod` method from `GetMarginReportHandler` and `GetProductMarginAnalysisHandler` into a single public extension method `AnalyticsProductExtensions.HasSalesInPeriod` on `AnalyticsProduct`, in the Domain layer, following the existing `*Extensions` convention (`CarrierExtensions`, `CurrentUserExtensions`, `ManufactureOrderExtensions`). Both handlers now call `product.HasSalesInPeriod(...)` via extension-method syntax and no longer define their own copy of the check. No behavior change — the body is logically identical to both removed private methods.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs` — new file, single static extension method `HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate)`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsProductExtensionsTests.cs` — new file, 7 unit tests covering in-range, boundary (start/end inclusive), before-range, after-range, empty history, and mixed-history cases.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — call site updated to `product.HasSalesInPeriod(startDate, endDate)`; private `HasSalesInPeriod` method removed.
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — call site updated to `productData.HasSalesInPeriod(request.StartDate, request.EndDate)`; private `HasSalesInPeriod` method removed.

## Tests
- `AnalyticsProductExtensionsTests` (7 new tests) — exercises the extension method directly, including both inclusive boundaries.
- `GetMarginReportHandlerTests` and `GetProductMarginAnalysisHandlerTests` — existing tests, unmodified, still pass (confirms no behavior regression from the refactor).

## How to verify
```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Analytics"
dotnet format Anela.Heblo.sln --verify-no-changes
```
Build succeeds with 0 errors (pre-existing unrelated warnings only). All 104 tests under `Features.Analytics` pass, including the 7 new extension tests. `dotnet format --verify-no-changes` reports no changes needed.

## Notes
No deviations from the task plan. No `using` statement changes were needed in either handler — both already imported `Anela.Heblo.Domain.Features.Analytics`.

## PR Summary
Two handlers in the Analytics module (`GetMarginReportHandler` and `GetProductMarginAnalysisHandler`) each defined a bit-for-bit identical private `HasSalesInPeriod` method, differing only in a parameter name. This duplication was invisible to any consumer (both methods were private) and risked silent divergence if date-range semantics ever changed in one handler but not the other.

The fix extracts the shared logic into a new `AnalyticsProductExtensions.HasSalesInPeriod` static extension method on `AnalyticsProduct` in the Domain layer — matching an existing codebase convention (`CarrierExtensions`, `CurrentUserExtensions`, `ManufactureOrderExtensions`). Both handlers now call the extension method and no longer carry their own copy. This is a pure refactor: no behavior change, verified by the existing handler test suites passing unmodified plus 7 new direct unit tests on the extension method covering inclusive boundary dates and edge cases.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs` — new extension method
- `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsProductExtensionsTests.cs` — new unit tests
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs` — use extension, remove duplicate
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs` — use extension, remove duplicate

## Status
DONE
