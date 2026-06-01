Looking at the input artifacts, I notice the `answers.r1.md` content addresses a completely different feature (IPrintQueueSink, ExpeditionListArchive, FlexiBee sync) rather than the Logistics catalog batching questions in `spec.r1.md`. Since none of the answers match the spec's open questions, I'll resolve the questions with documented reasonable defaults where possible and keep only the genuinely undecidable ones open.

# Specification: Batched Catalog Lookups for Logistics Handlers

## Summary
Replace per-item catalog lookups in two Logistics handlers (`GetTransportBoxByCodeHandler` and `GiftPackageManufactureService.GetGiftPackageDetailAsync`) with a single batched `GetByIdsAsync` call. This eliminates an N+1 access pattern that multiplies handler wall-clock time linearly with the number of products in a transport box or gift package — a problem felt most acutely at the latency-sensitive barcode-scan endpoint used by warehouse operators.

## Background
Two handlers in the Logistics module currently iterate over their items and call `_catalogRepository.GetByIdAsync(...)` once per product. Each call is a separate repository dispatch and (in the worst case) a separate DB query or cache fetch.

- **`GetTransportBoxByCodeHandler`** (lines 74–86) — invoked by the warehouse barcode-scanner workflow; a typical transport box contains 5–30 distinct products, yielding 5–30 sequential lookups per scan.
- **`GiftPackageManufactureService.GetGiftPackageDetailAsync`** (lines 155–170) — gift packages have 4–15 ingredients, producing the same serial fan-out per detail request.

The scan endpoint is latency-sensitive: scanners ping the API interactively and every added round-trip is felt by warehouse operators in real time. Reducing the lookup to a single batched call collapses N round-trips into 1.

The catalog interface used by Logistics is in flux: issue **#1960** introduces a Logistics-owned catalog abstraction (decoupling Logistics from the shared catalog interface). The new method must be added to whichever interface Logistics consumes at implementation time. If #1960 has merged, the addition goes onto the Logistics-owned interface; otherwise it lands on the shared interface and is carried over as part of #1960's follow-up work.

## Functional Requirements

### FR-1: Add `GetByIdsAsync` to the catalog interface consumed by Logistics
Introduce a batched lookup method on the catalog repository interface used by the two affected handlers.

**Signature:**
```csharp
Task<IReadOnlyList<CatalogAggregate>> GetByIdsAsync(
    IEnumerable<string> productCodes,
    CancellationToken cancellationToken = default);
```

**Behavior:**
- Accepts an enumerable of product codes; the implementation deduplicates internally (callers may also dedupe defensively).
- Returns only the catalog items that were found — missing codes are silently omitted (no exception, no null entries). Callers handle missing items explicitly per their own contract.
- Order of the returned list is not guaranteed; callers project to a dictionary keyed by `ProductCode`.
- Empty input → empty result, with **zero** repository round-trips.
- Cancellation propagates as `OperationCanceledException`; partial results are discarded (standard .NET convention).

**Acceptance criteria:**
- Method exists on the catalog interface that Logistics depends on (shared catalog interface today; Logistics-owned interface if #1960 has landed).
- Unit test: passing N codes returns ≤N items; missing codes are omitted from the result; duplicate input codes produce a single result entry per code.
- Unit test: empty input returns empty result and performs zero repository work (verified via mock/spy).
- The concrete implementation issues a **single** underlying query/fetch (verified by mock/spy in test, or by integration test asserting one round-trip to the backing store).

### FR-2: Refactor `GetTransportBoxByCodeHandler` to use the batched lookup
Replace the `foreach` loop calling `GetByIdAsync` per item with one `GetByIdsAsync` call followed by a dictionary projection.

**Implementation outline:**
```csharp
var codes = dto.Items.Select(i => i.ProductCode).Distinct().ToList();
var catalogItems = await _catalogRepository.GetByIdsAsync(codes, cancellationToken);
var byCode = catalogItems.ToDictionary(c => c.ProductCode);

foreach (var itemDto in dto.Items)
{
    if (!byCode.TryGetValue(itemDto.ProductCode, out var cat))
    {
        throw new InvalidOperationException(
            $"Catalog item not found for product code '{itemDto.ProductCode}' in transport box.");
    }
    itemDto.ImageUrl = cat.Image;
    itemDto.OnStock = cat.Stock.Eshop;
}
```

**Missing-item semantics (resolved):** The existing code at line ~76 uses the `!` null-forgiving operator, asserting "this code always exists" — a missing item would surface as an NRE. The refactor **preserves the strict contract** but replaces the implicit NRE with an explicit, informative `InvalidOperationException` naming the offending product code. This is a behavior-equivalent improvement: any input that succeeds today still succeeds; any input that would NRE today now throws a clearer exception with the same call-site semantics (no silent corruption of the response).

**Acceptance criteria:**
- The handler issues **exactly one** catalog lookup per request, regardless of the number of items in the transport box (verified by mock-based unit test counting calls to `GetByIdsAsync` and asserting `GetByIdAsync` is never called).
- Existing successful-path behavior is preserved: each item DTO is populated with `ImageUrl` and `OnStock` derived from its matching catalog item.
- Missing-item path: an `InvalidOperationException` is thrown identifying the unresolved product code (new test).
- Items with duplicate `ProductCode` (e.g., the same product appearing twice in the box) are populated correctly from the single dictionary entry.
- All existing tests for `GetTransportBoxByCodeHandler` continue to pass.

### FR-3: Refactor `GiftPackageManufactureService.GetGiftPackageDetailAsync` to use the batched lookup
Apply the same pattern to the gift-package ingredient enrichment loop (lines 155–170).

**Missing-item semantics:** The implementer must read the existing code at lines 155–170 and preserve the prior behavior exactly. If the existing code silently tolerates a null return (e.g., `if (ingredientProduct == null) continue;`), the refactor keeps the silent-skip path. If it uses null-forgiving or otherwise asserts presence, the refactor throws an explicit `InvalidOperationException` naming the missing code, mirroring FR-2.

**Acceptance criteria:**
- The method issues **exactly one** catalog lookup per request, regardless of ingredient count (verified by mock-based test).
- Successful-path behavior preserved bit-for-bit for ingredients whose catalog item resolves.
- Missing-item path behaves identically to the current implementation (either silent-skip or throw, depending on existing code).
- All existing tests for `GiftPackageManufactureService.GetGiftPackageDetailAsync` continue to pass; new test covers the single-batched-call assertion.

### FR-4: Verify the batched lookup is a true single-round-trip
The whole point of this refactor is collapsing N round-trips into 1. An implementation that internally fans out to `Task.WhenAll(codes.Select(GetByIdAsync))` would be a regression dressed as a fix.

**Acceptance criteria:**
- The concrete `GetByIdsAsync` implementation issues a single physical lookup against the backing store (e.g., `WHERE ProductCode IN (...)` for EF; a multi-key cache call where supported).
- If the catalog repository is layered over a cache that does **not** support multi-get (e.g., `IDistributedCache` per-key), the batched method must either:
  - (a) use a multi-key cache primitive if available on the concrete cache provider, or
  - (b) bypass the cache for batch reads and hit the source store directly with a single query, or
  - (c) push the batch primitive down into the cache layer.
- A simple parallel fan-out (`Task.WhenAll`) is **not** acceptable.
- Verification: integration test (preferred) or a one-off measurement attached to the PR description confirming a single backing-store call for a multi-code request.

## Non-Functional Requirements

### NFR-1: Performance
- `GetTransportBoxByCode` end-to-end latency for a 20-item box drops from ~20 catalog round-trips to 1.
- No regression in single-item or zero-item cases (batched call with N=1 must not be materially slower than `GetByIdAsync`; equal latency is acceptable as a worst case).
- Memory overhead bounded by the input batch size (no unbounded buffering beyond the requested code list).

### NFR-2: Backwards compatibility
- Public HTTP / MediatR contracts (`GetTransportBoxByCode` request/response, `GetGiftPackageDetail` request/response) remain unchanged.
- No change to OpenAPI-generated TypeScript or C# client contracts.
- No change to authentication, authorization, logging, or error-response shapes for either endpoint.

### NFR-3: Testability
- Catalog repository interface change covered by unit tests on the concrete implementation (single-call assertion, dedup behavior, missing-code omission, empty-input short-circuit).
- Both handler refactors include a unit test asserting "exactly one batched call" via a mock repository (call-count = 1 on `GetByIdsAsync`, call-count = 0 on `GetByIdAsync`).

### NFR-4: Security
No change. The refactor touches read-side enrichment only; no new data is exposed and no auth boundaries shift.

## Data Model
No schema changes. Only the catalog **interface** gains a new method. The underlying `CatalogAggregate` (return type of `GetByIdAsync` today) is reused unchanged.

Entities involved:
- `CatalogAggregate` — read-only, contains `ProductCode`, `Image`, `Stock.Eshop`, and other fields not relevant to this change.
- `TransportBoxItemDto` and the gift-package ingredient DTO — unchanged.

## API / Interface Design

### Repository interface change
Add one method to the catalog repository interface that Logistics consumes:

```csharp
Task<IReadOnlyList<CatalogAggregate>> GetByIdsAsync(
    IEnumerable<string> productCodes,
    CancellationToken cancellationToken = default);
```

### Implementation strategy
The concrete implementation must:
- Deduplicate incoming codes internally.
- Issue a **single** underlying lookup (e.g., EF `Where(x => codes.Contains(x.ProductCode))` materialized to a list; a multi-key cache fetch where available).
- Return matched items only; do not throw on missing codes.
- Honor the `CancellationToken` and propagate `OperationCanceledException` on cancellation.

### Public API
No public HTTP/MediatR contract changes. `GetTransportBoxByCode` and `GetGiftPackageDetail` request and response shapes are unchanged. No OpenAPI regeneration required for consumers.

## Dependencies
- **Catalog repository interface** consumed by Logistics today (shared catalog interface, or Logistics-owned interface per #1960 if it has merged). The new method lands on whichever is current.
- **Issue #1960** — Logistics-owned catalog interface refactor. Not a blocker. The implementer must check #1960's status before choosing the target interface; if a PR for #1960 is in flight, coordinate ordering to avoid merge conflicts on the interface file.
- **Concrete catalog repository / backing store** — must be capable of efficient batched fetch (EF or SQL `IN (...)`, or a cache provider with multi-get). If today's repository is layered over a per-key cache without multi-get, see FR-4 for acceptable implementation strategies.

## Out of Scope
- Batching catalog lookups in handlers outside Logistics (Manufacturing, Catalog, Reporting). Only the two handlers named in the brief are in scope.
- Adding caching, prefetching, or memoization beyond the batched lookup.
- Building benchmarking infrastructure or load-test scenarios. A one-off measurement attached to the PR to validate the win is sufficient.
- Changes to the `CatalogAggregate` shape or catalog-side projections.
- Resolving #1960 itself — this work coexists with #1960 but does not subsume it.
- UI changes to the scan or gift-package screens.

## Open Questions

None — design decisions documented above. The remaining implementation-time judgment calls (target interface depending on #1960 status; exact missing-item behavior in `GiftPackageManufactureService` after reading the unquoted source) are localized to the implementer and do not require product clarification.

## Status: COMPLETE