# Specification: Invert `IShipmentClient` dependency in `CompleteDeliveredOrdersJob` (ShoptetOrders → ShipmentLabels)

## Summary
`CompleteDeliveredOrdersJob` in the `ShoptetOrders` module injects `ShipmentLabels`' six-method `IShipmentClient` interface directly, even though it calls only one method (`HasDeliveredShipmentAsync`). This inverts the codebase's established cross-module contract-ownership rule (documented in `docs/architecture/development_guidelines.md`, "Cross-Module Communication Example: ILeafletKnowledgeSource") and is inconsistent with the two sibling contracts already used in the same module, `IPackingCarrierCoolingSource` and `IPackingProductSource`. This spec defines a narrow, `ShoptetOrders`-owned contract and a `ShipmentLabels`-owned adapter that replaces the direct `IShipmentClient` injection, with no change to job behavior.

## Background
`CompleteDeliveredOrdersJob` (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`) is a recurring job that scans Shoptet orders in "handed to carrier" states and moves them to "vyřízena" (completed) once any of their shipments is reported delivered. It currently depends on `Anela.Heblo.Application.Features.ShipmentLabels.IShipmentClient`, which exposes six methods: `GetLabelsByOrderCodeAsync`, `GetLatestActiveTrackingNumberAsync`, `HasDeliveredShipmentAsync`, `GetShippingOptionsAsync`, `CreateShipmentAsync`, `CancelShipmentAsync`. The job uses exactly one of these — `HasDeliveredShipmentAsync` — at line 99.

This codebase has an established, documented, and test-enforced pattern for cross-module reads (`docs/architecture/development_guidelines.md`, lines 220–239): the **consumer** module defines a narrow contract in its own `Contracts/` folder exposing only the operations it actually uses, and the **provider** module implements that contract via an adapter in its own `Infrastructure/` folder, registering the DI binding itself. This is demonstrated twice already inside `ShoptetOrders` itself:
- `IPackingCarrierCoolingSource` (`ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs`) — implemented by `CarrierCoolingPackingCarrierCoolingAdapter` in `CarrierCooling/Infrastructure/`, registered in `CarrierCoolingModule.cs`.
- `IPackingProductSource` (`ShoptetOrders/Contracts/IPackingProductSource.cs`) — implemented by `CatalogPackingProductSourceAdapter` in `Catalog/Infrastructure/`, registered in `CatalogModule.cs`.

`IShipmentClient` breaks this pattern: `ShoptetOrders` (consumer) depends directly on the full provider-owned (`ShipmentLabels`) interface. This creates unnecessary compile-time coupling — any change to `IShipmentClient` driven by `ShipmentLabels`' own needs (e.g. adding a new shipment-creation parameter) forces a rebuild/re-review of `ShoptetOrders`, and `CompleteDeliveredOrdersJobTests` must mock a five-method surface the job never touches.

Note: `IShipmentClient` is also legitimately consumed elsewhere — inside `ShipmentLabels`' own use cases (`GetOrderShipmentLabelsHandler`, `CreateOrderShipmentHandler`) where it's provider-internal, and by several handlers/jobs in the `Packaging` module (`GetOrderTrackingNumberHandler`, `GetPackageLabelPdfHandler`, `ScanPackingOrderHandler`, `GetOrderTrackingNumbersHandler`, `DeletePackageHandler`, `ResetOrderShipmentHandler`, `FillTrackingNumbersJob`). None of those are touched by this spec — see Out of Scope.

## Functional Requirements

### FR-1: Add a consumer-owned `IShipmentDeliveryChecker` contract in `ShoptetOrders`
Add a new interface to `ShoptetOrders/Contracts/` exposing exactly the operation `CompleteDeliveredOrdersJob` needs, matching the existing file/interface naming convention of `IPackingCarrierCoolingSource` / `IPackingProductSource` in the same folder.

File: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs`

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IShipmentDeliveryChecker
{
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
}
```

Note: the brief's sketch omits the `Task<bool>` return type — the return type here matches `IShipmentClient.HasDeliveredShipmentAsync`'s actual signature (`Task<bool>`, not `Task`), since the job branches on the boolean result (`if (!await _shipmentClient.HasDeliveredShipmentAsync(...)) continue;`).

**Acceptance criteria:**
- `IShipmentDeliveryChecker` is declared in namespace `Anela.Heblo.Application.Features.ShoptetOrders.Contracts`.
- It declares exactly one method: `Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)`.
- No implementation types, DTOs, or provider-owned types are referenced from this file.

### FR-2: Add a `ShipmentLabels`-owned adapter implementing the contract
Add an adapter class in `ShipmentLabels/Infrastructure/` (new folder, mirroring `CarrierCooling/Infrastructure/` and `Catalog/Infrastructure/`) that implements `IShipmentDeliveryChecker` by delegating to the existing `IShipmentClient`.

File: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs`

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;

internal sealed class ShipmentLabelsShipmentDeliveryCheckerAdapter : IShipmentDeliveryChecker
{
    private readonly IShipmentClient _shipmentClient;

    public ShipmentLabelsShipmentDeliveryCheckerAdapter(IShipmentClient shipmentClient)
    {
        _shipmentClient = shipmentClient;
    }

    public Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)
        => _shipmentClient.HasDeliveredShipmentAsync(orderCode, ct);
}
```

The adapter's constructor takes `IShipmentClient`, which is already registered as a typed `HttpClient` in `ShoptetApiAdapterServiceCollectionExtensions.cs` (`services.AddHttpClient<IShipmentClient, ShoptetShipmentClient>(...)`, in the `Anela.Heblo.Adapters.ShoptetApi` project) — no change needed to that registration.

**Acceptance criteria:**
- The adapter class is `internal sealed`, matching `CarrierCoolingPackingCarrierCoolingAdapter` / `CatalogPackingProductSourceAdapter` visibility.
- The adapter lives in the `Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure` namespace.
- The adapter's only dependency is `IShipmentClient`; it performs a pure delegation with no additional logic, mapping, or side effects.

### FR-3: Register the DI binding in `ShipmentLabelsModule`
Register `IShipmentDeliveryChecker` → `ShipmentLabelsShipmentDeliveryCheckerAdapter` inside `ShipmentLabelsModule.AddShipmentLabelsModule`, following the same lifetime and comment convention used in `CarrierCoolingModule.cs` (`services.AddTransient<IPackingCarrierCoolingSource, CarrierCoolingPackingCarrierCoolingAdapter>();` with an explanatory comment).

File: `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs`

**Acceptance criteria:**
- `ShipmentLabelsModule.AddShipmentLabelsModule` registers `services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();` (transient, matching the existing `IShipmentClient` HttpClient registration's implicit scoping and the sibling adapters' `AddTransient` usage).
- A comment is added above the registration stating this is a cross-module contract owned by `ShoptetOrders` and implemented by `ShipmentLabels`, per the pattern in `docs/architecture/development_guidelines.md`.
- `ShoptetOrdersModule.AddShoptetOrdersModule` is **not** modified — the consumer module never registers the provider's binding (per the documented pattern).

### FR-4: Replace `IShipmentClient` with `IShipmentDeliveryChecker` in `CompleteDeliveredOrdersJob`
Update `CompleteDeliveredOrdersJob` to depend on the new narrow contract instead of the full `IShipmentClient` surface. No change to control flow, logging, dry-run behavior, or any other logic.

File: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs`

Changes:
- Replace `using Anela.Heblo.Application.Features.ShipmentLabels;` with `using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;`.
- Replace the field `private readonly IShipmentClient _shipmentClient;` with `private readonly IShipmentDeliveryChecker _shipmentClient;` (field name may be kept as-is or renamed to `_shipmentDeliveryChecker` — see Open Questions).
- Replace the constructor parameter type `IShipmentClient shipmentClient` with `IShipmentDeliveryChecker shipmentClient`.
- Line 99's call site (`_shipmentClient.HasDeliveredShipmentAsync(order.Code, cancellationToken)`) requires no change, since the method signature is identical.

**Acceptance criteria:**
- `CompleteDeliveredOrdersJob` no longer references `Anela.Heblo.Application.Features.ShipmentLabels` (or any type from that namespace) anywhere in the file.
- `CompleteDeliveredOrdersJob` compiles and its `ExecuteAsync` logic is byte-for-byte unchanged except for the type of the injected dependency.
- The job's metadata (`JobName`, `DisplayName`, `Description`, `CronExpression`, `DefaultIsEnabled`) is unchanged.

### FR-5: Update existing unit tests to mock the new contract
`CompleteDeliveredOrdersJobTests` (`backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs`) currently constructs `Mock<IShipmentClient>` and passes `shipments.Object` into the job constructor. Update this to `Mock<IShipmentDeliveryChecker>`.

**Acceptance criteria:**
- `MakeSut` returns `Mock<IShipmentDeliveryChecker>` instead of `Mock<IShipmentClient>`.
- All 9 existing test methods continue to pass unmodified in behavior (same setups/verifies against `HasDeliveredShipmentAsync`, since the method signature is unchanged).
- The `using Anela.Heblo.Application.Features.ShipmentLabels;` import is removed from the test file if no longer needed, and `using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;` is added.
- No other test file changes are required (no other test constructs `CompleteDeliveredOrdersJob`).

### FR-6 (recommended, not required for merge): Add a `ModuleBoundariesTests` rule for `ShoptetOrders -> ShipmentLabels`
The codebase enforces contract-ownership boundaries via reflection-based tests in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (e.g. "Packaging -> ShoptetOrders", "Catalog -> Logistics"). There is currently no rule pinning the `ShoptetOrders -> ShipmentLabels` boundary. Adding one prevents this exact regression (a future contributor reintroducing a direct `IShipmentClient` dependency in `ShoptetOrders`).

**Acceptance criteria:**
- A new `ModuleBoundaryRule` entry named `"ShoptetOrders -> ShipmentLabels"` is added to `Rules()`, with `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ShoptetOrders"` and `ForbiddenNamespacePrefixes` including `"Anela.Heblo.Application.Features.ShipmentLabels"` (excluding `ShipmentLabels.Contracts`... — see note below), with an empty allowlist.
- Note: `ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs` itself must not trigger the rule (it references no `ShipmentLabels` type). If the test's `InspectedAssembly`/prefix scoping would otherwise flag the DI registration lambda or similar, follow the same allowlist-with-justification-comment convention used elsewhere in the file.
- This test passes with zero violations after FR-1–FR-5 are implemented.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. The adapter is a single-line delegation (no additional I/O, allocation of note, or synchronous blocking); the job's HTTP call pattern to Shoptet is unchanged.

### NFR-2: Security
No change in security posture. No new secrets, auth flows, or data exposure — the adapter delegates to the already-authenticated `IShipmentClient` HTTP client.

### NFR-3: Architectural conformance
This change must satisfy the module-boundary rule documented in `docs/architecture/development_guidelines.md` ("Cross-Module Communication Example: ILeafletKnowledgeSource"): consumer defines the contract, provider implements the adapter and owns the DI registration.

## Data Model
No persisted data model changes. This is a pure interface/adapter refactor — no new entities, DTOs stored in the database, or schema changes. The runtime data exchanged (`orderCode: string` in, `bool` out) is identical to the existing `IShipmentClient.HasDeliveredShipmentAsync` call.

## API / Interface Design

**New contract** (owned by `ShoptetOrders`):
```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IShipmentDeliveryChecker
{
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
}
```

**New adapter** (owned by `ShipmentLabels`):
```csharp
namespace Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;

internal sealed class ShipmentLabelsShipmentDeliveryCheckerAdapter : IShipmentDeliveryChecker
{
    // delegates to IShipmentClient.HasDeliveredShipmentAsync
}
```

**DI wiring** (owned by `ShipmentLabels`, in `ShipmentLabelsModule.cs`):
```csharp
services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();
```

No HTTP endpoints, background job schedules, or public API surfaces change. `CompleteDeliveredOrdersJob`'s `RecurringJobMetadata` (job name `complete-delivered-orders`, cron `0 * * * *`) is unaffected.

## Dependencies
- Existing `IShipmentClient` and its registered implementation `ShoptetShipmentClient` (`Anela.Heblo.Adapters.ShoptetApi`) — unchanged, continues to be injected into the new adapter.
- No new NuGet packages or external services.
- No frontend/OpenAPI client changes (this is a backend-internal DI refactor with no HTTP surface change).

## Out of Scope
- The `Packaging` module's direct use of `IShipmentClient` (`GetOrderTrackingNumberHandler`, `GetPackageLabelPdfHandler`, `ScanPackingOrderHandler`, `GetOrderTrackingNumbersHandler`, `DeletePackageHandler`, `ResetOrderShipmentHandler`, `FillTrackingNumbersJob`) is unchanged. Those handlers use multiple `IShipmentClient` methods (labels, tracking numbers, shipment creation/cancellation) and are a separate, larger refactor if ever pursued — not part of this fix.
- `ShipmentLabels`' own internal use cases (`GetOrderShipmentLabelsHandler`, `CreateOrderShipmentHandler`) continue to use `IShipmentClient` directly — this is legitimate, since `ShipmentLabels` is the provider/owner of that interface.
- No change to `IShipmentClient`'s method surface, `ShoptetShipmentClient`'s implementation, or the underlying Shoptet API integration.
- No change to job scheduling, feature flags (`DeliveredOrderCompletion`, `DeliveredOrderCompletionTestSource`), or `ShoptetOrdersSettings`.
- No change to `IPackingCarrierCoolingSource` / `IPackingProductSource` (already correctly structured; referenced here only as the pattern to follow).

## Open Questions
None. The brief provides a concrete, verified-against-code fix; the only judgment calls made (return type `Task<bool>` instead of the brief's `Task`, adapter lifetime `AddTransient` matching the sibling adapters, exact adapter class name, and the optional `ModuleBoundariesTests` rule in FR-6) are noted inline as reasonable, low-risk assumptions consistent with existing codebase conventions.

## Status: COMPLETE
