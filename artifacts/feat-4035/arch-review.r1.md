# Architecture Review: Replace ApplyEnrichment call in ShoptetApiPackingOrderClient with direct cooling enrichment

## Skip Design: true
Pure backend refactor of one private method body. No new/changed UI, no API contract change, no new interfaces. There is nothing for a designer to add here.

## Architectural Fit Assessment
This fits cleanly into the existing Vertical Slice / Clean Architecture layout: `ShoptetApiPackingOrderClient` lives in `Anela.Heblo.Adapters.ShoptetApi` and implements the Application-layer port `IPackingOrderClient`. The change is entirely internal to that adapter method — no port, contract, or cross-module boundary is touched.

I confirmed by reading the code (not just the brief) that `ApplyEnrichment` currently has exactly two call sites:

1. `ShoptetApiPackingOrderClient.GetPackingOrderAsync` (the one in scope) — passes empty `stockByCode`/`locationByCode` and no `priceByCode`, so only the cooling branch inside `ApplyEnrichment` ever does anything here.
2. `PickingListBatchProcessor.WriteEnrichmentAsync` (Expedition/`PickingListBatchProcessor.cs:89`, used by the `CreatePickingList`/picking-list flow) — passes real `stockByCode`, `locationByCode`, `coolingByCode`, and `priceByCode`, i.e. it genuinely needs all four branches of `ApplyEnrichment`.

This confirms the brief's premise and de-risks the change: `ApplyEnrichment` stays exactly as-is for the picking-list path; only the packing-order call site changes.

**Important behavioral fact confirmed during exploration** (this raises the stakes on getting the replacement exactly right, it does not change the fix): the per-item `Cooling` set by `ApplyEnrichment` is **not** dead code, even though `PackingOrderItem` (the DTO returned to callers, `Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs:57`) has no `Cooling` field at all. `ExpeditionOrder.IsCooled` (`Expedition/ExpeditionProtocolData.cs:25`) is a computed property:
```csharp
public bool IsCooled => Items.Any(i => i.Cooling != Cooling.None && i.Cooling <= CarrierCooling);
```
and `GetPackingOrderAsync` reads `order.IsCooled` into the returned `PackingOrder.IsCooled` (`ShoptetApiPackingOrderClient.cs:114`) — *after* `ApplyEnrichment` has run and set each item's `.Cooling`. So the cooling enrichment this call performs is load-bearing for `PackingOrder.IsCooled`, and is already covered by two existing unit tests:
- `GetPackingOrderAsync_ComputesCooling_FromCarrierMatrixAndCatalog` (asserts `IsCooled == true`)
- `GetPackingOrderAsync_NotCooled_WhenCarrierMatrixEmpty` (asserts `IsCooled == false`)

These two tests are sufficient regression coverage for behavior parity and should keep passing unmodified after the fix — no new test is architecturally required, though the developer may add one that directly asserts per-item `Cooling` if desired (optional, not blocking).

## Proposed Architecture

### Component Overview
No component boundaries change. Only the body of one method changes:

```
ShoptetApiPackingOrderClient.GetPackingOrderAsync(code, ct)
  ├─ IShoptetExpeditionOrderSource.GetExpeditionOrderDetailAsync   (unchanged)
  ├─ ShoptetApiExpeditionListSource.MapToExpeditionOrder            (unchanged, static, still used)
  ├─ IPackingCarrierCoolingSource.GetAllAsync + ResolveCarrierCooling (unchanged)
  ├─ IPackingProductSource.GetByCodesAsync → coolingByCode          (unchanged)
  ├─ [CHANGED] inline foreach over order.Items setting item.Cooling from coolingByCode
  │     (replaces: ShoptetApiExpeditionListSource.ApplyEnrichment(order.Items, {}, {}, coolingByCode))
  └─ build PackingOrder / PackingOrderItem list                     (unchanged)
```

`ShoptetApiExpeditionListSource.ApplyEnrichment` remains unchanged and is still called from `PickingListBatchProcessor`.

### Key Design Decisions

#### Decision 1: In-line loop vs. extracted shared helper
**Options considered:**
- (a) In-line `foreach` loop directly in `GetPackingOrderAsync`, exactly as the brief suggests.
- (b) Extract a small shared `internal static void ApplyCoolingEnrichment(IEnumerable<ExpeditionOrderItem> items, Dictionary<string, Cooling> coolingByCode)` helper (e.g. on `ShoptetApiExpeditionListSource` or a new small static class) and call it from both `GetPackingOrderAsync` and, optionally, refactor `ApplyEnrichment` to call it internally too.

**Chosen approach:** (a), the in-line loop, per the brief's explicit "smallest change" framing and the spec's Out-of-Scope section.

**Rationale:** The brief is explicit that a new shared abstraction is not wanted — the goal is removing a speculative coupling and two dead allocations with the minimum-risk change, not building new reuse. A single 4-line loop used at one call site does not meet the bar for extraction. If a second call site with the same narrow need ever appears, extraction can be revisited then (rule of three).

#### Decision 2: Where exactly the loop goes
**Options considered:**
- (a) Replace the `ApplyEnrichment(...)` call in place, at `ShoptetApiPackingOrderClient.cs:76-80`, keeping it before the `items = order.Items.Select(...)` projection (so `IsCooled` continues to be computed after `Cooling` is applied, matching current ordering).
- (b) Move cooling enrichment logic into the same LINQ projection that builds `items` at line 82-101.

**Chosen approach:** (a) — replace strictly in place.

**Rationale:** `order.Items` (the `ExpeditionOrder`'s own item list, of type `List<ExpeditionOrderItem>`) is a *different* collection from the projected `items` (`List<PackingOrderItem>`) built afterward. `order.IsCooled` is read later, from `order.Items`, not from the projected `items`. Folding the cooling assignment into the `items` projection would still mutate the same underlying `ExpeditionOrderItem` objects (since `.Select` doesn't copy them), so it would technically still work, but it splits one concern (cooling enrichment) across two unrelated blocks of code and makes the ordering dependency on `order.IsCooled` harder to see. Keeping the loop exactly where `ApplyEnrichment` was called preserves the existing, already-correct ordering and stays minimal.

## Implementation Guidance

### Directory / Module Structure
No new files. Single-method edit in:
`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs`

### Interfaces and Contracts
None changed. `IPackingOrderClient`, `PackingOrder`, `PackingOrderItem`, `ExpeditionOrderItem`, and `ShoptetApiExpeditionListSource.ApplyEnrichment`'s signature are all untouched.

### Data Flow
Unchanged end-to-end. The only difference is *how* `item.Cooling` gets set for each `ExpeditionOrderItem` in `order.Items` — via an in-line loop instead of a call into `ApplyEnrichment` with two throwaway dictionaries. Concretely, replace:

```csharp
ShoptetApiExpeditionListSource.ApplyEnrichment(
    order.Items,
    new Dictionary<string, decimal>(),
    new Dictionary<string, string>(),
    coolingByCode);
```

with:

```csharp
foreach (var item in order.Items)
{
    if (coolingByCode.TryGetValue(item.ProductCode, out var cooling))
        item.Cooling = cooling;
}
```

placed at the same location (immediately after `coolingByCode` is built, immediately before the `items = order.Items.Select(...)` projection).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Accidentally dropping the cooling assignment, silently breaking `PackingOrder.IsCooled` | Medium (silent, no compile error) | Copy the loop body verbatim from `ApplyEnrichment`'s existing cooling branch (already shown above); run the two existing `IsCooled`-asserting tests (`GetPackingOrderAsync_ComputesCooling_FromCarrierMatrixAndCatalog`, `GetPackingOrderAsync_NotCooled_WhenCarrierMatrixEmpty`) and confirm they still pass unmodified. |
| Confusing this method's `ExpeditionOrderItem` (`order.Items`) with the projected `PackingOrderItem` (`items`) and applying the loop to the wrong collection | Low | Loop stays exactly where the old call was, over `order.Items`, before the `items` projection — no new collection is introduced. |
| Regression in the unrelated `PickingListBatchProcessor` path if a developer "helpfully" also touches `ApplyEnrichment` | Low | Spec's Out-of-Scope explicitly forbids touching `ApplyEnrichment` or the picking-list path; this review confirms via `grep` that `PickingListBatchProcessor` is the only other caller and it passes real (non-empty) dictionaries, so it has a genuine reason to keep using `ApplyEnrichment` as-is. |

## Specification Amendments
None required. The spec (`spec.r1.md`) already correctly captures the fix and its acceptance criteria, and its Open Questions note (confirming `ApplyEnrichment` has other callers, so it must not be removed) is now fully resolved by this review: `PickingListBatchProcessor.cs:89` is that other caller, and it must keep using `ApplyEnrichment` unchanged.

## Prerequisites
None. No migrations, config, or infrastructure changes are needed — this is a self-contained code edit plus running the existing backend test suite.
