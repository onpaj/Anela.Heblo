# Architecture Review: Invert `IShipmentClient` dependency in `CompleteDeliveredOrdersJob`

## Skip Design: true

## Architectural Fit Assessment

This is a textbook application of a pattern the codebase already has fully documented and twice implemented in the exact same module. `development_guidelines.md` (lines 226–239, "Cross-Module Communication Example: `ILeafletKnowledgeSource`") states the rule plainly: when module A needs read-only access to module B, the **consumer defines the contract, the provider implements the adapter and owns the DI registration**. `ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs` (implemented by `CarrierCoolingPackingCarrierCoolingAdapter`, registered in `CarrierCoolingModule.cs`) and `IPackingProductSource.cs` (implemented by `CatalogPackingProductSourceAdapter`, registered in `CatalogModule.cs`) are the working precedent, both living in the same `ShoptetOrders/Contracts/` folder the spec targets.

`CompleteDeliveredOrdersJob` breaks this pattern: it holds a compile-time reference to `Anela.Heblo.Application.Features.ShipmentLabels.IShipmentClient`, a six-method, provider-shaped interface (`GetLabelsByOrderCodeAsync`, `GetLatestActiveTrackingNumberAsync`, `HasDeliveredShipmentAsync`, `GetShippingOptionsAsync`, `CreateShipmentAsync`, `CancelShipmentAsync`) it uses one method of (line 99 of `CompleteDeliveredOrdersJob.cs`). This is a genuine architectural defect, not a style nit: it is the exact ISP + dependency-direction violation `ModuleBoundariesTests.cs` is built to catch for other module pairs — and confirmed absent for `ShoptetOrders → ShipmentLabels` (no such rule currently exists in the 621-line `Rules()` theory data I inspected).

The fix is small, mechanical, and has zero behavioral surface area. No new infrastructure, no new dependency, no data model or API change. It fits cleanly and is fully consistent with the two sibling contracts already in the same folder.

## Proposed Architecture

### Component Overview

```
Before:
  ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob
        │  injects (full surface)
        ▼
  ShipmentLabels.IShipmentClient  (6 methods)
        ▲  implements
  Adapters.ShoptetApi.ShoptetShipmentClient  (registered as typed HttpClient)

After:
  ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob
        │  injects (narrow, consumer-owned)
        ▼
  ShoptetOrders/Contracts/IShipmentDeliveryChecker  (1 method)
        ▲  implements
  ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter
        │  delegates to
        ▼
  ShipmentLabels.IShipmentClient  (unchanged, 6 methods)
        ▲  implements
  Adapters.ShoptetApi.ShoptetShipmentClient  (unchanged)
```

`IShipmentClient` itself, its `ShoptetShipmentClient` implementation, and its typed-`HttpClient` DI registration in `ShoptetApiAdapterServiceCollectionExtensions.cs` are untouched. The new adapter sits entirely inside `ShipmentLabels` and wraps the existing client — it does not replace it, and `ShipmentLabels`' own handlers (`GetOrderShipmentLabelsHandler`, `CreateOrderShipmentHandler`) keep injecting `IShipmentClient` directly, which is correct since they *are* the provider.

### Key Design Decisions

#### Decision 1: Contract shape — one method, matching existing signature exactly
**Options considered:**
- Mirror `IShipmentClient.HasDeliveredShipmentAsync`'s exact signature (`Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)`).
- A more generic name (e.g. `IOrderDeliveryStatusSource`) or a differently-shaped API (e.g. returning an enum/status object for future extensibility).

**Chosen approach:** Exact signature mirror, named `IShipmentDeliveryChecker`.

**Rationale:** The consumer-owned-contract pattern exists to narrow the surface to what's *actually used today*, not to speculatively generalize (`development_guidelines.md` explicitly says "exposing only the operations it actually consumes (no speculative methods)"). Renaming or reshaping invites scope creep into a fix that should have zero behavioral risk. If a second `ShoptetOrders` consumer later needs a different shipment fact, extend the interface then — YAGNI applies.

#### Decision 2: Adapter ownership and location
**Options considered:**
- Adapter in `ShipmentLabels/Infrastructure/` (provider-owned, new folder).
- Adapter in the `Anela.Heblo.Adapters.ShoptetApi` project, next to `ShoptetShipmentClient`.

**Chosen approach:** `ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs`, matching `CarrierCooling/Infrastructure/` and `Catalog/Infrastructure/` exactly.

**Rationale:** The pattern's rule is "provider module owns the adapter," and the provider of this data, from `ShoptetOrders`' point of view, is the `ShipmentLabels` *Application-layer module* (which itself depends on `IShipmentClient`, an Application-layer contract implemented in the Adapters project). Putting the adapter in `Anela.Heblo.Adapters.ShoptetApi` would be a layering violation — that project has no reason to know about `ShoptetOrders.Contracts`, and none of the existing sibling adapters (`CarrierCoolingPackingCarrierCoolingAdapter`, `CatalogPackingProductSourceAdapter`) live in an Adapters project either. Keep the adapter at the same architectural layer as `IShipmentClient`'s existing consumers.

#### Decision 3: DI lifetime — `AddTransient`
**Options considered:** `AddTransient`, `AddScoped`.

**Chosen approach:** `AddTransient`, matching `CarrierCoolingModule`'s `services.AddTransient<IPackingCarrierCoolingSource, ...>()`.

**Rationale:** The adapter is stateless pure delegation with no per-request state to scope; `AddTransient` is the established convention for these sibling adapters and there's no reason to diverge. Note the adapter's own dependency, `IShipmentClient`, is registered via `AddHttpClient<>`, which is itself scoped-per-request by the `IHttpClientFactory` machinery regardless of the wrapping adapter's lifetime — no conflict.

## Implementation Guidance

### Directory / Module Structure

New files:
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs` (consumer contract)
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs` (provider adapter — new `Infrastructure/` folder under `ShipmentLabels`, mirroring `CarrierCooling/Infrastructure/` and `Catalog/Infrastructure/`)

Modified files:
- `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/Jobs/CompleteDeliveredOrdersJob.cs` — swap `IShipmentClient` for `IShipmentDeliveryChecker`
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShipmentLabelsModule.cs` — add the `AddTransient<IShipmentDeliveryChecker, ...>()` registration
- `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/CompleteDeliveredOrdersJobTests.cs` — mock the new interface instead of `IShipmentClient`
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add the `"ShoptetOrders -> ShipmentLabels"` rule (recommended, see below)

Not touched: `IShipmentClient.cs`, `ShoptetShipmentClient.cs`, `ShoptetApiAdapterServiceCollectionExtensions.cs`, `Packaging` module's seven `IShipmentClient` consumers, `ShoptetOrdersModule.cs`.

### Interfaces and Contracts

```csharp
// ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IShipmentDeliveryChecker
{
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
}
```

```csharp
// ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs
namespace Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;

internal sealed class ShipmentLabelsShipmentDeliveryCheckerAdapter : IShipmentDeliveryChecker
{
    private readonly IShipmentClient _shipmentClient;

    public ShipmentLabelsShipmentDeliveryCheckerAdapter(IShipmentClient shipmentClient)
        => _shipmentClient = shipmentClient;

    public Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)
        => _shipmentClient.HasDeliveredShipmentAsync(orderCode, ct);
}
```

DI registration in `ShipmentLabelsModule.AddShipmentLabelsModule`, placed alongside the module's other registrations, with the same justification-comment convention used in `CarrierCoolingModule.cs`:

```csharp
// Cross-module contract: ShipmentLabels implements ShoptetOrders' IShipmentDeliveryChecker via
// adapter. DI registration is owned by the provider (ShipmentLabels), not the consumer (ShoptetOrders).
services.AddTransient<IShipmentDeliveryChecker, ShipmentLabelsShipmentDeliveryCheckerAdapter>();
```

`CompleteDeliveredOrdersJob`'s constructor and field simply retype from `IShipmentClient` to `IShipmentDeliveryChecker`; the call at line 99 is unchanged since the method signature is identical.

### Data Flow

Unchanged at runtime. `CompleteDeliveredOrdersJob.ExecuteAsync` still calls `HasDeliveredShipmentAsync(order.Code, ct)` once per scanned order; the call now routes through `IShipmentDeliveryChecker` → `ShipmentLabelsShipmentDeliveryCheckerAdapter` → `IShipmentClient` → `ShoptetShipmentClient` → Shoptet's `GET /api/shipments?orderCode={code}`, one extra virtual dispatch, no extra I/O, no behavior change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| DI resolution failure at startup if `IShipmentDeliveryChecker` binding is missed | Low | `dotnet build` won't catch this (DI is resolved at runtime); rely on the existing test suite's `WebApplicationFactory`-based integration tests (if any construct the full DI container) plus manual smoke of the job registration. Low risk given the pattern is copy-paste from `CarrierCoolingModule`. |
| Regression reintroducing direct `IShipmentClient` use in `ShoptetOrders` later | Low | Add the `ModuleBoundariesTests` rule now (see FR-6 / Specification Amendments) — this is the exact mechanism used to pin the four other cross-module contracts, and it's nearly free to add given the reflection-based test harness already exists. |
| Adapter left untested while its sibling adapters (`CarrierCoolingPackingCarrierCoolingAdapter`) have dedicated unit test files | Low | See Specification Amendments — add a minimal adapter test for consistency; the delegation is trivial but a one-assert test costs little and matches project convention. |

No other risks identified — this is a compile-time-verified, behavior-preserving refactor with no runtime data, security, or performance surface.

## Specification Amendments

The spec (`spec.r1.md`) is thorough and already anticipated the architectural questions correctly (return type correction, adapter lifetime, file placement). Two small additions:

1. **Promote FR-6 from "recommended" to required.** The `ModuleBoundariesTests -> ShoptetOrders -> ShipmentLabels` rule is what makes this fix durable rather than cosmetic — without it, nothing stops a future contributor from reintroducing `IShipmentClient` into `ShoptetOrders` (exactly the four other module-boundary rules exist to prevent this same regression class elsewhere). Given the test harness, allowlist convention, and `Rules()` `TheoryData` are all already in place and copy-paste-adaptable from the `"Packaging -> ShoptetOrders"` rule, the added cost is minimal relative to the value of closing the loop. Recommend making this a hard requirement of the PR, not optional follow-up.
2. **Add a minimal unit test for `ShipmentLabelsShipmentDeliveryCheckerAdapter`.** Every existing sibling adapter (`CarrierCoolingPackingCarrierCoolingAdapterTests.cs`) has a dedicated test file, even though some of that logic is pure mapping. For consistency, add one test verifying the adapter delegates to `IShipmentClient.HasDeliveredShipmentAsync` with the same arguments and returns its result — a single `[Fact]` is sufficient given there's no mapping logic. Suggested location: `backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs`, mirroring the `CarrierCooling` test's namespace/folder convention (`Anela.Heblo.Tests.Features.{Module}.Infrastructure`).

No other amendments — FR-1 through FR-5 are implementable as written with no conflicts against existing code or conventions found during exploration.

## Prerequisites

None. No migrations, no config, no infrastructure changes, no feature-flag work. All types referenced (`IShipmentClient`, `EshopOrderSummary`, `ShoptetOrdersSettings`) already exist and are unmodified. Implementation can start immediately.
