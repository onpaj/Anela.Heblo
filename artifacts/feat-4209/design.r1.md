# Design: Decouple Packaging and ExpeditionList from ShoptetOrders.IEshopOrderClient

Backend-only refactor — no user-facing UI. Skip Design confirmed `true` in arch-review.r1.md.

## Component Design

### `Anela.Heblo.Application.Features.Packaging.Contracts.IPackedOrderStatusUpdater`
- **Owner:** `Packaging` module.
- **Responsibility:** The single operation Packaging needs to tell the order-management provider
  "this order has been packed." No other method — narrower than `IEshopOrderClient`'s 11 methods by design
  (spec FR-1).
- **Consumers:** `ScanPackingOrderHandler`, `CompletePackingOrderHandler` (constructor injection, replacing
  their current `IEshopOrderClient _eshopOrderClient` field).
- **Implementor:** `ShoptetOrdersPackedOrderStatusUpdaterAdapter` (see below). Packaging never references
  the implementing type or the `ShoptetOrders` namespace.

### `Anela.Heblo.Application.Features.ExpeditionList.Contracts.IOrderStatusReader`
- **Owner:** `ExpeditionList` module.
- **Responsibility:** The single operation ExpeditionList needs — read an order's current status id. No
  other method (spec FR-4).
- **Consumer:** `PrintExpeditionOrderHandler` (constructor injection, replacing its current
  `IEshopOrderClient _eshopOrderClient` field).
- **Implementor:** `ShoptetOrdersOrderStatusReaderAdapter` (see below).
- **Error-shape contract:** must let `HttpRequestException` (with `StatusCode == HttpStatusCode.NotFound`)
  propagate unmodified — `PrintExpeditionOrderHandler`'s existing `catch (HttpRequestException ex) when
  (ex.StatusCode == HttpStatusCode.NotFound)` block depends on that concrete shape surviving the adapter
  call. This is a behavioral contract of the interface, not just its method signature, and must be preserved
  exactly (arch-review.r1.md, Implementation Guidance / Risks).

### `Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.ShoptetOrdersPackedOrderStatusUpdaterAdapter`
- **Owner:** `ShoptetOrders` module (provider).
- **Responsibility:** Implements `IPackedOrderStatusUpdater` by delegating 1:1 to the existing, unchanged
  `IEshopOrderClient.MarkAsPackedAsync`. No logic, no mapping, no error translation.
- **Visibility:** `internal sealed class` (DI-resolved implementation detail — matches
  `ShipmentLabelsShipmentDeliveryCheckerAdapter` and `KnowledgeBaseLeafletSourceAdapter` conventions).
- **DI registration:** `ShoptetOrdersModule.AddShoptetOrdersModule`, lifetime mirrored from
  `IEshopOrderClient`'s own registration in `ShoptetApiAdapterServiceCollectionExtensions`.

### `Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure.ShoptetOrdersOrderStatusReaderAdapter`
- **Owner:** `ShoptetOrders` module (provider).
- **Responsibility:** Implements `IOrderStatusReader` by delegating 1:1 to the existing, unchanged
  `IEshopOrderClient.GetOrderStatusIdAsync`, without catching or wrapping any exception it throws.
- **Visibility:** `internal sealed class`.
- **DI registration:** `ShoptetOrdersModule.AddShoptetOrdersModule`, same lifetime approach as above.

### Unchanged components
- `IEshopOrderClient` and its Shoptet-facing implementation — no changes to signature, behavior, or
  registration.
- `ScanPackingOrderHandler`, `CompletePackingOrderHandler`, `PrintExpeditionOrderHandler` — no behavior
  changes; only the injected dependency type and the corresponding field/ctor-param name change at each of
  the three call sites already identified in the spec (FR-3, FR-6).

## Data Schemas

No database schema, persisted entity, or wire-level API contract changes. This section covers the two new
in-process C# interface contracts (the only "schema" this feature introduces):

```csharp
namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackedOrderStatusUpdater
{
    /// <summary>
    /// Transitions the order to the configured "packed" state (Shoptet "Zabaleno", id 52 by default).
    /// Mirrors IEshopOrderClient.MarkAsPackedAsync exactly; Packaging depends only on this narrower
    /// surface.
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
```

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IOrderStatusReader
{
    /// <summary>
    /// Returns the order's current Shoptet status id.
    /// Mirrors IEshopOrderClient.GetOrderStatusIdAsync exactly; may throw HttpRequestException with
    /// StatusCode == HttpStatusCode.NotFound when the order does not exist — callers depend on this
    /// exact exception shape (see PrintExpeditionOrderHandler's 404 handling).
    /// </summary>
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
}
```

Both signatures are copied verbatim from the current `IEshopOrderClient.MarkAsPackedAsync` and
`IEshopOrderClient.GetOrderStatusIdAsync` members (confirmed against
`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` during architecture
review) — no parameter, type, or return-shape changes versus what the three handlers already call today.
