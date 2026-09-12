# Architecture Review: Batch Catalog Lookup for Gift Package Manufacture Ingredients

## Skip Design: true

## Architectural Fit Assessment

This is a small, well-contained fix that closes a gap in an existing pattern — it does not introduce a new pattern. `ICatalogRepository.GetByIdsAsync` already exists precisely for this purpose (its own doc comment says "Bulk lookup — eliminates N+1 query patterns"), and three sibling adapters in `Catalog/Infrastructure` (`CatalogPackingProductSourceAdapter.GetByCodesAsync`, `PurchaseMaterialCatalogAdapter.GetByIdsAsync`, `CatalogManufactureCatalogSourceAdapter`) already expose a batch method on their respective outbound-port interfaces by delegating to it. `ILogisticsCatalogSource` is simply the one adapter in this family that never got the batch method added. The fix brings it into line with an established, repeated convention — there is no design decision to make here, only consistent application of what already exists.

Verified in code:
- `ILogisticsCatalogSource` (`Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs`) currently exposes only `GetCatalogItemAsync(string, ...)`.
- `LogisticsCatalogSourceAdapter` (`Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs`) is `internal sealed`, takes `ICatalogRepository` via constructor injection, and already has a private `ToCatalogItem(CatalogAggregate)` mapper used by the single-item method — the batch method reuses it as-is.
- `ICatalogRepository.GetByIdsAsync(IEnumerable<string>, CancellationToken)` returns `IReadOnlyDictionary<string, CatalogAggregate>`. Its concrete implementation (`CatalogRepository.GetByIdsAsync`, `Application/Features/Catalog/CatalogRepository.cs:68-75`) is a synchronous in-memory filter over `_cacheStore.GetCatalogData()` wrapped in `Task.FromResult` — not a network or DB call today, and it tolerates an empty `ids` collection without throwing (empty `HashSet`, empty result dict).
- `PurchaseMaterialCatalogAdapter.GetByIdsAsync` is the closest structural twin: `_catalogRepository.GetByIdsAsync(...)` → `foreach` → build `Dictionary<string, T>` with `StringComparer.Ordinal` sized to `aggregates.Count`. `CatalogPackingProductSourceAdapter.GetByCodesAsync` does the same via LINQ `.ToDictionary`. Either shape is acceptable; use whichever reads more consistently with the adapter's existing style (this adapter currently has no dictionary-building code to match against, so either is fine — LINQ `.ToDictionary` is the more concise of the two seen in the spec).
- DI registration: `services.AddTransient<ILogisticsCatalogSource, LogisticsCatalogSourceAdapter>()` in `CatalogModule.cs:58` — no lifetime change needed, the new method is stateless like the rest of the class.
- Call sites confirmed: `GiftPackageManufactureService` is the only consumer of `ILogisticsCatalogSource.GetCatalogItemAsync` for ingredient resolution; `CreateManufactureAsync` (and, per the spec, `DisassembleGiftPackageAsync`) call `GetGiftPackageDetailAsync` internally, so fixing the one call site fixes all three paths. `GetTransportBoxByCodeHandler` has an identical loop but is explicitly out of scope per the spec.

No module-boundary, contract-ownership, or persistence concerns are raised: this stays entirely inside `Application/Features/Catalog/Infrastructure` (the adapter) and `Application/Features/Logistics` (the interface and consuming service), matching `docs/architecture/development_guidelines.md`'s rule that inter-module communication happens exclusively through `contracts/` interfaces — `ILogisticsCatalogSource` remains Logistics's outbound port, and its backing implementation remains free to change independently.

## Proposed Architecture

No new components. This adds one method to an existing interface and its one existing implementation, and changes one call site to use it.

### Component Overview

```
GiftPackageManufactureService (Logistics/UseCases/.../Services)
        │  calls ILogisticsCatalogSource.GetCatalogItemsAsync(codes)   [NEW]
        ▼
ILogisticsCatalogSource (Logistics/Contracts)                          [+1 method]
        │  implemented by
        ▼
LogisticsCatalogSourceAdapter (Catalog/Infrastructure)                 [+1 method]
        │  delegates to
        ▼
ICatalogRepository.GetByIdsAsync (Domain/Features/Catalog)             [unchanged, pre-existing]
```

This mirrors the already-established shape used by `IPackingProductSource`, `IMaterialCatalogService`, and `IManufactureCatalogSource` against the same `ICatalogRepository.GetByIdsAsync`.

### Key Design Decisions

#### Decision 1: Add a new batch method rather than change `GetCatalogItemAsync`'s signature

**Options considered:**
- (a) Add `GetCatalogItemsAsync(IReadOnlyList<string>, CancellationToken)` alongside the existing single-item method.
- (b) Replace `GetCatalogItemAsync` with a batch-only method and adapt callers that need one item.

**Chosen approach:** (a), exactly as the brief and spec specify.

**Rationale:** `GetTransportBoxByCodeHandler` still uses the single-item method and is explicitly out of scope for this change. Removing or changing the existing method's signature would force an unrelated, unscoped change there. Additive is also the pattern every sibling adapter in `Catalog/Infrastructure` already follows (`GetByIdAsync`/`GetByIdsAsync`, `GetByIdAsync`/`GetByIdsAsync` pairs coexist).

#### Decision 2: Return type is `IReadOnlyDictionary<string, LogisticsCatalogItem>`, keyed by product code, missing codes simply absent

**Options considered:**
- (a) Dictionary keyed by code, absent entry for unmatched codes (matches today's per-item loop behavior, where a `null` result is simply skipped).
- (b) A list/collection requiring the caller to re-index, or a `Result<T>`/wrapper type signaling per-code success/failure.

**Chosen approach:** (a).

**Rationale:** This is the exact convention already used by `IManufactureCatalogSource`, `IMaterialCatalogService.GetByIdsAsync`, and `IPackingProductSource.GetByCodesAsync`, and it preserves `GetGiftPackageDetailAsync`'s existing "unknown ingredient → zero stock, no image" behavior via `TryGetValue` without any change to the consuming loop's logic. No new error-handling contract is introduced.

#### Decision 3: Adapter implementation is a single delegation to `ICatalogRepository.GetByIdsAsync` plus the existing `ToCatalogItem` mapper — no batching/chunking/parallelism logic

**Options considered:**
- (a) One direct call to `GetByIdsAsync`, map results with the existing private helper.
- (b) Add chunking, retries, or parallel fan-out in case a future backing implementation is a live network call.

**Chosen approach:** (a).

**Rationale:** `ICatalogRepository`'s current implementation is an in-memory cache read (`CatalogRepository.GetByIdsAsync`, confirmed above) with no batch-size limit or network cost. Adding resilience machinery for a hypothetical future network-backed implementation is speculative complexity this fix does not need — if `ILogisticsCatalogSource` is ever backed by a live network source, that adapter's implementation is the right place to add chunking/retry, not this one. This keeps the change exactly as scoped: one interface method, one implementation, one call-site swap.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Edit in place:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsCatalogSource.cs` | Add `GetCatalogItemsAsync` member |
| `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsCatalogSourceAdapter.cs` | Implement `GetCatalogItemsAsync`, reusing the existing private `ToCatalogItem` |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` | Replace the `foreach` loop (lines 111–118) with a single call |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` | Update mocks/assertions from `GetCatalogItemAsync` to `GetCatalogItemsAsync` for ingredient-resolution tests |

No DI registration change — `LogisticsCatalogSourceAdapter` is already registered as `ILogisticsCatalogSource` (`CatalogModule.cs:58`), and adding a method to an existing interface/implementation pair requires no new registration.

### Interfaces and Contracts

```csharp
// ILogisticsCatalogSource.cs — add alongside the existing single-item method
Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
    IReadOnlyList<string> codes, CancellationToken cancellationToken);
```

```csharp
// LogisticsCatalogSourceAdapter.cs — add, reusing the existing private ToCatalogItem
public async Task<IReadOnlyDictionary<string, LogisticsCatalogItem>> GetCatalogItemsAsync(
    IReadOnlyList<string> codes,
    CancellationToken cancellationToken)
{
    var aggregates = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
    return aggregates.ToDictionary(kv => kv.Key, kv => ToCatalogItem(kv.Value));
}
```

No public HTTP contract, request/response DTO, or frontend surface is touched — this is entirely below the MediatR handler / controller boundary.

### Data Flow

Unchanged end-to-end behavior, only the number of `ILogisticsCatalogSource` calls changes:

1. `GetGiftPackageDetailAsync` resolves the BOM (`_manufactureClient.GetSetPartsAsync`), then de-duplicates ingredient codes (`.Distinct()` — unchanged).
2. **Before:** one `GetCatalogItemAsync` call per distinct code (N round-trips into `ICatalogRepository`, currently in-memory but contractually unbounded).
3. **After:** one `GetCatalogItemsAsync(ingredientCodes, ct)` call, which performs exactly one `ICatalogRepository.GetByIdsAsync` call and returns a dictionary.
4. The existing downstream `foreach (var part in productParts)` loop is untouched — it already reads via `ingredientCatalog.TryGetValue(...)`, which behaves identically against the dictionary returned by the new batch call (including the "code absent → 0 stock, null image" fallback).
5. `CreateManufactureAsync` and `DisassembleGiftPackageAsync` inherit the fix automatically since both call `GetGiftPackageDetailAsync` internally — no changes needed in either.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file still references the old single-item mock for ingredient resolution after the change, causing compile/assert failures | Low | FR-4 in the spec already covers this explicitly; verify with `dotnet test` before completion |
| `GetByIdsAsync` on an empty `codes` list behaves unexpectedly | Low | Verified: the concrete `CatalogRepository.GetByIdsAsync` builds an empty `HashSet` and returns an empty dictionary for empty input — no exception, no special-casing needed in the adapter |
| Scope creep — also migrating `GetTransportBoxByCodeHandler` in the same change | Low | Explicitly out of scope per the spec; leave it as a separate follow-up issue, do not touch it in this PR |

## Specification Amendments

None. The spec's proposed interface signature, adapter implementation, and call-site replacement match the actual code exactly (interface location, adapter location, private `ToCatalogItem` helper, DI registration, and downstream `TryGetValue` usage were all verified directly in the repository). No architectural corrections are needed.

## Prerequisites

None. No migrations, config, or infrastructure changes are required — this is a same-PR interface addition, implementation, call-site swap, and test update, all within already-existing, already-wired components.
