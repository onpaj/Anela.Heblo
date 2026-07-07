# Design: Remove `ProductMarginSegmentDto` Backward-Compatibility Alias Properties

## Component Design

No new or restructured components. One existing DTO loses six computed, read-only alias properties; its producer and consumer are unaffected because neither touches those aliases today.

- **`ProductMarginSegmentDto`** (`backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs`) — remove the `// Keep for backward compatibility` comment and the six alias getters (`ProductCode`, `ProductName`, `MarginPerPiece`, `SellingPriceWithoutVat`, `MaterialCosts`, `LaborCosts`, lines 20–26). All twelve canonical properties (`GroupKey`, `DisplayName`, `MarginContribution`, `Percentage`, `ColorCode`, `IsOther`, `AverageMarginPerPiece`, `UnitsSold`, `AverageSellingPriceWithoutVat`, `AverageMaterialCosts`, `AverageLaborCosts`, `ProductCount`) and their responsibility (one row of aggregated monthly margin-segment data for the analytics chart) are unchanged.
- **`MonthlyBreakdownGenerator.GenerateMonthlySegments`** — producer, already sets only canonical properties; no change required.
- **`ProductMarginSummary.tsx`** — frontend consumer, already reads only canonical `average*`/`groupKey`/`displayName` fields; no change required.
- **`ProductMarginSummary.test.tsx`** — the `productSegments` fixture (lines 31–58) is renamed field-by-field to canonical names for consistency with the DTO shape (not required for tests to pass, since it's `any`-cast and the tooltip callback that reads these fields is never invoked under the mocked `Chart` component).
- **`api-client.ts`** (generated) — regenerated via `npm run generate-client`; never hand-edited. No manual component change.

No new interfaces, endpoints, services, or dependencies are introduced.

## Data Schemas

`ProductMarginSegmentDto` (C#) after the change — the wire shape returned wherever this DTO is serialized (the product-margin-summary analytics endpoint's monthly segment list):

```csharp
public class ProductMarginSegmentDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal MarginContribution { get; set; }
    public decimal Percentage { get; set; }
    public string ColorCode { get; set; } = string.Empty;
    public bool IsOther { get; set; } = false;

    public decimal AverageMarginPerPiece { get; set; }
    public int UnitsSold { get; set; }
    public decimal AverageSellingPriceWithoutVat { get; set; }
    public decimal AverageMaterialCosts { get; set; }
    public decimal AverageLaborCosts { get; set; }
    public int ProductCount { get; set; }
}
```

The corresponding generated TypeScript (`ProductMarginSegmentDto` class and `IProductMarginSegmentDto` interface in `frontend/src/api/generated/api-client.ts`) drops the same six fields (`productCode`, `productName`, `marginPerPiece`, `sellingPriceWithoutVat`, `materialCosts`, `laborCosts`) from both its property declarations and its `toJSON`/`fromJS` (de)serialization logic, mirroring whatever NSwag emits for the twelve remaining canonical fields.

No database schema, migration, or persisted entity is affected — this DTO is not persisted. No route, HTTP verb, or request contract changes; only the response payload's field set shrinks.
