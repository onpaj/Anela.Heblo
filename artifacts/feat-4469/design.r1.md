# Design: Decouple IStockAnalysisCalculator from use case

## Component Design
- `Contracts/StockAnalysisItemDto`, `StockAnalysisSummaryDto`, `LastPurchaseInfoDto`: verbatim moves of the existing classes (namespace `Anela.Heblo.Application.Features.Purchase.Contracts`), one per file.
- `Contracts/StockAnalysisFilter`: input value for filtering analyzed items.
- `IStockAnalysisCalculator.FilterItems(List<StockAnalysisItemDto> items, StockAnalysisFilter filter)`; `StockAnalysisCalculator.ShouldIncludeItem(item, filter)` keeps the existing logic (`OnlyConfigured && !IsConfigured` excludes; `StockStatus` switch on `Severity`, default includes).
- `GetPurchaseStockAnalysisHandler` maps request -> filter right before the call.
- The `Services` folder must not reference `UseCases` namespaces.

## Data Schemas
```csharp
namespace Anela.Heblo.Application.Features.Purchase.Contracts;
public class StockAnalysisFilter
{
    public bool OnlyConfigured { get; set; }
    public StockStatusFilter StockStatus { get; set; } = StockStatusFilter.All;
}
```
DTO schemas are unchanged (see current `GetPurchaseStockAnalysisResponse.cs`). No API, DB or event changes.
