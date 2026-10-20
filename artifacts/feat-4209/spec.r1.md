# Specification: Decouple Packaging and ExpeditionList from ShoptetOrders.IEshopOrderClient

## Summary
Three Application-layer handlers in the `Packaging` and `ExpeditionList` modules inject `IEshopOrderClient` directly from the `ShoptetOrders` module, violating the codebase's consumer-owns-contract module boundary rule. This spec defines two new narrow, consumer-owned interfaces — `IPackedOrderStatusUpdater` (Packaging) and `IOrderStatusReader` (ExpeditionList) — each implemented by a `ShoptetOrders`-side adapter, following the existing `IShipmentDeliveryChecker` / `ILeafletKnowledgeSource` pattern.

## Background
`IEshopOrderClient` is defined inside `Anela.Heblo.Application.Features.ShoptetOrders` (not in a `Contracts/` folder) and exposes 11 methods. Three handlers in two other feature modules reference it directly:

- `Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` — uses `MarkAsPackedAsync`
- `Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs` — uses `MarkAsPackedAsync`
- `ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs` — uses `GetOrderStatusIdAsync`

Issue #3721 previously fixed the analogous `IPackingOrderClient` violation in the same Packaging handlers but did not address `IEshopOrderClient`, and `ExpeditionList` was never covered. This is a direct architectural-debt follow-up filed by the automated arch-review routine, referencing the already-established pattern in the codebase (`ShipmentLabelsShipmentDeliveryCheckerAdapter` implementing consumer-owned `IShipmentDeliveryChecker`).

This is a pure internal refactor: no behavior, API surface, or data model changes are in scope. The goal is strictly to fix module coupling by introducing consumer-owned narrow contracts and provider-side adapters.

## Functional Requirements

### FR-1: Define `IPackedOrderStatusUpdater` contract owned by Packaging
Add an interface `IPackedOrderStatusUpdater` to `Packaging.Contracts/` (mirroring the location/pattern used for other consumer-owned contracts such as `IShipmentDeliveryChecker`) with exactly one method matching the shape of `IEshopOrderClient.MarkAsPackedAsync` currently used by the two Packaging handlers.

**Acceptance criteria:**
- `IPackedOrderStatusUpdater` is defined under `Anela.Heblo.Application/Features/Packaging/Contracts/` (or the module's established `Contracts` location — verify exact existing path convention from `IShipmentDeliveryChecker` before implementing).
- The interface exposes only the single method needed (`MarkAsPackedAsync`, matching the existing signature's parameters and return type exactly, so no caller logic must change beyond the injected type).
- No dependency from `Packaging.Contracts` back to `ShoptetOrders`.

### FR-2: Implement ShoptetOrders-side adapter for `IPackedOrderStatusUpdater`
Add an adapter class in `ShoptetOrders` (e.g. `EshopOrderClientPackedOrderStatusUpdaterAdapter`, following the naming convention of `ShipmentLabelsShipmentDeliveryCheckerAdapter`) that implements `IPackedOrderStatusUpdater` by delegating to the existing `IEshopOrderClient.MarkAsPackedAsync`.

**Acceptance criteria:**
- Adapter lives in the `ShoptetOrders` module (alongside or near `IEshopOrderClient`'s implementation).
- Adapter is registered in DI as the implementation of `IPackedOrderStatusUpdater` (mirroring how the existing adapter pattern registers `IShipmentDeliveryChecker`'s implementation).
- Adapter delegates 1:1 to `IEshopOrderClient.MarkAsPackedAsync` with no behavior change.

### FR-3: Replace `IEshopOrderClient` with `IPackedOrderStatusUpdater` in Packaging handlers
Update `ScanPackingOrderHandler` and `CompletePackingOrderHandler` to depend on `IPackedOrderStatusUpdater` instead of `IEshopOrderClient`.

**Acceptance criteria:**
- Neither handler references `IEshopOrderClient` or any `ShoptetOrders` namespace/type after the change.
- Constructor injection updated; call site updated to call the new interface's method.
- Existing unit tests for both handlers pass after updating their mocks/fakes to the new interface (test doubles for `IEshopOrderClient` replaced with test doubles for `IPackedOrderStatusUpdater`).

### FR-4: Define `IOrderStatusReader` contract owned by ExpeditionList
Add an interface `IOrderStatusReader` to `ExpeditionList.Contracts/` with exactly one method matching the shape of `IEshopOrderClient.GetOrderStatusIdAsync` currently used by `PrintExpeditionOrderHandler`.

**Acceptance criteria:**
- `IOrderStatusReader` is defined under `Anela.Heblo.Application/Features/ExpeditionList/Contracts/` (or the module's established `Contracts` location).
- The interface exposes only `GetOrderStatusIdAsync`, matching the existing signature's parameters and return type exactly.
- No dependency from `ExpeditionList.Contracts` back to `ShoptetOrders`.

### FR-5: Implement ShoptetOrders-side adapter for `IOrderStatusReader`
Add an adapter class in `ShoptetOrders` (e.g. `EshopOrderClientOrderStatusReaderAdapter`) that implements `IOrderStatusReader` by delegating to `IEshopOrderClient.GetOrderStatusIdAsync`.

**Acceptance criteria:**
- Adapter lives in the `ShoptetOrders` module.
- Adapter is registered in DI as the implementation of `IOrderStatusReader`.
- Adapter delegates 1:1 to `IEshopOrderClient.GetOrderStatusIdAsync` with no behavior change.

### FR-6: Replace `IEshopOrderClient` with `IOrderStatusReader` in ExpeditionList handler
Update `PrintExpeditionOrderHandler` to depend on `IOrderStatusReader` instead of `IEshopOrderClient`.

**Acceptance criteria:**
- `PrintExpeditionOrderHandler` does not reference `IEshopOrderClient` or any `ShoptetOrders` namespace/type after the change.
- Constructor injection updated; call site updated to call the new interface's method.
- Existing unit tests for the handler pass after updating mocks/fakes to `IOrderStatusReader`.

### FR-7: Verify no remaining direct `ShoptetOrders` coupling
After FR-1 through FR-6, confirm no Application-layer type outside `ShoptetOrders` references `IEshopOrderClient` or any other internal `ShoptetOrders` type directly.

**Acceptance criteria:**
- A repo-wide search for `IEshopOrderClient` usage outside the `ShoptetOrders` feature folder and its adapters returns no results.
- `docs/architecture/development_guidelines.md` module-boundary rule ("no direct references between feature modules") holds for `Packaging` and `ExpeditionList` with respect to `ShoptetOrders`.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected; adapters are thin delegation wrappers with no added I/O, caching, or transformation logic. No specific latency target beyond "no regression."

### NFR-2: Security
No change to authN/authZ, data sensitivity, or external API surface. This is an internal DI/interface refactor only.

### NFR-3: Backward compatibility
No change to `IEshopOrderClient` itself, its implementation, or any external-facing behavior (e.g. Shoptet API calls, order packing/status flows). Existing callers within `ShoptetOrders` are unaffected.

## Data Model
No data model changes. No new entities, DTOs, or persisted state. Only two new C# interfaces (contracts) and two new adapter classes, plus DI registration changes.

## API / Interface Design

```
Packaging.Contracts.IPackedOrderStatusUpdater
    Task MarkAsPackedAsync(<same params as IEshopOrderClient.MarkAsPackedAsync>)

ExpeditionList.Contracts.IOrderStatusReader
    Task<...> GetOrderStatusIdAsync(<same params as IEshopOrderClient.GetOrderStatusIdAsync>)
```

Provider-side (ShoptetOrders):
```
EshopOrderClientPackedOrderStatusUpdaterAdapter : IPackedOrderStatusUpdater
    — delegates to IEshopOrderClient.MarkAsPackedAsync

EshopOrderClientOrderStatusReaderAdapter : IOrderStatusReader
    — delegates to IEshopOrderClient.GetOrderStatusIdAsync
```

Exact method signatures (parameter names/types/return types) must be copied verbatim from the current `IEshopOrderClient` members referenced by the three handlers — the architect/developer should read those signatures directly from source before implementing, rather than guessing from this spec.

DI registration: both adapters registered in the `ShoptetOrders` module's DI extension (wherever `IEshopOrderClient`'s own registration and the existing `IShipmentDeliveryChecker`-style adapter registrations live), so `Packaging` and `ExpeditionList` only need their own interfaces registered/resolved, not `IEshopOrderClient`.

## Dependencies
- Existing `IEshopOrderClient` interface and its current implementation (untouched, just no longer consumed directly by other modules).
- Existing precedent pattern: `IShipmentDeliveryChecker` / `ShipmentLabelsShipmentDeliveryCheckerAdapter` and `ILeafletKnowledgeSource` — architect and developer should read these for the exact file layout, naming, and DI registration convention to replicate.
- No external service or library dependencies introduced.

## Out of Scope
- Any change to `IEshopOrderClient`'s own definition, implementation, or the other 9 methods it exposes that aren't used by these three handlers.
- Addressing coupling in any other module or handler not listed in the Finding (this issue only covers the three named handlers).
- Any change to Shoptet API integration behavior.
- Renaming or restructuring the `ShoptetOrders` module itself beyond adding the two adapters.
- Re-addressing `IPackingOrderClient` (already fixed in #3721).

## Open Questions

None.

## Status: COMPLETE
