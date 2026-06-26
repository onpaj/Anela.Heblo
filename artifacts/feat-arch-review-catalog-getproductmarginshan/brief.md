## Module
Catalog

## Finding
`GetProductMarginsHandler.MapToMarginDto` (line 189 of `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`) hardcodes `DateTime.Now` to compute the 13-month history window:

```csharp
var dateFrom = DateTime.Now.AddMonths(-13);
```

The handler does not inject `TimeProvider` at all. Every other handler in this module that needs the current time uses the injected `TimeProvider` (e.g. `GetCatalogDetailHandler`, `CatalogRepository`, `MarginCalculationService`).

## Why it matters
- **Testability**: unit tests cannot control `DateTime.Now`, making time-sensitive assertions brittle or impossible.
- **Consistency**: the codebase-wide pattern is `TimeProvider.GetUtcNow()`. This is the only place in the Catalog module that deviates.
- **Correctness edge case**: `DateTime.Now` returns local time while the rest of the codebase uses UTC, which can produce off-by-one month errors around midnight in non-UTC timezones.

## Suggested fix
Inject `TimeProvider` and replace `DateTime.Now`:

```csharp
// Constructor - add TimeProvider
public GetProductMarginsHandler(
    ICatalogRepository catalogRepository,
    TimeProvider timeProvider,
    ILogger<GetProductMarginsHandler> logger)

// MapToMarginDto - replace DateTime.Now
var dateFrom = _timeProvider.GetUtcNow().DateTime.AddMonths(-13);
```

---
_Filed by daily arch-review routine on 2026-05-30._