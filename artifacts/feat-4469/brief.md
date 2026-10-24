## Module
Purchase

## Finding
`IStockAnalysisCalculator.FilterItems` (and its implementation `StockAnalysisCalculator.FilterItems`) takes `GetPurchaseStockAnalysisRequest` as a parameter — a type that lives in the `UseCases/GetPurchaseStockAnalysis/` namespace:

- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs:2` — `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockAnalysisCalculator.cs:46` — `List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request);`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockAnalysisCalculator.cs:120` — `public List<StockAnalysisItemDto> FilterItems(List<StockAnalysisItemDto> items, GetPurchaseStockAnalysisRequest request)`

Additionally, three response-shape DTOs that the service interface returns are defined inside the use-case folder rather than `Contracts/`:
- `StockAnalysisItemDto` — `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs:22`
- `StockAnalysisSummaryDto` — `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs:78`
- `LastPurchaseInfoDto` — `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs:65`

## Why it matters
The intended direction of dependency is: **use cases depend on services, not the reverse**. By importing a use-case request into the service interface, `IStockAnalysisCalculator` is permanently tethered to the one use case that owns `GetPurchaseStockAnalysisRequest`. Any future use case that wants to call `FilterItems` (e.g. an export endpoint, a dashboard tile, or a background job) would have to import an unrelated use-case namespace to satisfy the contract — which is the pattern issue #4105 caught in Leaflet (`LeafletDocumentSummary in use-case folder`). The same placement also prevents the `StockAnalysisItemDto` types from appearing in the generated OpenAPI client as re-usable contracts, since they live under a private use-case namespace.

## Suggested fix
1. Move `StockAnalysisItemDto`, `StockAnalysisSummaryDto`, and `LastPurchaseInfoDto` from `UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` into three separate files in `Contracts/` (matching the pattern of `PurchaseOrderSummaryDto.cs`, `SupplierDto.cs`, etc.).
2. Create a small `StockAnalysisFilter` value object in `Contracts/` containing `OnlyConfigured` and `StockStatus` — the two fields from `GetPurchaseStockAnalysisRequest` that `FilterItems` actually reads — and replace the `GetPurchaseStockAnalysisRequest` parameter with it.
3. Update `GetPurchaseStockAnalysisHandler` to construct a `StockAnalysisFilter` from the request before calling `FilterItems`. No other callers change.

---
_Filed by daily arch-review routine on 2026-10-11._