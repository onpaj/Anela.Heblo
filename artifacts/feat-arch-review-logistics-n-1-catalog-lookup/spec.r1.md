# Specification: Batched Catalog Lookups for Logistics Handlers

## Summary
Replace per-item catalog lookups in two Logistics handlers (`GetTransportBoxByCodeHandler` and `GiftPackageManufactureService.GetGiftPackageDetailAsync`) with a single batched `GetByIdsAsync` call. This eliminates an N+1 access pattern that multiplies handler wall-clock time linearly with the number of products in a transport box or gift package — a problem felt most acutely at the latency-sensitive barcode-scan endpoint.

## Background
Two handlers in the Logistics module currently iterate over their items and call `_catalogRepository.GetByIdAsync(...)` once per product. Each call is a separate repository dispatch and (in the worst case) a separate DB query or cache fetch.

- `GetTransportBoxByCodeHandler` (lines 74–86) — invoked by the warehouse barcode-scanner workflow; a typical transport box contains 5–30 distinct products, yielding 5–30 sequential lookups per scan.
- `GiftPackageManufactureService.GetGiftPackageDetailAsync` (lines 155–170) — gift packages have 4–15 ingredients, producing the same serial fan-out per detail request.

The scan endpoint is latency-sensitive: scanners ping the API interactively and any added round-trip is felt by warehouse operators. Reducing the lookup to a single batched call collapses N round-trips into 1.

The catalog interface used by Logistics is in flux: issue #1960 introduces a Logistics-owned catalog abstraction (decoupling Logistics from the shared catalog interface). The new method must be added to whichever interface Logistics consumes today; if #1960 is already merged at implementation time, the addition goes onto the Logistics-owned interface, not the shared one.

## Functional Requirements

### FR-1: Add `GetByIdsAsync` to the catalog interface consumed by Logistics
Introduce a batched lookup method on the catalog repository interface used by the two affected handlers.

**Signature (proposed):**
```csharp
Task<IReadOnlyList<CatalogAggregate>> GetByIdsAsync(
    IEnumerable<string> productCodes,
    CancellationToken cancellationToken = default);
```

**Behavior:**
- Accepts an enumerable of product codes; the implementation deduplicates internally (callers may also dedupe).
- Returns only the catalog items that were found — missing codes are silently omitted (no exception, no null entries). Callers handle missing items explicitly.
- Order of the returned list is not guaranteed; callers project to a dictionary keyed by `ProductCode`.
- Empty input → empty result (no repository round-trip).

**Acceptance criteria:**
- Method exists on the catalog interface that Logistics depends on (shared catalog interface today; Logistics-owned interface if #1960 has landed).
- Unit test: passing N codes returns ≤N items; missing codes are omitted; duplicate input codes produce a single result entry per code.
- Unit test: empty input returns empty result and performs zero repository work.
- The concrete implementation issues a **single** underlying query/fetch (verified by mock/spy in test, or by integration test asserting one DB round-trip).

### FR-2: Refactor `GetTransportBoxByCodeHandler` to use the batched lookup
Replace the `foreach` loop calling `GetByIdAsync` per item with one `GetByIdsAsync` call followed by a dictionary projection.

**Implementation outline:**
```csharp
var codes = dto.Items.Select(i => i.ProductCode).Distinct().ToList();
var catalogItems = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
var byCode = catalogItems.ToDictionary(c => c.ProductCode);

foreach (var itemDto in dto.Items)
{
    if (byCode.TryGetValue(itemDto.ProductCode, out var cat))
    {
        itemDto.ImageUrl = cat.Image;
        itemDto.OnStock = cat.Stock.Eshop;
    }
}
```

**Acceptance criteria:**
- The handler issues exactly one catalog lookup per request, regardless of the number of items in the transport box (verified by mock-based unit test counting calls to `GetByIdsAsync` / `GetByIdAsync`).
- Existing handler behavior is preserved: each item DTO is populated with `ImageUrl` and `OnStock` derived from its matching catalog item.
- Items whose `ProductCode` does not resolve to a catalog item leave `ImageUrl` and `OnStock` at their default (current code uses `!` null-forgiving, which would NRE on a miss — see [Open Questions](#open-questions) on whether to preserve "throw on miss" semantics or quietly skip).
- All existing tests for `GetTransportBoxByCodeHandler` continue to pass; new test covers the batched-call assertion.

### FR-3: Refactor `GiftPackageManufactureService.GetGiftPackageDetailAsync` to use the batched lookup
Apply the same pattern to the gift-package ingredient enrichment loop (lines 155–170).

**Acceptance criteria:**
- The method issues exactly one catalog lookup per request, regardless of ingredient count.
- Existing behavior preserved for ingredients whose catalog item resolves successfully.
- Existing tests for `GiftPackageManufactureService.GetGiftPackageDetailAsync` continue to pass; new test covers the batched-call assertion.

### FR-4: Preserve current "missing-catalog-item" handling semantics
The two call sites differ in how strict they are about missing catalog items. The refactor must keep observable behavior identical unless explicitly noted otherwise.

**Acceptance criteria:**
- `GetTransportBoxByCodeHandler`: see [Open Questions](#open-questions) — default assumption is to **preserve current "expect item exists" contract** by throwing an exception (or returning an error response) if any requested code is unresolved, matching today's `!` null-forgiving usage. Decision recorded before implementation.
- `GiftPackageManufactureService`: must match the prior behavior at lines 155–170 of that file (which the brief does not fully quote). Implementation step should read the prior code and preserve identical missing-item handling.

## Non-Functional Requirements

### NFR-1: Performance
- `GetTransportBoxByCode` end-to-end latency for a 20-item box should drop from ~20 catalog round-trips to 1.
- No regression in single-item or zero-item cases.
- The batched query must not be materially slower than a single `GetByIdAsync` for N=1 (worst case acceptable: equal latency).
- Memory overhead bounded by the batch size (no unbounded buffering beyond the input list).

### NFR-2: Correctness & backwards compatibility
- Public API surfaces (`GetTransportBoxByCode` request/response, `GetGiftPackageDetail` request/response) remain unchanged.
- No change to OpenAPI-generated client contracts.
- No change to authentication, authorization, logging, or error-response shapes for either endpoint.

### NFR-3: Testability
- Catalog repository interface change must be covered by unit tests on the implementation.
- Both handler refactors must include a test asserting "exactly one batched call" via a mock repository.

### NFR-4: Security
No change. The refactor touches read-side enrichment only; no new data is exposed and no auth boundaries shift.

## Data Model
No schema changes. Only the catalog **interface** gains a new method. The underlying `CatalogAggregate` (or whatever type `GetByIdAsync` currently returns) is reused unchanged.

Key entities involved:
- `CatalogAggregate` (or equivalent catalog DTO) — read-only, contains `ProductCode`, `Image`, `Stock.Eshop`, and other fields not relevant here.
- Existing Logistics DTOs (`TransportBoxItemDto`, gift-package ingredient DTO) — unchanged.

## API / Interface Design

### Repository interface change
Add one method to the catalog repository interface that Logistics consumes:

```csharp
Task<IReadOnlyList<CatalogAggregate>> GetByIdsAsync(
    IEnumerable<string> productCodes,
    CancellationToken cancellationToken = default);
```

### Implementation strategy
The concrete implementation should:
- Deduplicate the incoming codes.
- Issue a single underlying lookup (e.g., `WHERE ProductCode IN (...)` for an EF/SQL backing store, or a multi-key cache fetch).
- Return matched items only; do not throw on missing codes.

If the concrete repository is cache-backed, prefer a single batched cache call (e.g., `IDistributedCache.GetAsync` per key is still N round-trips — investigate whether the cache provider supports multi-get, or whether the layer below the cache should be batched).

### Public API
No public HTTP/MediatR contract changes. `GetTransportBoxByCode` and `GetGiftPackageDetail` request and response shapes are unchanged.

## Dependencies
- **Catalog repository interface** — the interface that Logistics depends on (today shared; potentially Logistics-owned per #1960). The new method lands on whichever is current at implementation time.
- **Issue #1960** — Logistics-owned catalog interface refactor. Not a blocker, but the implementer must check its status to know which interface to extend. If #1960 is in flight (open PR), coordinate to avoid merge conflict.
- **Underlying catalog data source** — must support an efficient batched fetch. If today's repository is layered over a backing store that does not (e.g., a cache layer with per-key `GetAsync` only), implementer must decide whether to batch at a lower layer or accept "1 dispatch to the repository, N to the cache" as a partial win.

## Out of Scope
- Batching catalog lookups in handlers outside Logistics (e.g., Manufacturing, Catalog, Reporting). Only the two handlers named in the brief are in scope.
- Adding caching, prefetching, or memoization beyond the batched lookup.
- Performance benchmarking infrastructure or load-test scenarios (a one-off measurement to confirm the win is fine; building benchmark suites is not).
- Changes to the `CatalogAggregate` shape or any catalog-side projections.
- Resolving #1960 itself — this work coexists with #1960 but does not subsume it.
- UI changes to the scan or gift-package screens.

## Open Questions

1. **Missing-item handling in `GetTransportBoxByCodeHandler`.** The current code at line ~76 uses `(await _catalogRepository.GetByIdAsync(...))!` — the `!` null-forgiving operator. This means today the handler would NRE if a catalog item is missing, effectively asserting "this code always exists." After batching, should we:
   - **(a) Preserve the assertion** by throwing an explicit exception (e.g., `InvalidOperationException` or a domain-specific error) when any item in the transport box has no matching catalog entry? — *Default assumption: yes, preserve current strict contract with a clearer exception than NRE.*
   - **(b) Soft-fail** by leaving `ImageUrl` and `OnStock` at default values when a code is unresolved (matches the snippet in the brief's "Suggested fix")?
   The brief's suggested code does (b); the existing code does (a). These have different observable behaviors. **Implementer must confirm with reviewer before coding.**

2. **Which catalog interface should receive `GetByIdsAsync`?** Status of #1960 at implementation time determines the answer. If #1960 has merged, add to the Logistics-owned interface; otherwise add to the shared interface and re-home as part of #1960's follow-up.

3. **Batching at the storage layer.** If the catalog repository is currently a thin wrapper over a per-key cache (e.g., `IMemoryCache.Get` per key), a `GetByIdsAsync` implemented as `Task.WhenAll(codes.Select(GetByIdAsync))` would be a regression dressed as a fix. Implementer must verify the concrete implementation issues a *single* lookup against the backing store, not parallel per-key lookups. If the cache provider does not support multi-get, the batched lookup should bypass the cache and hit the source store directly, or push the batch primitive down into the cache layer.

4. **Cancellation semantics for partial results.** If the batched lookup is cancelled mid-flight, what does the caller see? Default assumption: cancellation propagates as `OperationCanceledException` and the partial result is discarded (matches typical .NET conventions); no special handling required.

## Status: HAS_QUESTIONS