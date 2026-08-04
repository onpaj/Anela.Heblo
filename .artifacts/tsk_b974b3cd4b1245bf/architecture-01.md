# Architecture review: Split `IPackingOrderClient` (design-01.md)

## Verdict: design-01.md is approved as written. No changes required before implementation.

I re-verified every factual claim in `design-01.md` and `plan-01.md` directly against the current
source tree (not just against each other). Everything checked out exactly as described — down to
specific line numbers. Details below, organized by invariant.

## 1. Consumer-owns-the-contract pattern (`development_guidelines.md:226-239`, `ILeafletKnowledgeSource`)

The documented pattern is: **consumer** defines the interface in its own `Contracts/` folder,
**provider** implements an adapter and registers the DI binding from its own module.

Design-01 puts the new `IPackingOrderCountSource` in `Packaging/Contracts/` — Packaging is the
actual (and only) consumer of the two count methods, confirmed by direct read of both call sites:
- `GetPackingDashboardHandler.cs:42,44` — `_packingOrderClient.GetOrdersBeingPackedCountAsync` / `GetOrdersBeingProcessedCountAsync`
- `PackingStatsTile.cs:53,55` — same two calls

This matches the pattern correctly. ✅

**One nuance worth naming explicitly (not a defect, but worth the implementer understanding why):**
the *provider* side does **not** follow the "adapter lives in the provider module's own
`Infrastructure/`" half of the ILeafletKnowledgeSource pattern. Instead `ShoptetApiPackingOrderClient`
(in the external `Anela.Heblo.Adapters.ShoptetApi` project) implements the interface directly, and
DI is registered in that project's own `ShoptetApiAdapterServiceCollectionExtensions.cs`, not in
`ShoptetOrdersModule.cs` (which I confirmed registers nothing beyond `IOptions<ShoptetOrdersSettings>`
— `ShoptetOrdersModule.cs:8-16`). This is **not** a violation: it's simply a different, equally
established pattern already in use for the *existing* `IPackingOrderClient` registration
(`ShoptetApiAdapterServiceCollectionExtensions.cs:119`). Standard Clean Architecture ports-and-adapters
(external infrastructure project implements an Application-layer interface) is distinct from the
intra-Application cross-module pattern that `IPackingCarrierCoolingSource`/`IPackingProductSource`
exemplify (those are implemented by `Features/Catalog/Infrastructure/CatalogPackingProductSourceAdapter.cs`
and `Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs` — genuine
intra-Application module boundaries). Design-01's plan to have `ShoptetApiPackingOrderClient`
implement `IPackingOrderCountSource` directly, registered in the adapter project, is consistent
with the precedent already set by `IPackingOrderClient` itself. Correct call.

## 2. Naming convention (`IPacking<Noun>Source`)

Confirmed against `ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs` and
`IPackingProductSource.cs` — both follow the `IPacking<Noun>Source` shape. `IPackingOrderCountSource`
matches stylistically. It lives in a different folder (`Packaging/Contracts/`, a new folder — verified
no `Contracts/` folder currently exists under `Packaging/`) because it's owned by a different
module, which is correct per §1 — the design only borrows the naming style, not the location, and
says so. ✅

## 3. `GetPackingOrderAsync` fetch — Option A (leave shared, track `ShipmentLabels`)

Verified the three real consumers and confirmed the design touches none of them:
- `ScanPackingOrderHandler.cs:51`, `ResetOrderShipmentHandler.cs:58`, `CreateOrderShipmentHandler.cs:49`
  all call only `GetPackingOrderAsync` — none call either count method. Untouched, as the design states.
- Confirmed the pinned `Packaging -> ShoptetOrders` `ModuleBoundaryRule` at
  `ModuleBoundariesTests.cs:606-613`, and its `PackagingShoptetOrdersAllowlist` at lines 308-342 with
  the "2026-06-05 decoupling" comment — matches plan-01's account exactly.
- Confirmed **no** `ShipmentLabels -> ShoptetOrders` rule exists today (only the reverse,
  `ShoptetOrders -> ShipmentLabels`, at lines 650-657) — the gap plan-01/design-01 describe is real,
  not a misreading.
- Confirmed the `Rules` static list feeds a single `[Theory][MemberData]` test
  (`Consumer_types_should_not_reference_provider_owned_namespaces`, line 662) that walks
  `EnumerateReferencedTypes` per type — adding one more `ModuleBoundaryRule` entry is a mechanical,
  low-risk addition consistent with every other rule in the list. No stale-allowlist-entry
  assertion exists in the test (verified — no "unused"/"stale" check), so trimming the two
  `GetPackingDashboardHandler`/`PackingStatsTile` allowlist lines (335-341, confirmed byte-for-byte
  against the design's cited range) is good hygiene but not required for the test to pass either
  way — worth knowing so the implementer doesn't over-invest verifying that step.

Rejecting Option B (interface-per-consuming-module) is the right call: it would triple the fetch
interface for zero behavioral gain and contradicts both "surgical changes" and the prior deliberate
2026-06-05 decision. Agree with the recommendation.

## 4. Adapter implementation impact

Read `ShoptetApiPackingOrderClient.cs` in full. Confirmed:
- Method bodies for `GetOrdersBeingPackedCountAsync`/`GetOrdersBeingProcessedCountAsync` are already
  exactly as design-01 describes (delegate to `_orderClient.GetOrdersByStatusAsync(...)`) — no logic
  changes needed, only the class's implemented-interfaces list gains `IPackingOrderCountSource`.
- The class holds only injected dependencies/readonly config values — genuinely stateless, so the
  design's claim that two separate `AddTransient` registrations pointing at the same concrete type
  carry no shared-state risk is correct. This is also the existing pattern for `IPickingListSource`
  registered independently at the line right above (`ShoptetApiAdapterServiceCollectionExtensions.cs:118-119`).

## 5. Test impact

Confirmed both test files the design calls out for mock-type changes exist:
`backend/test/Anela.Heblo.Tests/Features/Packaging/GetPackingDashboardHandlerTests.cs` and
`.../DashboardTiles/PackingStatsTileTests.cs`. No other test file references the two count methods.

## Risks / prerequisites for implementation

None blocking. Two minor implementation-time notes (not scope changes):
1. When trimming `ModuleBoundariesTests.cs` allowlist lines 335-341, re-run the test after the
   `GetPackingDashboardHandler`/`PackingStatsTile` changes land — since no stale-entry check exists,
   a mistaken trim (e.g. leaving a dangling comment) won't be caught by the test itself; visual
   diff review is the only safety net.
2. The `ShipmentLabels -> ShoptetOrders` allowlist entry set in design-01 (§6) is explicitly flagged
   by the design itself as "starting point, confirm by running the test" — correct approach, since
   `EnumerateReferencedTypes` walks IL per member and compiler-generated async state-machine/closure
   types can surface as extra entries the design can't predict from static reading alone. Implementer
   should treat the test run, not the snippet, as authoritative.

## Scope confirmation

In scope matches plan-01/design-01: new `Packaging/Contracts/IPackingOrderCountSource.cs`, narrowed
`IPackingOrderClient`, two-interface adapter, one DI line, two consumer swaps, `ModuleBoundariesTests.cs`
trim + new rule, two test files' mock types. Out of scope confirmed untouched: `IEshopOrderClient`,
`PackingOrder`/`PackingOrderItem` DTO placement, the three `GetPackingOrderAsync`-only consumers,
`CompletePackingOrderHandler -> IEshopOrderClient` allowlist entry.
