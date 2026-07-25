# Design: Invert `IShipmentClient` dependency in `CompleteDeliveredOrdersJob`

## Component Design

### `IShipmentDeliveryChecker` (new, consumer-owned contract)
- **Location:** `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs`
- **Namespace:** `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`
- **Responsibility:** Narrow, `ShoptetOrders`-owned read contract exposing exactly the single shipment-delivery-status fact the module consumes. Mirrors the existing sibling pattern of `IPackingCarrierCoolingSource` and `IPackingProductSource` in the same folder.
- **Members:** `Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)` — the only member; no other methods, no speculative additions.
- **Consumers:** `CompleteDeliveredOrdersJob` (constructor-injected).
- **Implementers:** `ShipmentLabelsShipmentDeliveryCheckerAdapter` (see below). No other implementation should exist.

### `ShipmentLabelsShipmentDeliveryCheckerAdapter` (new, provider-owned adapter)
- **Location:** `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs` (new `Infrastructure/` folder under `ShipmentLabels`, mirroring `CarrierCooling/Infrastructure/` and `Catalog/Infrastructure/`)
- **Namespace:** `Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure`
- **Visibility:** `internal sealed`, matching `CarrierCoolingPackingCarrierCoolingAdapter` / `CatalogPackingProductSourceAdapter`.
- **Responsibility:** Implements `IShipmentDeliveryChecker` by pure delegation to the existing provider-owned `IShipmentClient`. No mapping, no additional logic, no side effects, no new I/O.
- **Dependency:** `IShipmentClient` only (already registered as a typed `HttpClient` in `ShoptetApiAdapterServiceCollectionExtensions.cs`; unchanged).
- **Contract:** `HasDeliveredShipmentAsync(orderCode, ct)` forwards its arguments unmodified to `IShipmentClient.HasDeliveredShipmentAsync(orderCode, ct)` and returns its result unmodified.

### `CompleteDeliveredOrdersJob` (modified)
- **Location:** `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`
- **Change:** Constructor/field dependency retyped from `Anela.Heblo.Application.Features.ShipmentLabels.IShipmentClient` (6-method provider interface) to `Anela.Heblo.Application.Features.ShoptetOrders.Contracts.IShipmentDeliveryChecker` (1-method consumer contract).
- **Behavior:** No change. `ExecuteAsync` control flow, logging, dry-run handling, and the single call site (`_shipmentClient.HasDeliveredShipmentAsync(order.Code, cancellationToken)`) are unchanged, since the invoked method's signature is identical on both interfaces.
- **Job metadata** (`JobName`, `DisplayName`, `Description`, `CronExpression`, `DefaultIsEnabled`): unchanged.

### `ShipmentLabelsModule` (modified)
- **Location:** `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs`
- **Change:** `AddShipmentLabelsModule` registers `services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();`, with a comment identifying it as a cross-module contract owned by `ShoptetOrders` and implemented by `ShipmentLabels` — following the convention in `CarrierCoolingModule.cs`.
- **Constraint:** `ShoptetOrdersModule.AddShoptetOrdersModule` must **not** register this binding — the consumer module never registers the provider's implementation, per the documented cross-module pattern.

### `ModuleBoundariesTests` (modified, required per architecture review amendment)
- **Location:** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`
- **Change:** Add a `ModuleBoundaryRule` entry `"ShoptetOrders -> ShipmentLabels"` with `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ShoptetOrders"` and `ForbiddenNamespacePrefixes` including `"Anela.Heblo.Application.Features.ShipmentLabels"`, empty allowlist. This pins the boundary so `ShoptetOrders` can never again reference `ShipmentLabels` types directly (including `IShipmentClient`), closing the exact regression this fix addresses. `ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs` does not reference any `ShipmentLabels` type and will not trip the rule.

### `CompleteDeliveredOrdersJobTests` (modified)
- **Location:** `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs`
- **Change:** `MakeSut` constructs `Mock<IShipmentDeliveryChecker>` instead of `Mock<IShipmentClient>`; `using` statements updated accordingly (drop `Anela.Heblo.Application.Features.ShipmentLabels`, add `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`). All 9 existing test methods keep their setups/verifies against `HasDeliveredShipmentAsync` unchanged, since the method signature is identical across both interfaces.

### `ShipmentLabelsShipmentDeliveryCheckerAdapterTests` (new, per architecture review amendment)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs`, mirroring the `CarrierCooling` adapter test's namespace/folder convention.
- **Responsibility:** A single `[Fact]` verifying the adapter delegates `HasDeliveredShipmentAsync(orderCode, ct)` to the mocked `IShipmentClient` with the same arguments and returns its result unmodified.

### Unchanged components (explicitly out of scope)
- `IShipmentClient` and `ShoptetShipmentClient` (`Anela.Heblo.Adapters.ShoptetApi`) — method surface and HTTP behavior unmodified.
- `ShoptetApiAdapterServiceCollectionExtensions.cs`'s `AddHttpClient<IShipmentClient, ShoptetShipmentClient>(...)` registration.
- `ShipmentLabels`' own handlers (`GetOrderShipmentLabelsHandler`, `CreateOrderShipmentHandler`) — continue injecting `IShipmentClient` directly, since `ShipmentLabels` is the provider/owner of that interface.
- `Packaging` module's seven `IShipmentClient` consumers (`GetOrderTrackingNumberHandler`, `GetPackageLabelPdfHandler`, `ScanPackingOrderHandler`, `GetOrderTrackingNumbersHandler`, `DeletePackageHandler`, `ResetOrderShipmentHandler`, `FillTrackingNumbersJob`) — not part of this refactor.

## Data Schemas

No persisted data model, database schema, or HTTP API surface changes. This is a pure in-process interface/adapter refactor.

### In-process contract shape (new)
```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IShipmentDeliveryChecker
{
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
}
```

**Input:** `orderCode: string` — Shoptet order code, identical to the value currently passed to `IShipmentClient.HasDeliveredShipmentAsync`.
**Output:** `Task<bool>` — `true` if any shipment for the order is reported delivered, identical semantics to the existing `IShipmentClient` method it wraps.

### DI registration shape (new)
```csharp
// In ShipmentLabelsModule.AddShipmentLabelsModule
services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();
```

### Downstream (unchanged)
The runtime call chain terminates at the same Shoptet HTTP call as before:
`IShipmentDeliveryChecker` → `ShipmentLabelsShipmentDeliveryCheckerAdapter` → `IShipmentClient` → `ShoptetShipmentClient` → Shoptet `GET /api/shipments?orderCode={code}`.
No request/response payload shapes change; one additional virtual dispatch is introduced with no new I/O.
