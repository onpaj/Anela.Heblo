## Module
Purchase

## Finding
`GetPurchaseStockAnalysisHandler` reads the current UTC time via `DateTime.UtcNow` at lines 33–34 to compute default date-range boundaries:

```csharp
// backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs:33-34
var fromDate = request.FromDate ?? DateTime.UtcNow.AddYears(-1);
var toDate   = request.ToDate   ?? DateTime.UtcNow;
```

The same module's `CreatePurchaseOrderHandler` correctly injects `TimeProvider` and calls `_timeProvider.GetUtcNow()`:

```csharp
// CreatePurchaseOrderHandler.cs:64
var now = _timeProvider.GetUtcNow();
```

`GetPurchaseStockAnalysisHandler` does not inject `TimeProvider` at all.

## Why it matters
- **Untestable**: `DateTime.UtcNow` cannot be substituted in unit tests. Any test that verifies the default date-window logic must run at a specific real wall-clock time or is forced to pass non-null dates on every call, which leaves the default-fallback path untested. `GetPurchaseStockAnalysisHandlerTests.cs` already exists — this gap affects its coverage.
- **Inconsistency within the module**: two handlers in the same module follow different patterns for the same concern, adding cognitive overhead and increasing the chance of future drift (e.g. a contributor adding a third handler and not knowing which pattern to follow).

## Suggested fix
Inject `TimeProvider` into `GetPurchaseStockAnalysisHandler` and replace the two direct calls:

```csharp
// Constructor
private readonly TimeProvider _timeProvider;

public GetPurchaseStockAnalysisHandler(
    IMaterialCatalogService materialCatalog,
    IStockSeverityCalculator stockSeverityCalculator,
    IStockAnalysisCalculator stockAnalysisCalculator,
    ILogger<GetPurchaseStockAnalysisHandler> logger,
    TimeProvider timeProvider)
{
    // ...
    _timeProvider = timeProvider;
}

// Handle method
var now      = _timeProvider.GetUtcNow().UtcDateTime;
var fromDate = request.FromDate ?? now.AddYears(-1);
var toDate   = request.ToDate   ?? now;
```

`TimeProvider` is already registered in DI via `services.AddSingleton(TimeProvider.System)` (standard ASP.NET Core pattern), so no DI registration change is required.

---
_Filed by daily arch-review routine on 2026-09-11._
