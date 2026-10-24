# Design: Manufacture-owned ProductCatalogSnapshot

## Component Design

All new types live in namespace `Anela.Heblo.Application.Features.Manufacture.Contracts`, one type per file, plain classes with `init` setters (no records), no reference to `Anela.Heblo.Domain.Features.Catalog.*`.

- `ProductCatalogSnapshot` - the projection Manufacture reads. Computed members: `ProductFamily` (first `min(6, len)` chars of code), `SizeCode` (substring from `min(6, len)`), `LastStockTaking` (max `Date` of `StockTakingHistory`, null if empty). Must be unit-tested against `CatalogAggregate` for equality of results.
- `ProductStockSnapshot` - Total, Available, Planned, Erp, Eshop, Transport, Reserve, Quarantine, Manufactured, PrimaryStockSource (as the Manufacture-side mirror enum or string, whichever the compiler shows is consumed), Lots only if consumed.
- `ProductPropertiesSnapshot` - ExpirationMonths (default 12), OptimalStockDaysSetup, StockMinSetup, BatchSize, AllowedResiduePercentage.
- `ProductSalesRecordSnapshot` - Date, AmountB2B, AmountB2C.
- `ProductConsumptionRecordSnapshot` - Date, Amount.
- `ProductStockTakingSnapshot` - Id, Type (`StockTakingKind`), Code, AmountNew, AmountOld, Date, User, Error.
- `ProductKind` enum - mirrors `ProductType` values by name (UNDEFINED, Product, Goods, Material, SemiProduct, Set, ...; take the exact list from `Domain/Features/Catalog/ProductType.cs`). `StockTakingKind` enum - Erp, Eshop.
- `CatalogAggregateSnapshotMapper` (internal static, `Features/Catalog/Infrastructure/`) - `ProductCatalogSnapshot ToSnapshot(CatalogAggregate)`, enum mapping by explicit switch (throw on unknown so new Catalog enum values fail loudly in tests), `StockTakingRecord ToDomain(ProductStockTakingSnapshot)` for the stock sync adapter.
- `IManufactureCatalogSource` - return types swapped; no Catalog using.
- `IManufactureCatalogStockSync.SyncErpStockTakingAsync(string productCode, decimal newStockAmount, ProductStockTakingSnapshot record, CancellationToken ct = default)`; Catalog adapter calls existing cache patch then appends the mapped record to the cached aggregate's `StockTakingHistory` (idempotent on record Id).
- Consumers: replace `CatalogAggregate` with `ProductCatalogSnapshot` in signatures; `IConsumptionRateCalculator` takes `IEnumerable<ProductSalesRecordSnapshot>` / `IEnumerable<ProductConsumptionRecordSnapshot>`; `GetManufactureStockTakingHistoryHandler` maps from `ProductStockTakingSnapshot`; `ManufactureStockTakingHistoryItemDto.Type` keeps its public wire type unchanged (it currently exposes `StockTakingType`; to keep the OpenAPI/TS client byte-identical, either keep that DTO property typed as the Catalog enum and leave that allowlist entry, or switch to `StockTakingKind` only if the generated enum name and values are identical). Default: leave `ManufactureStockTakingHistoryItemDto -> StockTakingType` as is, mapping `StockTakingKind` -> `StockTakingType` in the handler, and keep that single allowlist entry with an explanatory comment.
- Tests: `ProductCatalogSnapshotBuilder` (tests project) with defaults equal to `new CatalogAggregate()` defaults; mapper tests; existing Manufacture tests switched to the builder.

## Data Schemas

No database, API request/response or event payload changes. The only schema is the in-memory snapshot:

```csharp
public class ProductCatalogSnapshot
{
    public required string ProductCode { get; init; }
    public string ProductName { get; init; } = "";
    public string ProductNameNormalized { get; init; } = "";
    public ProductKind Type { get; init; }
    public double MinimalManufactureQuantity { get; init; }
    public double? NetWeight { get; init; }
    public bool HasLots { get; init; }
    public ProductStockSnapshot Stock { get; init; } = new();
    public ProductPropertiesSnapshot Properties { get; init; } = new();
    public IReadOnlyList<ProductSalesRecordSnapshot> SalesHistory { get; init; } = [];
    public IReadOnlyList<ProductConsumptionRecordSnapshot> ConsumedHistory { get; init; } = [];
    public IReadOnlyList<ProductStockTakingSnapshot> StockTakingHistory { get; init; } = [];
    public string? ProductFamily => ...;
    public string? SizeCode => ...;
    public DateTime? LastStockTaking => ...;
}
```
Members shown are the starting set from the code survey; add or drop members strictly according to what compiles against real usage (e.g. `ProductNameNormalized`, `Stock.*` subfields).
