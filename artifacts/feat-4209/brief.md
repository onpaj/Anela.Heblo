## Module
ShoptetOrders

## Finding
Three Application-layer handlers in two other modules import and inject `IEshopOrderClient` directly from the `ShoptetOrders` namespace, coupling those modules to `ShoptetOrders` without following the consumer-owns-contract pattern:

- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs:3,17` — injects `IEshopOrderClient` for `MarkAsPackedAsync`
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs:1,11` — injects `IEshopOrderClient` for `MarkAsPackedAsync`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs:3,24` — injects `IEshopOrderClient` for `GetOrderStatusIdAsync`

`IEshopOrderClient` is defined in `Anela.Heblo.Application.Features.ShoptetOrders` (not in `Contracts/`). The `IShipmentDeliveryChecker` and `ILeafletKnowledgeSource` patterns in the same codebase show the correct approach: the consumer module owns the contract interface; the provider module implements an adapter.

Note: #3721 covered the related `IPackingOrderClient` violation for Packaging handlers. That issue has been closed, but `IEshopOrderClient` consumption in the same handlers was not addressed, and `ExpeditionList` was not covered at all.

## Why it matters
- Breaks module isolation: any change to `ShoptetOrders.IEshopOrderClient` can affect `Packaging` and `ExpeditionList` builds and tests
- Violates the documented rule "no direct references between feature modules" and the consumer-owns-contract pattern established by `IShipmentDeliveryChecker` / `ILeafletKnowledgeSource`
- `Packaging` only needs `MarkAsPackedAsync`; `ExpeditionList` only needs `GetOrderStatusIdAsync` — both drag in the full 11-method `IEshopOrderClient` interface for mocking

## Suggested fix
For `Packaging`: add `IPackedOrderStatusUpdater` with a single `MarkAsPackedAsync` method to `Packaging.Contracts/`. Have `ShoptetOrders` register an adapter implementing it (following the `ShipmentLabelsShipmentDeliveryCheckerAdapter` pattern). Replace the `IEshopOrderClient` injection in `ScanPackingOrderHandler` and `CompletePackingOrderHandler` with `IPackedOrderStatusUpdater`.

For `ExpeditionList`: add `IOrderStatusReader` with `GetOrderStatusIdAsync` to `ExpeditionList.Contracts/`. Have `ShoptetOrders` register an adapter. Replace the `IEshopOrderClient` injection in `PrintExpeditionOrderHandler`.

---
_Filed by daily arch-review routine on 2026-09-16._