## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` lines 222–230:

```csharp
var marginPerUnit = marginLevel.ToUpperInvariant() switch
{
    "M0" => product.M0Amount,
    "M1" => product.M1Amount,
    "M2" => product.M2Amount,
    _ => product.M2Amount
};
```

This is identical logic to `IMarginCalculator.GetMarginAmountForLevel` defined in `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs` lines 113–119:

```csharp
return marginLevel.ToUpperInvariant() switch
{
    "M0" => product.M0Amount,
    "M1" => product.M1Amount,
    "M2" => product.M2Amount,
    _ => product.M2Amount
};
```

`GetProductMarginSummaryHandler` already has `_marginCalculator` injected (line 15) and calls it elsewhere (`CalculateAsync`, `GetGroupDisplayName`, `GetGroupKey`), so it could call `_marginCalculator.GetMarginAmountForLevel(product, marginLevel)` directly instead of reimplementing the switch.

## Why it matters

If a new margin level is introduced (e.g. M3), or the fallback behaviour changes, both sites must be updated. The private `CalculateTotalMarginForLevel` method inside the handler is a leaked concern — margin-level resolution belongs to `IMarginCalculator`, which already owns it.

## Suggested fix

Replace `CalculateTotalMarginForLevel` in the handler with a call to the injected calculator:

```csharp
private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, string marginLevel)
{
    return products.Sum(p =>
    {
        var totalSales = p.SalesHistory.Sum(s => s.AmountB2B + s.AmountB2C);
        return (decimal)totalSales * _marginCalculator.GetMarginAmountForLevel(p, marginLevel);
    });
}
```

The private method body is then a one-liner and the switch is removed entirely.

---
_Filed by daily arch-review routine on 2026-06-03._