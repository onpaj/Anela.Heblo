# Specification: Replace ApplyEnrichment call in ShoptetApiPackingOrderClient with direct cooling enrichment

## Summary
`ShoptetApiPackingOrderClient.GetPackingOrderAsync` currently enriches packing-order items by calling the shared `ShoptetApiExpeditionListSource.ApplyEnrichment` static method, passing two `Dictionary` instances (`stockByCode`, `locationByCode`) that are always empty and serve no purpose at this call site — only `coolingByCode` is ever populated and used. This change replaces that call with a small in-line loop that applies only the cooling enrichment, removing the unnecessary allocations, the misleading empty-dictionary arguments, and the packing path's static coupling to the expedition-list enrichment method.

## Background
`ApplyEnrichment` (in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`, lines 272–290) is a shared internal helper originally built for the expedition/picking-list path (`CreatePickingList` → `BatchAndFlushAsync`), where stock, warehouse location, cooling, and (optionally) price are all enriched onto `ExpeditionOrderItem`s from real data sources.

`ShoptetApiPackingOrderClient.GetPackingOrderAsync` (in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`, lines 76–80) reuses this same method but only ever has real data for `coolingByCode` (sourced from `_productSource.GetByCodesAsync`); it passes `new Dictionary<string, decimal>()` and `new Dictionary<string, string>()` for the stock and location parameters purely to satisfy the method signature. These two dictionaries are never populated, so the `stock`/`location` branches inside `ApplyEnrichment` are always no-ops for this call site, and the `priceByCode` optional parameter is left at its default (`null`), which also disables the price branch.

This is a pure internal-quality fix identified by the arch-review routine: no behavior changes, no new dependencies, no data-model or API changes. It only affects the private implementation of one method in one adapter class.

## Functional Requirements

### FR-1: Apply only cooling enrichment at the packing call site
Replace the `ShoptetApiExpeditionListSource.ApplyEnrichment(...)` call inside `GetPackingOrderAsync` with an in-line loop over `order.Items` that sets `item.Cooling` from `coolingByCode` when a match exists — reproducing exactly the cooling branch of `ApplyEnrichment` (`if (coolingByCode.TryGetValue(item.ProductCode, out var cooling)) item.Cooling = cooling;`) and nothing else.

**Acceptance criteria:**
- `GetPackingOrderAsync` no longer references `ShoptetApiExpeditionListSource.ApplyEnrichment`.
- No `Dictionary<string, decimal>()` / `Dictionary<string, string>()` are allocated for stock/location purposes in this method.
- For any `order.Items[i]` whose `ProductCode` is a key in `coolingByCode`, `item.Cooling` is set to `coolingByCode[ProductCode]` — identical to current behavior.
- For any `order.Items[i]` whose `ProductCode` is **not** a key in `coolingByCode`, `item.Cooling` is left unchanged (its existing/default value) — identical to current behavior.
- All other fields on `ExpeditionOrderItem` (`StockCount`, `WarehousePosition`, `UnitPrice`) are left untouched by this method, matching current behavior (they were already no-ops before this change since the dictionaries were always empty and `priceByCode` was `null`).
- The public behavior and return value of `GetPackingOrderAsync` (the resulting `PackingOrder`/`PackingOrderItem` values, including each item's cooling-derived fields) are unchanged for every existing input.

### FR-2: Preserve `ApplyEnrichment` for the expedition/picking-list path
`ShoptetApiExpeditionListSource.ApplyEnrichment` itself is not modified, removed, or renamed — it is still needed by (or reachable from) the picking-list path. Only the packing-order call site's usage of it is removed.

**Acceptance criteria:**
- `ApplyEnrichment`'s signature and body are unchanged.
- No other call site of `ApplyEnrichment` is affected. (Note: at the time of this spec, a full-repo check should confirm `GetPackingOrderAsync` is the only caller before deciding whether `ApplyEnrichment` remains otherwise-unused — see Open Questions.)

## Non-Functional Requirements

### NFR-1: Performance
Eliminates two `Dictionary<TKey,TValue>` allocations (and their default-capacity backing arrays) per `GetPackingOrderAsync` call, i.e., per packing-screen load. No new allocations are introduced; the replacement loop iterates `order.Items` once, same as the removed call already did internally.

### NFR-2: Behavior parity / risk
This is a refactor with zero intended behavior change. Risk is limited to: (a) accidentally changing iteration semantics (e.g., materializing `order.Items` differently), or (b) accidentally applying stock/location logic that this call site never had. Both are mitigated by keeping the replacement to the exact one-line cooling branch already used inside `ApplyEnrichment`.

## Data Model
No data model changes. `ExpeditionOrderItem.Cooling`, `.StockCount`, `.WarehousePosition`, `.UnitPrice` are all pre-existing fields; only `.Cooling` is touched by the modified code path.

## API / Interface Design
No public API, contract, or DTO changes. This is a private-method-body change inside `ShoptetApiPackingOrderClient` (internal adapter class implementing `IPackingOrderClient` / `IPackingOrderCountSource`). No changes to `IPackingOrderClient`, `PackingOrder`, or `PackingOrderItem`.

## Dependencies
None beyond what already exists: `_productSource.GetByCodesAsync` (already called, unchanged), `Cooling` type from `Anela.Heblo.Domain.Features.Logistics` (already referenced transitively).

## Out of Scope
- Any change to `ShoptetApiExpeditionListSource.ApplyEnrichment` itself, or to the picking-list (`CreatePickingList`) code path.
- Any change to `ShoptetApiPackingOrderClient`'s other methods (`GetOrdersBeingPackedCountAsync`, `GetOrdersBeingProcessedCountAsync`) or its carrier-cooling resolution logic (`ResolveCarrierCooling` usage stays as-is).
- Broader deduplication/refactor of enrichment logic shared between the expedition-list and packing-order paths (e.g., extracting a smaller shared "apply cooling" helper). The brief explicitly asks for the smallest change (in-line loop), not a new shared abstraction.
- Any test-suite restructuring beyond what is needed to cover this method's cooling-enrichment behavior.

## Open Questions

None. The brief and codebase together fully determine the change: this spec assumes the in-line replacement shown in the brief's "Suggested fix" is implemented verbatim inside `GetPackingOrderAsync`, and that no other call site depends on `ApplyEnrichment` receiving empty stock/location dictionaries from this class (confirmed by reading `ShoptetApiPackingOrderClient.cs` in full — it is the only caller within that file, and grep for `ApplyEnrichment` usages should be part of implementation to confirm no other adapter calls it the same way).

## Status: COMPLETE
