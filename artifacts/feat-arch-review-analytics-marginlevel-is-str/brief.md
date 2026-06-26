## Module
Analytics

## Finding
`MarginLevel` is passed as a raw `string` ("M0", "M1", "M2") throughout the Analytics module, while every other discriminator in the same module uses an enum (`AnalyticsProductType`, `ProductGroupingMode`, `BankStatementDateType`, `ImportDateType`). An invalid string silently falls back to M2 with no compile-time or runtime error:

```csharp
// GetProductMarginSummaryRequest.cs, line 13
public string MarginLevel { get; set; } = "M2"; // M0, M1, M2

// MarginCalculator.cs, lines 116-124
public decimal GetMarginAmountForLevel(AnalyticsProduct product, string marginLevel)
{
    return marginLevel.ToUpperInvariant() switch
    {
        "M0" => product.M0Amount,
        "M1" => product.M1Amount,
        "M2" => product.M2Amount,
        _ => product.M2Amount   // silent fallback on any typo
    };
}
```

Affected files:
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryRequest.cs`, line 13
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/MarginCalculator.cs`, lines 116–124
- `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs` (multiple `marginLevel` pass-throughs)

## Why it matters
A misspelled margin level (e.g. `"m2"` before `.ToUpperInvariant()` was added, or a future code path that skips the normalisation) silently returns M2 data instead of failing. The inconsistency with peer enum types makes the API surface confusing — `ProductGroupingMode` is properly typed but `MarginLevel` is not. It also means the generated TypeScript client accepts any arbitrary string rather than a constrained union type.

## Suggested fix
Add `MarginLevel` enum to the domain alongside `ProductGroupingMode`:
```csharp
// Domain/Features/Analytics/MarginLevel.cs
public enum MarginLevel { M0, M1, M2 }
```
Change `GetProductMarginSummaryRequest.MarginLevel` from `string` to `MarginLevel`, update `IMarginCalculator.GetMarginAmountForLevel` signature to accept `MarginLevel`, and replace the stringly-typed `switch` with a clean enum switch that has no silent default arm. Update the frontend query parameter accordingly.

---
_Filed by daily arch-review routine on 2026-06-07._