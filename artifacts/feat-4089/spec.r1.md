# Specification: Batch Catalog Lookup for Gift Package Manufacture Ingredients

## Summary
`GiftPackageManufactureService.GetGiftPackageDetailAsync` currently resolves ingredient catalog data with one `ILogisticsCatalogSource.GetCatalogItemAsync` call per distinct ingredient code, an N+1 pattern that also taxes `CreateManufactureAsync` and `DisassembleGiftPackageAsync` since both call through `GetGiftPackageDetailAsync`. This spec adds a batch lookup method to `ILogisticsCatalogSource`, implements it in `LogisticsCatalogSourceAdapter` on top of the catalog repository's existing bulk lookup, and replaces the per-item loop with a single call.

## Background
`ICatalogRepository` already exposes `GetByIdsAsync(IEnumerable<string> ids, ...)`, explicitly documented as "Bulk lookup — eliminates N+1 query patterns," and several other adapters in the same codebase (`CatalogPackingProductSourceAdapter`, `PurchaseMaterialCatalogAdapter`, `CatalogManufactureCatalogSourceAdapter`) already delegate to it to expose a batch lookup on their respective source interfaces. `ILogisticsCatalogSource` is the outlier: it only exposes the single-item `GetCatalogItemAsync`, forcing `GiftPackageManufactureService` (and, separately, `GetTransportBoxByCodeHandler`, out of scope here) to fetch ingredients one at a time in a loop. Because `_catalogRepository` is an in-memory/cached read-only repository (not a live Shoptet call), the immediate cost today is repeated dictionary/collection lookups rather than network round-trips, but the interface contract does not guarantee that, and the arch-review finding is correct that the shape is N+1 regardless of the backing implementation. Fixing it at the interface level closes off the risk and matches the established pattern already used elsewhere in `Catalog/Infrastructure`.

## Functional Requirements

### FR-1: Add a batch method to `ILogisticsCatalogSource`
Add the following member to `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs`:

```csharp
Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
    IReadOnlyList<string> codes, CancellationToken cancellationToken);
```

The existing `GetCatalogItemAsync(string code, ...)` member is kept unchanged for callers that only ever need one code (e.g. `GetTransportBoxByCodeHandler`, unless a future change migrates it too).

**Acceptance criteria:**
- The interface compiles with both the existing single-item method and the new batch method present.
- The new method's signature matches exactly: takes `IReadOnlyList<string> codes` and a `CancellationToken`, returns `Task<IReadOnlyDictionary<string, LogisticsCatalogItem>>`.

### FR-2: Implement the batch method in `LogisticsCatalogSourceAdapter`
Implement `GetCatalogItemsAsync` in `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs` by delegating to `_catalogRepository.GetByIdsAsync(codes, cancellationToken)` and mapping each returned `CatalogAggregate` to a `LogisticsCatalogItem` using the existing private `ToCatalogItem` helper — mirroring the pattern already used in `CatalogPackingProductSourceAdapter.GetByCodesAsync`.

**Acceptance criteria:**
- Exactly one call to `_catalogRepository.GetByIdsAsync` is made per invocation of `GetCatalogItemsAsync`, regardless of how many codes are passed in.
- The returned dictionary is keyed by product code and contains an entry only for codes the repository actually found (codes with no matching aggregate are simply absent — same "unknown code is silently dropped" behavior as today's per-item loop, not a thrown exception).
- Passing an empty `codes` list returns an empty dictionary without calling the repository with an empty/degenerate query in a way that throws.
- Duplicate codes in the input do not cause a duplicate-key error (the loop that builds the ingredient list already de-duplicates via `.Distinct()` before calling this method, but the adapter itself should not assume that — reuse of `GetByIdsAsync`'s own key semantics naturally covers this since it already returns a dictionary keyed by id).

### FR-3: Replace the per-item loop in `GetGiftPackageDetailAsync`
In `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` (lines 111-118), replace:

```csharp
var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
var ingredientCatalog = new Dictionary<string, LogisticsCatalogItem>(StringComparer.Ordinal);
foreach (var code in ingredientCodes)
{
    var item = await _catalogSource.GetCatalogItemAsync(code, cancellationToken);
    if (item != null)
        ingredientCatalog[code] = item;
}
```

with a single call to the new batch method:

```csharp
var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
var ingredientCatalog = await _catalogSource.GetCatalogItemsAsync(ingredientCodes, cancellationToken);
```

The rest of the method (the subsequent `foreach (var part in productParts)` loop that builds `GiftPackageIngredientDto` from `ingredientCatalog`) is unchanged — it already reads via `TryGetValue`, which works identically against the dictionary returned by the batch call.

**Acceptance criteria:**
- `_catalogSource.GetCatalogItemAsync` (single-item) is no longer called from `GetGiftPackageDetailAsync`.
- `_catalogSource.GetCatalogItemsAsync` (batch) is called exactly once per invocation of `GetGiftPackageDetailAsync`, regardless of the number of distinct ingredients.
- Behavior for the caller is unchanged: `GetGiftPackageDetailAsync`, `CreateManufactureAsync`, and `DisassembleGiftPackageAsync` (the latter two call `GetGiftPackageDetailAsync` internally) produce identical `GiftPackageDto`/`GiftPackageIngredientDto` output for the same inputs as before this change, including the existing behavior that an ingredient missing from the catalog gets `AvailableStock = 0` and `Image = null` rather than causing an error.

### FR-4: Update existing unit tests to match the new call pattern
`backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` currently mocks and verifies `GetCatalogItemAsync` (e.g. `GetGiftPackageDetailAsync_CallsGetCatalogItemAsyncPerIngredient`, asserting `Times.Exactly(2)`, plus other setups of the single-item method used to supply ingredient stock data). These must be updated to mock/verify `GetCatalogItemsAsync` instead, including a case that verifies the batch method is called exactly once (not once per ingredient) and a case preserving the "missing ingredient in catalog" behavior (ingredient code absent from the returned dictionary → zero stock, null image).

**Acceptance criteria:**
- No remaining test in `GiftPackageManufactureServiceTests.cs` sets up or verifies `_catalogSourceMock.Object.GetCatalogItemAsync` in place of the batch method for ingredient resolution.
- A test exists asserting `GetCatalogItemsAsync` is invoked exactly once per `GetGiftPackageDetailAsync` call, independent of ingredient count (replacing the old `Times.Exactly(2)` per-ingredient assertion, which specifically encoded the N+1 behavior this change removes).
- The "missing ingredient" test continues to pass with equivalent behavior, now driven by an incomplete dictionary returned from the mocked batch call rather than a `null` return from the single-item mock.
- `dotnet build` and the full `Anela.Heblo.Tests` suite pass.

## Non-Functional Requirements

### NFR-1: Performance
Reduce the number of `ILogisticsCatalogSource` round-trips for ingredient resolution from O(N) (one per distinct ingredient code) to O(1) (one batch call) per `GetGiftPackageDetailAsync` invocation, and therefore also per `CreateManufactureAsync` and `DisassembleGiftPackageAsync` invocation. No specific latency target is set beyond this structural fix, since the current backing implementation (`ICatalogRepository`, in-memory/cached) is not itself a network call today — the goal is to remove the N+1 shape at the interface boundary so a future or alternate implementation of `ILogisticsCatalogSource` cannot reintroduce N sequential network round-trips.

### NFR-2: Security
No change. This is an internal application-layer interface with no new external inputs, authentication surface, or sensitive data exposure; the data returned (`LogisticsCatalogItem`: product code, image, stock quantities) is identical to what the single-item method already returns per code.

## Data Model
No new persisted entities. Reuses existing types:
- `LogisticsCatalogItem` (`ProductCode`, `Image`, `EshopStock`, `AvailableStock`) — the value type returned per code, unchanged.
- New in-memory shape only: `IReadOnlyDictionary<string, LogisticsCatalogItem>` as the batch method's return type, keyed by product code, matching the convention already used by `IManufactureCatalogSource.GetByIdsAsync`, `IMaterialCatalogService.GetByIdsAsync`, and `IPackingProductSource.GetByCodesAsync`.

## API / Interface Design
Interface change (internal application-layer contract, not a public HTTP API):

```csharp
// ILogisticsCatalogSource.cs
Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
    IReadOnlyList<string> codes, CancellationToken cancellationToken);
```

Implementation (`LogisticsCatalogSourceAdapter`):

```csharp
public async Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
    IReadOnlyList<string> codes,
    CancellationToken cancellationToken)
{
    var aggregates = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
    return aggregates.ToDictionary(kv => kv.Key, kv => ToCatalogItem(kv.Value));
}
```

No HTTP endpoint, contract DTO, or frontend surface changes — `GiftPackageDto`/`GiftPackageIngredientDto` and all controller-facing responses are unaffected.

## Dependencies
- `ICatalogRepository.GetByIdsAsync` (`backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs`) — already exists and is already used by this exact pattern in sibling adapters (`CatalogPackingProductSourceAdapter`, `PurchaseMaterialCatalogAdapter`, `CatalogManufactureCatalogSourceAdapter`). No repository-level changes are required.
- No new NuGet packages, external services, or feature flags.

## Out of Scope
- `GetTransportBoxByCodeHandler` (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs:74-81`) has the identical per-item `GetCatalogItemAsync` loop pattern for enriching transport box items. It is a separate call site not mentioned in the brief; migrating it to the new batch method is a natural follow-up but is not part of this fix.
- Any change to the single-item `GetCatalogItemAsync` method's signature or removal — it is kept as-is per the brief's suggested fix ("The existing single-item method can be kept for callers that genuinely need it one at a time").
- Any change to `ICatalogRepository`, `CatalogAggregate`, or the underlying catalog data source/refresh mechanism.
- Performance benchmarking or load testing — this is a structural N+1 fix with no specific throughput/latency target (see NFR-1).
- Adding a batch method to `IManufactureClient.GetSetPartsAsync` (the BOM lookup) — that call is already a single call per gift package, not a loop, and is unaffected.

## Open Questions
None.

## Status: COMPLETE
