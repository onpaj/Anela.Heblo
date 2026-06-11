## Module
Catalog

## Finding
`GetCatalogDetailHandler` declares `ILotsClient` as a constructor dependency (lines 14, 21, 27 of `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`) but the field `_lotsClient` is never called anywhere in the `Handle` method or its private helpers.

Lots are already populated from the `CatalogAggregate` cache via `catalogItem.Stock.Lots` (line 64), so the client is genuinely dead:

```csharp
// injected but never used
private readonly ILotsClient _lotsClient;

// lots come from cache, not from _lotsClient
if (catalogItem.HasLots)
{
    catalogItemDto.Lots = catalogItem.Stock.Lots.Select(...).ToList();
}
```

## Why it matters
YAGNI violation: the dependency inflates the constructor signature, increases DI container wiring complexity, and misleads readers into thinking the handler fetches lots on-demand. It also silently delays detection of a missing `ILotsClient` registration should the DI binding ever be removed.

## Suggested fix
Remove the `ILotsClient` parameter from the constructor and delete the `_lotsClient` field. No behavior changes.

```csharp
// Before
public GetCatalogDetailHandler(
    ICatalogRepository catalogRepository,
    ILotsClient lotsClient,      // ← remove
    IMapper mapper,
    TimeProvider timeProvider,
    ILogger<GetCatalogDetailHandler> logger)

// After
public GetCatalogDetailHandler(
    ICatalogRepository catalogRepository,
    IMapper mapper,
    TimeProvider timeProvider,
    ILogger<GetCatalogDetailHandler> logger)
```

---
_Filed by daily arch-review routine on 2026-05-30._