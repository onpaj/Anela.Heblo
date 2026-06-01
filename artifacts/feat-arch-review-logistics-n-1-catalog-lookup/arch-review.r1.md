I have enough to write the review. The major finding: `GetByIdsAsync` already exists on `ICatalogRepository` (Domain/Features/Catalog/ICatalogRepository.cs:54) with a different (better) signature than the spec proposed. The catalog is fully in-memory cache — no DB, no per-id roundtrips. Existing handler silently logs and skips missing items, not the NRE the spec assumed.

```markdown
# Architecture Review: Batched Catalog Lookups for Logistics Handlers

## Skip Design: true

Backend-only refactor of two handler internals. No UI components, screens, or visual decisions change.

## Architectural Fit Assessment

The feature fits cleanly into existing patterns, but the spec is materially out of date with the codebase. Three observations dominate the review:

1. **`GetByIdsAsync` already exists** on `ICatalogRepository` (`backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs:54`) with the signature `Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken)`. It is implemented in `CatalogRepository.cs:927-934` and `MockCatalogRepository.cs:334-341`, and is already in production use by `UpdateManufactureOrderStatusHandler`, `CalculatedBatchSizeHandler`, `GetProductUsageHandler`, `UpdatePurchaseOrderHandler`, `GetPurchaseOrderByIdHandler`, `PurchaseMaterialCatalogAdapter`, and the Shoptet packing-order client. **FR-1 of the spec is already done.** The work reduces to "make the two Logistics handlers use it like everyone else does."

2. **There is no DB and no per-id roundtrip.** `ICatalogRepository` is backed entirely by `IMemoryCache` containing a fully-merged `List<CatalogAggregate>`. `GetByIdAsync(id)` does `CatalogData.SingleOrDefault(s => s.ProductCode == id)` — a **linear scan of the entire in-memory catalog**, not a DB call. With ~thousands of products in catalog × 30 items in a box, the N+1 cost is N full-list scans (~O(N·M) total comparisons), not N network roundtrips. The fix is still worth doing (one dictionary projection vs. N linear scans), but FR-4's framing (EF `WHERE IN (...)`, cache multi-get, `IDistributedCache` per-key) and the entire "verify single physical lookup against backing store" discussion **do not apply to this codebase**. The existing `GetByIdsAsync` implementation already does the right thing: one `HashSet` build + one `Where(...).ToDictionary(...)` pass.

3. **Existing missing-item behavior is silent-skip-with-warning, not NRE-or-throw.** `GetTransportBoxByCodeHandler.cs:74-86` wraps the loop body in `try { ... } catch (Exception ex) { _logger.LogWarning(...) }`. A missing catalog item today produces a logged warning and an item DTO with default `ImageUrl`/`OnStock`. The spec's FR-2 prescription (throw `InvalidOperationException("Catalog item not found for product code ...")`) **tightens behavior the existing code deliberately loosens**, and would change the response shape for any box containing a product that has been pruned from the catalog. The refactor must preserve silent-skip-with-warning, not introduce a hard fail.

Aside from those three corrections, the proposal aligns perfectly with how the rest of the codebase already consumes `GetByIdsAsync` (look at `UpdateManufactureOrderStatusHandler` or `CalculatedBatchSizeHandler` for the canonical local pattern: `Distinct().ToList()`, single call, dictionary lookup in loop).

The Logistics-owned catalog interface from issue #1960 is irrelevant: the existing `GetByIdsAsync` is on the shared `ICatalogRepository`, and the two Logistics consumers (`GetTransportBoxByCodeHandler.cs:15`, `GiftPackageManufactureService.cs:18`) already depend on that shared interface. No #1960 coordination is needed for this work.

## Proposed Architecture

### Component Overview

```
                         GetTransportBoxByCode (MediatR)
                                   │
                                   ▼
                    GetTransportBoxByCodeHandler
                         │                   │
                         │                   ▼
                         │      ICatalogRepository.GetByIdsAsync(codes)
                         │                   │
                         ▼                   ▼
            ITransportBoxRepository    CatalogRepository
                                       (in-memory cache)
                                              │
                                              ▼
                                   IReadOnlyDictionary<string, CatalogAggregate>
```

Same shape for `GiftPackageManufactureService.GetGiftPackageDetailAsync`, with `IManufactureClient.GetSetPartsAsync` taking the role of `ITransportBoxRepository`.

No new components, no new interfaces, no new types.

### Key Design Decisions

#### Decision 1: Use the existing `GetByIdsAsync` as-is; do not add a new method
**Options considered:**
- (A) Add a new `Task<IReadOnlyList<CatalogAggregate>> GetByIdsAsync(...)` per spec FR-1.
- (B) Use the existing `Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(...)`.

**Chosen approach:** (B).

**Rationale:** The existing dictionary-returning signature is in production, covered by tests, and matches what every other handler in the codebase consumes. Adding a parallel list-returning method would split the interface for no benefit (callers would need to project to a dictionary anyway, which is exactly the projection the existing method already performs). The dictionary signature is also more ergonomic: it eliminates the `.ToDictionary(c => c.ProductCode)` step from each call site.

#### Decision 2: Preserve silent-skip-on-missing in `GetTransportBoxByCodeHandler`, do not throw
**Options considered:**
- (A) Throw `InvalidOperationException` when a code is unresolved (per spec FR-2).
- (B) Preserve the existing try/catch's silent-skip-with-warning behavior using an explicit `TryGetValue` check + `_logger.LogWarning(...)`.

**Chosen approach:** (B).

**Rationale:** The current code at `GetTransportBoxByCodeHandler.cs:76-86` catches `Exception` and logs a warning. The implicit NRE from the `!` null-forgiving operator is the exception that gets caught — meaning today, a missing catalog item produces a logged warning and a partially-populated DTO, not a 500. The scan endpoint is latency-sensitive *and* end-user-facing in the warehouse; converting "warehouse operator scans a box containing a recently-discontinued SKU" from "succeeds with missing image" to "scan fails" is a behavior regression dressed as a fix. Replace the `!`-and-try/catch with an explicit `TryGetValue` + log-and-continue.

#### Decision 3: Preserve null-tolerant fallback in `GiftPackageManufactureService`
**Options considered:**
- (A) Throw on missing ingredient (mirror Decision 2's strict path).
- (B) Preserve the existing `ingredientProduct?.Stock.Available ?? 0` semantics.

**Chosen approach:** (B).

**Rationale:** `GiftPackageManufactureService.cs:165-166` reads `(double)(ingredientProduct?.Stock.Available ?? 0)` and `ingredientProduct?.Image`. Missing-ingredient is already a documented success path: a `GiftPackageIngredientDto` is created with `AvailableStock=0` and `Image=null`. Bug-for-bug preservation: same observable result, just sourced from a dictionary lookup that returns no entry.

#### Decision 4: De-duplicate input codes via `Distinct()` at the call site
**Options considered:**
- (A) Rely on the implementation's internal `HashSet` dedup.
- (B) Add `.Distinct()` to the caller's projection before passing to `GetByIdsAsync`.

**Chosen approach:** (B), matching the existing codebase convention.

**Rationale:** `UpdateManufactureOrderStatusHandler` and every other current consumer call `.Distinct().ToList()` before passing to `GetByIdsAsync`. It is harmless duplication of the implementation's safety, but it makes the call site self-documenting ("yes, I know about repeats") and reduces transient allocation inside the impl. Follow local convention.

## Implementation Guidance

### Directory / Module Structure

No new files. Two existing files change:

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs` (lines 73-86)
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` (lines 153-170)

Two existing test files update:

- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/GetTransportBoxByCodeHandlerTests.cs` — replace `GetByIdAsync` setups with `GetByIdsAsync` setups; add a test asserting `GetByIdAsync` is never invoked and `GetByIdsAsync` is invoked exactly once; add a test for the missing-item silent-skip-with-warning path.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` — same updates; add a test for the missing-ingredient null-tolerant path.

### Interfaces and Contracts

**`ICatalogRepository` — no change.** The method already exists:

```csharp
Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(
    IEnumerable<string> ids,
    CancellationToken cancellationToken = default);
```

Public HTTP / MediatR / OpenAPI contracts — no change. `GetTransportBoxByCodeRequest`, `GetTransportBoxByCodeResponse`, `GiftPackageDto`, `GiftPackageIngredientDto` shapes are untouched.

### Data Flow

**`GetTransportBoxByCodeHandler` — refactored body** (replaces lines 73-86):

```csharp
var codes = dto.Items.Select(i => i.ProductCode).Distinct().ToList();
var catalogByCode = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);

foreach (var itemDto in dto.Items)
{
    if (catalogByCode.TryGetValue(itemDto.ProductCode, out var catalogItem))
    {
        itemDto.ImageUrl = catalogItem.Image;
        itemDto.OnStock = catalogItem.Stock.Eshop;
    }
    else
    {
        _logger.LogWarning(
            "Catalog item not found for product code {ProductCode} in transport box {BoxCode}; leaving image/stock unset",
            itemDto.ProductCode, dto.Code);
    }
}
```

Note: no try/catch wrapper. `GetByIdsAsync` is the catalog repository's lookup primitive — if it throws, we want the failure to surface, not be swallowed. The previous try/catch existed to swallow NREs from `!` on a null result; with `TryGetValue` that class of exception is gone.

**`GiftPackageManufactureService.GetGiftPackageDetailAsync` — refactored body** (replaces lines 153-170):

```csharp
var ingredientCodes = productParts.Select(p => p.ProductCode).Distinct().ToList();
var ingredientCatalog = await _catalogRepository.GetByIdsAsync(ingredientCodes, cancellationToken);

var ingredients = new List<GiftPackageIngredientDto>();
foreach (var part in productParts)
{
    ingredientCatalog.TryGetValue(part.ProductCode, out var ingredientProduct);

    var ingredient = new GiftPackageIngredientDto
    {
        ProductCode = part.ProductCode,
        ProductName = part.ProductName,
        RequiredQuantity = part.Amount,
        AvailableStock = (double)(ingredientProduct?.Stock.Available ?? 0),
        Image = ingredientProduct?.Image
    };

    ingredients.Add(ingredient);
}
```

`ingredientProduct` becomes `null` for an unresolved code, and the existing `?.` / `?? 0` chain handles it — identical observable result to the current `GetByIdAsync` path.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Refactor accidentally tightens missing-item handling in `GetTransportBoxByCodeHandler` (throws where it used to swallow), breaking scans for products pruned from catalog | Medium | Decision 2 above explicitly preserves silent-skip-with-warning. Add a dedicated unit test: box with one item whose code is absent from the catalog mock → response succeeds, item DTO has default `ImageUrl`/`OnStock`, `_logger.LogWarning` was invoked. |
| Refactor changes observable behavior in `GiftPackageManufactureService` because the existing code's null-tolerance is subtle | Low | Decision 3 + the literal `TryGetValue`-then-null-coalesce structure shown above. Add a unit test: ingredient with code absent from catalog → `AvailableStock == 0`, `Image == null`. |
| Existing tests for `GetTransportBoxByCodeHandlerTests` still mock `GetByIdAsync` and silently keep passing because the new code doesn't use it | Low-Medium | Add `_catalogRepositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<string>(), ...), Times.Never())` and `_catalogRepositoryMock.Verify(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), ...), Times.Once())` to the happy-path tests. Without this, a regression to per-item lookups would slip through CI. |
| Spec contradiction with reality misleads implementer (signature mismatch, NRE-or-throw, EF `IN (...)`, cache layering) | Medium | Specification Amendments below restate the truth. Implementer should read this review before the spec. |
| Hidden third call site in Logistics (handler not named in brief) keeps the N+1 pattern | Low | Quick `grep -rn "_catalogRepository.GetByIdAsync" backend/src/Anela.Heblo.Application/Features/Logistics` before closing the PR. Mention any hits in the PR description; don't expand scope without a follow-up brief. |

## Specification Amendments

The spec was written against an assumed state of the codebase that does not match reality. The implementer should treat these amendments as overrides:

1. **Strike FR-1 entirely.** `GetByIdsAsync` already exists on `ICatalogRepository` and is implemented in `CatalogRepository.cs:927-934` and `MockCatalogRepository.cs:334-341`. Acceptance criteria for "method exists / dedup / empty input zero work / single backing call" are already met by the production code, with test coverage in adapter and consumer tests. No interface or implementation work is required.

2. **Replace the proposed signature.** The spec's `Task<IReadOnlyList<CatalogAggregate>> GetByIdsAsync(...)` is wrong. The actual (and superior) signature is `Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken)`. All sample code in the spec that calls `.ToDictionary(...)` after `GetByIdsAsync` should be deleted — the dictionary is returned directly.

3. **Strike all of FR-4.** There is no DB, no EF query, no `IDistributedCache`, no cache layering. The catalog is a single in-memory `List<CatalogAggregate>` in `IMemoryCache`. "Single backing-store round-trip" is not a meaningful acceptance criterion here; the meaningful one is "single call to `_catalogRepository.GetByIdsAsync(...)`, zero calls to `_catalogRepository.GetByIdAsync(...)`" — verifiable by `Mock<ICatalogRepository>.Verify(..., Times.Once/Never)`.

4. **Rewrite FR-2's missing-item behavior.** The spec claims existing code uses `!` and would NRE on missing items, and prescribes converting that to a thrown `InvalidOperationException`. In fact, lines 76–86 of the current handler wrap the loop body in `try/catch (Exception ex)` and **log a warning, continuing the loop**. The refactor must preserve that silent-skip-with-warning semantic via `TryGetValue` + `_logger.LogWarning`, not introduce a hard exception. See Decision 2 above for the exact replacement code.

5. **Strike the #1960 coordination clause.** `GetByIdsAsync` is on the shared `ICatalogRepository`; both target handlers already consume that interface. There is no Logistics-owned catalog interface today, and this refactor does not need to wait for or coordinate with #1960.

6. **Performance acceptance criteria.** Replace "drops from ~20 catalog round-trips to 1" with "drops from N linear scans of the in-memory catalog list (~O(N·M) comparisons where M is catalog size) to one dictionary projection (~O(N+M))." Still a meaningful win for the scan endpoint, but the mechanism is CPU/allocation, not network. The PR description should report wall-clock for a representative scan; if it is not materially lower, the change is still defensible on code-clarity / consistency-with-other-handlers grounds.

7. **Out-of-scope cleanup, in-scope flag.** The current `GetTransportBoxByCodeHandler.cs:78` uses `(await _catalogRepository.GetByIdAsync(itemDto.ProductCode, cancellationToken))!` — the `!` is masking the very nullability the try/catch then handles. The refactor naturally removes both. Don't add a separate cleanup commit; the refactor *is* the cleanup.

## Prerequisites

None. All required infrastructure (interface method, implementation, mock implementation, dependency-injection registration, Moq usage pattern, test scaffolding) already exists in the codebase. The implementer can start by reading `UpdateManufactureOrderStatusHandler` for the canonical local idiom and copy its shape into the two target handlers, applying the missing-item-semantics overrides from Decisions 2 and 3.
```