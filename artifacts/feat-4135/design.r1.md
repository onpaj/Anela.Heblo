# Design: Inject TimeProvider into GetPurchaseStockAnalysisHandler

## Component Design

**`GetPurchaseStockAnalysisHandler`** (`Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`)
Implements `IRequestHandler<GetPurchaseStockAnalysisRequest, GetPurchaseStockAnalysisResponse>`. Gains one new constructor dependency, `TimeProvider`, appended as the last parameter (after `ILogger<GetPurchaseStockAnalysisHandler> logger`), mirroring `CreatePurchaseOrderHandler`. Stored as `private readonly TimeProvider _timeProvider`.

Responsibility change: the handler's "resolve default date range when caller omits `FromDate`/`ToDate`" responsibility now sources the current instant from the injected `TimeProvider` instead of the static `DateTime.UtcNow`, making that path deterministic under test. All other responsibilities (validation, snapshot retrieval via `IMaterialCatalogService`, severity/summary calculation via `IStockSeverityCalculator`/`IStockAnalysisCalculator`, filtering, sorting, pagination) are unchanged.

In `Handle`, the two independent `DateTime.UtcNow` reads are replaced by a single read:
```csharp
var now = _timeProvider.GetUtcNow().UtcDateTime;
var fromDate = request.FromDate ?? now.AddYears(-1);
var toDate = request.ToDate ?? now;
```

**Resolution:** `TimeProvider` continues to resolve from the existing `services.AddSingleton(TimeProvider.System)` registration in `ServiceCollectionExtensions.cs` — no DI change needed. In tests, `GetPurchaseStockAnalysisHandlerTests` constructs the handler directly and must supply a `Mock<TimeProvider>` with `GetUtcNow()` stubbed to a fixed `DateTimeOffset`, matching the pattern in `CreatePurchaseOrderHandlerTests`.

No new components, interfaces, or files are introduced; no other handler, controller, or DTO is touched.

## Data Schemas

No data schema, DTO, or MediatR contract changes. `GetPurchaseStockAnalysisRequest` and `GetPurchaseStockAnalysisResponse` are unchanged; only the internal computation of the `fromDate`/`toDate` fallback values (and the `Summary.AnalysisPeriodStart`/`AnalysisPeriodEnd` values derived from them) changes its time source, not its shape or semantics.
