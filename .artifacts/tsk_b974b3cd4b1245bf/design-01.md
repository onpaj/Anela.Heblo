# Design: Split `IPackingOrderClient` along module ownership lines

No UI section — this is a backend-only interface/DI reshuffle. No endpoint, request/response DTO,
or frontend-visible behavior changes.

## Design decisions (resolving plan-01's open questions)

1. **FR-2 → Option A (minimal split, pin the rest).** `GetPackingOrderAsync` stays on
   `IPackingOrderClient` in `ShoptetOrders`, consumed as today by `ShoptetOrders` itself,
   `Packaging` (2 handlers), and `ShipmentLabels` (1 handler). Only the two count methods —
   which really are Packaging-exclusive — move to a new Packaging-owned contract. Reasons:
   a prior review (`ModuleBoundariesTests.cs`, "2026-06-05 decoupling") already looked at this
   exact `Packaging → ShoptetOrders` coupling for the fetch operation and deliberately pinned
   it rather than inverting it; forcing three near-identical single-method fetch interfaces
   (one per consuming module, all implemented by the same adapter) is ceremony without a
   behavior or testability payoff. The gap the finding *is* right about — the two counters
   don't belong on a fetch-oriented interface and Packaging shouldn't own that dependency —
   gets fixed by FR-1.
2. **New interface name: `IPackingOrderCountSource`.** Matches the existing naming convention
   for consumer-owned contracts already in `ShoptetOrders/Contracts/`
   (`IPackingCarrierCoolingSource`, `IPackingProductSource` — pattern `IPacking<Noun>Source`).
3. **`IPackingOrderClient` keeps its name.** It narrows to one method but a rename only buys
   cosmetic clarity at the cost of touching all three consumers' `using`/injection sites for no
   functional gain — skip it (surgical-changes rule).
4. **Untracked `ShipmentLabels → ShoptetOrders` coupling gets tracked, not removed.** Add a new
   `ModuleBoundaryRule` mirroring the existing `Packaging → ShoptetOrders` one, with a
   justification comment, so `CreateOrderShipmentHandler`'s use of `IPackingOrderClient` is
   pinned instead of silently ungoverned.

## Component design

### 1. New contract — `Packaging/Contracts/IPackingOrderCountSource.cs`

```csharp
namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackingOrderCountSource
{
    /// <summary>Returns the total count of orders currently in the configured packing state ("Balí se").</summary>
    Task<int> GetOrdersBeingPackedCountAsync(CancellationToken ct = default);

    /// <summary>Returns the total count of orders currently in the configured processing state ("Vyřizuje se").</summary>
    Task<int> GetOrdersBeingProcessedCountAsync(CancellationToken ct = default);
}
```

Doc comments are moved verbatim from the current `IPackingOrderClient` — no wording change.

### 2. Narrowed contract — `ShoptetOrders/IPackingOrderClient.cs`

Remove `GetOrdersBeingPackedCountAsync` and `GetOrdersBeingProcessedCountAsync` from the
interface. `PackingOrder` / `PackingOrderItem` DTOs are untouched and stay in this file/module —
they are consumed only via `GetPackingOrderAsync`, which stays put.

```csharp
public interface IPackingOrderClient
{
    Task<PackingOrder?> GetPackingOrderAsync(string code, CancellationToken ct = default);
}
```

### 3. Adapter — `ShoptetApiPackingOrderClient`

Implements both interfaces; no method body changes:

```csharp
public class ShoptetApiPackingOrderClient : IPackingOrderClient, IPackingOrderCountSource
```

Add `using Anela.Heblo.Application.Features.Packaging.Contracts;`. `GetOrdersBeingPackedCountAsync`
and `GetOrdersBeingProcessedCountAsync` bodies are unchanged (still call
`_orderClient.GetOrdersByStatusAsync(...)`).

### 4. DI registration — `ShoptetApiAdapterServiceCollectionExtensions.cs`

Add one line next to the existing registration (line 119):

```csharp
services.AddTransient<IPackingOrderClient, ShoptetApiPackingOrderClient>();
services.AddTransient<IPackingOrderCountSource, ShoptetApiPackingOrderClient>();
```

Both resolve to the same adapter type; each `AddTransient` call constructs its own instance per
resolution (consistent with existing behavior — no singleton/state sharing concerns since the
adapter is stateless besides injected dependencies).

### 5. Consumers — `Packaging` count-only handlers

`GetPackingDashboardHandler` and `PackingStatsTile` swap their constructor parameter and field
from `IPackingOrderClient` to `IPackingOrderCountSource`, and swap
`using Anela.Heblo.Application.Features.ShoptetOrders;` for
`using Anela.Heblo.Application.Features.Packaging.Contracts;`. Call sites
(`_packingOrderClient.GetOrdersBeingPackedCountAsync(...)` etc.) are unchanged apart from the
field's renamed type — field/variable names may stay as `_packingOrderClient`/rename to
`_orderCountSource` at the development step's discretion; no behavioral impact either way.

The three fetch consumers (`ScanPackingOrderHandler`, `ResetOrderShipmentHandler`,
`CreateOrderShipmentHandler`) are **not touched** — they keep injecting `IPackingOrderClient` for
`GetPackingOrderAsync`, unchanged.

### 6. Architecture test — `ModuleBoundariesTests.cs`

- **Trim `PackagingShoptetOrdersAllowlist`**: remove the `GetPackingDashboardHandler ->
  IPackingOrderClient` and `PackingStatsTile -> IPackingOrderClient` entries and their comments
  (lines 335–341 today) — after the change these two types no longer reference the
  `ShoptetOrders` namespace at all, so the entries would fail the "no stale allowlist entries"
  bar if left in.
- **Add a new rule + allowlist for `ShipmentLabels → ShoptetOrders`**, mirroring the existing
  `Packaging → ShoptetOrders` rule shape:

  ```csharp
  // Allowlist for ShipmentLabels -> ShoptetOrders. CreateOrderShipmentHandler legitimately
  // consumes IPackingOrderClient to fetch order weight/items for shipment creation. This
  // mirrors the pre-existing, deliberately pinned Packaging -> ShoptetOrders coupling
  // (2026-06-05 decoupling) — tracked here rather than left ungoverned.
  private static readonly HashSet<string> ShipmentLabelsShoptetOrdersAllowlist = new(StringComparer.Ordinal)
  {
      "Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment.CreateOrderShipmentHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IPackingOrderClient",
      "Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment.CreateOrderShipmentHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder",
      "Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment.CreateOrderShipmentHandler -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrderItem",
  };

  new ModuleBoundaryRule(
      Name: "ShipmentLabels -> ShoptetOrders",
      InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ShipmentLabels",
      ForbiddenNamespacePrefixes: new[]
      {
          "Anela.Heblo.Application.Features.ShoptetOrders",
      },
      Allowlist: ShipmentLabelsShoptetOrdersAllowlist),
  ```

  The exact allowlist entry set (whether `PackingOrder`/`PackingOrderItem` need separate entries,
  and whether compiler-generated closure/state-machine types need their own lines) must be
  confirmed by running the test — the mechanism walks IL-referenced types per member
  (`EnumerateReferencedTypes`, `ModuleBoundariesTests.cs:674`) and falls back to the declaring
  type for nested/generated types, same as the existing `Packaging` entries did. Treat the
  snippet above as the starting point; add lines only as the failing test output demands, don't
  pre-guess beyond it.

### 7. Unit tests

Test files that construct `IPackingOrderClient` mocks for `GetPackingDashboardHandler` and
`PackingStatsTile` (`GetPackingDashboardHandlerTests.cs`, `PackingStatsTileTests.cs`) switch their
mock type to `IPackingOrderCountSource`; assertions and setups (`GetOrdersBeingPackedCountAsync`,
`GetOrdersBeingProcessedCountAsync`) are otherwise unchanged. All other existing test files
touching `IPackingOrderClient` (`ScanPackingOrderHandlerPackagePersistenceTests`,
`ScanPackingOrderPackerTests`, `ScanPackingOrderHandlerTests`, `ResetOrderShipmentHandlerTests`,
`CreateOrderShipmentHandlerTests`, `GetPackingOrderHandlerTests`) are untouched — they only ever
exercised `GetPackingOrderAsync`, which stays on `IPackingOrderClient`.

## Data schemas

No database schema changes. No HTTP request/response contract changes — `GetPackingDashboardResponse`
and the `PackingStatsTile` JSON payload shape are identical before and after; only the internal
DI wiring that produces the same `OrdersBeingPackedCount`/`OrdersBeingProcessedCount` values
changes. No event payloads involved.

**Interface shapes (the actual "schema" of this change):**

| Type | Before | After |
|---|---|---|
| `ShoptetOrders.IPackingOrderClient` | `GetPackingOrderAsync`, `GetOrdersBeingPackedCountAsync`, `GetOrdersBeingProcessedCountAsync` | `GetPackingOrderAsync` only |
| `Packaging.Contracts.IPackingOrderCountSource` (new) | — | `GetOrdersBeingPackedCountAsync`, `GetOrdersBeingProcessedCountAsync` |
| `ShoptetApiPackingOrderClient` | `: IPackingOrderClient` | `: IPackingOrderClient, IPackingOrderCountSource` |

## Scope confirmation (unchanged from plan-01)

In scope: the two new/changed interface files, adapter interface list, one DI registration line,
two Packaging consumer files, `ModuleBoundariesTests.cs` allowlist/rule updates, two test files'
mock types. Out of scope: `IEshopOrderClient`, `PackingOrder`/`PackingOrderItem` DTO placement,
`ScanPackingOrderHandler`/`ResetOrderShipmentHandler`/`CreateOrderShipmentHandler`/
`GetPackingOrderHandler` (all keep using `IPackingOrderClient.GetPackingOrderAsync` unchanged),
`CompletePackingOrderHandler -> IEshopOrderClient` allowlist entry.
