## Module
FinancialOverview

## Finding
`PlaceholderStockValueService` (`backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/PlaceholderStockValueService.cs`) carries XML documentation claiming:

> *"Automatically injected in Test and Test environments via FinancialOverviewModule."*

But `FinancialOverviewModule.cs:19-25` unconditionally registers `StockValueService` — there is no environment check and no path that registers `PlaceholderStockValueService`. The class is never bound in DI and is therefore unreachable dead code.

The same block in `FinancialOverviewModule.cs` uses an unnecessary manual factory instead of standard DI:

```csharp
// Current — manual factory, comment says "tests can override this"
services.AddScoped<IStockValueService>(provider =>
{
    var stockClient = provider.GetRequiredService<IErpStockClient>();
    var priceClient = provider.GetRequiredService<IProductPriceErpClient>();
    var logger = provider.GetRequiredService<ILogger<StockValueService>>();
    return new StockValueService(stockClient, priceClient, logger);
});
```

The factory comment implies the intent was to support test-time injection of the placeholder, but since it never happens, the factory adds complexity for no gain.

## Why it matters
- Dead code misleads future readers about what happens in test environments.
- If someone actually needs a test placeholder, the misleading docs will send them looking for registration logic that does not exist.
- The manual factory means any new constructor dependency on `StockValueService` must also be manually added here (easily forgotten).

## Suggested fix
1. Delete `PlaceholderStockValueService.cs`.
2. Simplify the module registration to:

```csharp
services.AddScoped<IStockValueService, StockValueService>();
```

If a real test placeholder is needed in future, register it in the test project's service overrides — not in production module code.

---
_Filed by daily arch-review routine on 2026-06-06._