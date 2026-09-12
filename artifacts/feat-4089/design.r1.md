# Design: Batch Catalog Lookup for Gift Package Manufacture Ingredients

## Component Design

No new components. One interface gains a method, its one implementation implements it, and one call site swaps a loop for a single call.

- **`ILogisticsCatalogSource`** (`Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs`) — Logistics's outbound port to catalog data. Keeps existing `GetCatalogItemAsync(string code, CancellationToken)` unchanged (still used by the out-of-scope `GetTransportBoxByCodeHandler`). Gains:
  ```csharp
  Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
      IReadOnlyList<string> codes, CancellationToken cancellationToken);
  ```
  Responsibility: given a set of product codes, return catalog data for whichever of them exist, keyed by code. Codes with no matching catalog entry are simply absent from the result (no exception, no null placeholder) — same contract as the existing per-code method's "not found → null" behavior, just batched.

- **`LogisticsCatalogSourceAdapter`** (`Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs`, `internal sealed`, constructor-injected `ICatalogRepository`) — implements the new method by delegating to the already-existing `ICatalogRepository.GetByIdsAsync(codes, cancellationToken)` and mapping each returned `CatalogAggregate` through the existing private `ToCatalogItem` helper (unchanged), mirroring `CatalogPackingProductSourceAdapter.GetByCodesAsync`. No new dependencies, no DI registration change (already registered as `ILogisticsCatalogSource` in `CatalogModule.cs:58`).
  ```csharp
  public async Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
      IReadOnlyList<string> codes,
      CancellationToken cancellationToken)
  {
      var aggregates = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
      return aggregates.ToDictionary(kv => kv.Key, kv => ToCatalogItem(kv.Value));
  }
  ```

- **`GiftPackageManufactureService.GetGiftPackageDetailAsync`** (`Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`, lines 111–118) — the sole ingredient-resolution consumer. Replaces the per-code `foreach` loop calling `GetCatalogItemAsync` with one call to `GetCatalogItemsAsync(ingredientCodes, cancellationToken)`. `ingredientCodes` (already `.Distinct()`-ed) is passed through unchanged. The downstream `foreach (var part in productParts)` loop is untouched — it already reads the catalog dictionary via `TryGetValue`, so it behaves identically whether the dictionary came from a loop of single calls or one batch call, including the "code absent → `AvailableStock = 0`, `Image = null`" fallback. `CreateManufactureAsync` and `DisassembleGiftPackageAsync` need no changes — they inherit the fix by calling `GetGiftPackageDetailAsync` internally.

Data flow (unchanged shape, fewer calls):

```
GiftPackageManufactureService.GetGiftPackageDetailAsync
    → BOM lookup (_manufactureClient.GetSetPartsAsync)          [unchanged]
    → ingredientCodes = parts.Select(ProductCode).Distinct()    [unchanged]
    → _catalogSource.GetCatalogItemsAsync(ingredientCodes, ct)  [NEW — replaces N x GetCatalogItemAsync]
        → LogisticsCatalogSourceAdapter.GetCatalogItemsAsync
            → _catalogRepository.GetByIdsAsync(codes, ct)       [pre-existing, unchanged]
            → ToDictionary(ToCatalogItem)                       [reuses existing private mapper]
    → foreach part: ingredientCatalog.TryGetValue(...)          [unchanged]
```

## Data Schemas

No persisted schema changes. Reused/affected in-memory shapes only:

- **`LogisticsCatalogItem`** — unchanged value type: `ProductCode` (string), `Image` (string?), `EshopStock`, `AvailableStock`.
- **New in-memory return shape**: `IReadOnlyDictionary<string, LogisticsCatalogItem>`, keyed by product code, produced by the new `GetCatalogItemsAsync` method. Same convention as `IManufactureCatalogSource.GetByIdsAsync`, `IMaterialCatalogService.GetByIdsAsync`, and `IPackingProductSource.GetByCodesAsync`. A code with no matching `CatalogAggregate` is simply absent from the dictionary — not an entry with a null/default value.
- No new request/response DTOs: this sits below the MediatR handler / controller boundary. `GiftPackageDto` and `GiftPackageIngredientDto` — and every HTTP-facing contract — are unaffected.
- No event payloads are introduced or changed.

Test data shape (for FR-4): existing unit tests mock `_catalogSourceMock` against `GetCatalogItemAsync`; these must instead set up `GetCatalogItemsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>())` to return a `Dictionary<string, LogisticsCatalogItem>` (or a partial one, omitting a code, to preserve the "missing ingredient" test case), and assert it is invoked `Times.Once` per `GetGiftPackageDetailAsync` call rather than once per ingredient.
