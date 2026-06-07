# Architecture Review: Decouple Packaging module from ShoptetOrdersSettings

## Skip Design: true

Backend-only refactor. No new screens, components, or visual changes. API response shape (`Eligibility.IsEligible`) is unchanged — `IsEligibleForPacking` lives on the internal contract DTO only.

## Architectural Fit Assessment

The proposal is an **excellent fit** — it removes a known violation of `docs/architecture/development_guidelines.md` ("No direct references between feature modules. Communication only through contracts/interfaces.") and aligns the `Packaging` module with the same pattern already used by every other cross-module consumer in the codebase (`ILeafletKnowledgeSource`/`KnowledgeBaseLeafletSourceAdapter` is the canonical example).

Integration points are minimal and already exist:

- `IPackingOrderClient` and `IEshopOrderClient` are the legitimate seams between `Packaging`/Shoptet handlers and the Shoptet adapter; both contracts already live in `Anela.Heblo.Application.Features.ShoptetOrders`.
- `ShoptetApiPackingOrderClient` and `ShoptetOrderClient` are the natural homes for status-ID knowledge — they live in `Anela.Heblo.Adapters.ShoptetApi.Orders` and already reference the Application namespace to implement the contracts.
- The `ShoptetOrdersSettings` options binding is already done by `ShoptetOrdersModule`; both adapters simply resolve `IOptions<ShoptetOrdersSettings>` from DI.

No new infrastructure, no new module wiring, no new packages. The refactor compresses the rule "what does eligible-to-pack mean?" from two locations to one and removes the only remaining link from `Packaging` into `ShoptetOrders.ShoptetOrdersSettings`.

## Proposed Architecture

### Component Overview

```
Before (violation):

  Packaging.ScanPackingOrderHandler ───────────┐
                                               │ reads PackingStateId, PackedStateId
                                               ▼
  ShoptetOrders.GetPackingOrderHandler ──► ShoptetOrdersSettings (concrete config class)
                                               ▲
                                               │ reads PackingStateId
                                               │
                              ShoptetOrders.BlockOrderProcessingHandler

After:

  Packaging.ScanPackingOrderHandler
       │ uses order.IsEligibleForPacking
       │ uses _eshopOrderClient.MarkAsPackedAsync(orderCode, ct)
       ▼
  IPackingOrderClient ──── IEshopOrderClient            (contracts in Application/ShoptetOrders)
       │                          │
       ▼ implemented by           ▼ implemented by
  ShoptetApiPackingOrderClient   ShoptetOrderClient     (adapters in Adapters.ShoptetApi)
       │                          │
       │ reads PackingStateId     │ reads PackedStateId
       ▼                          ▼
  ShoptetOrdersSettings  ◄─────────┘
       ▲
       │ unchanged
  ShoptetOrders.BlockOrderProcessingHandler  (same module owns the setting — no boundary crossed)
```

After the refactor, `Anela.Heblo.Application.Features.Packaging` does not name `ShoptetOrdersSettings`, `PackingStateId`, or `PackedStateId` anywhere.

### Key Design Decisions

#### Decision 1: Place eligibility on the DTO, not as a separate query method

**Options considered:**
1. Add `bool IsEligibleForPacking { get; set; }` to `PackingOrder`.
2. Add `Task<bool> IsEligibleForPackingAsync(string code, CancellationToken)` to `IPackingOrderClient`.
3. Compute eligibility in the handler from a Shoptet-agnostic rule object (e.g., `IPackingEligibilityPolicy`).

**Chosen approach:** Option 1, as the spec mandates.

**Rationale:** The handler already has the `PackingOrder` in hand from `GetPackingOrderAsync`; a property is zero extra cost and zero extra round-trip. Option 2 doubles the HTTP traffic for the same answer. Option 3 introduces a new abstraction whose only consumer is the same place that already calls the client — premature generality.

#### Decision 2: Name the status-transition method by business intent (`MarkAsPackedAsync`), keep generic `UpdateStatusAsync`

**Options considered:**
1. Add `MarkAsPackedAsync(string orderCode, CancellationToken)` alongside the existing `UpdateStatusAsync(string, int, CancellationToken)`.
2. Replace `UpdateStatusAsync` with named per-transition methods (`MarkAsPackedAsync`, `MarkAsBlockedAsync`, …).
3. Inject a separate `IPackingStatusTransitions` contract dedicated to packing concerns.

**Chosen approach:** Option 1, as the spec mandates.

**Rationale:** `UpdateStatusAsync` has known callers (`BlockOrderProcessingHandler`) that legitimately operate on raw status IDs because they live in the `ShoptetOrders` module and own those IDs. Replacing it (Option 2) is unnecessary scope. A dedicated contract (Option 3) over-abstracts a single method.

#### Decision 3: Adapters read `ShoptetOrdersSettings` directly via `IOptions<T>`

**Options considered:**
1. Inject `IOptions<ShoptetOrdersSettings>` into `ShoptetApiPackingOrderClient` and `ShoptetOrderClient`.
2. Introduce an adapter-local options class (e.g., `ShoptetApiPackingOptions`) mapped from `ShoptetOrdersSettings` at startup.
3. Pass status IDs in via factory parameters at DI registration.

**Chosen approach:** Option 1, as the spec mandates.

**Rationale:** `Anela.Heblo.Adapters.ShoptetApi` already references `Anela.Heblo.Application.Features.ShoptetOrders` to implement the contracts; reading the sibling settings class from the same namespace adds no new coupling. Options 2 and 3 split one truth across two configuration shapes for no benefit.

#### Decision 4: Leave `PackingOrder.StatusId` in place

**Options considered:**
1. Keep `StatusId` on the DTO (spec position).
2. Remove `StatusId` immediately now that no handler reads it.

**Chosen approach:** Option 1.

**Rationale:** Spec defers this to a follow-up after confirming no remaining consumer. Risk of removal-without-audit is low (compile errors flag any reader) but the spec is explicit, so honour it. See **Specification Amendments** below for a small caveat.

## Implementation Guidance

### Directory / Module Structure

No new files. The change is **strictly additive on the contract side** and **subtractive on the consumer side**:

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IPackingOrderClient.cs` | Add `bool IsEligibleForPacking { get; set; }` to `PackingOrder`. |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` | Add `Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetApiPackingOrderClient.cs` | Inject `IOptions<ShoptetOrdersSettings>`; set `IsEligibleForPacking = statusId == settings.PackingStateId` on the returned `PackingOrder`. |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` | Inject `IOptions<ShoptetOrdersSettings>`; implement `MarkAsPackedAsync` as a one-line delegate to `UpdateStatusAsync(orderCode, settings.PackedStateId, ct)`. |
| `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs` | Drop `ShoptetOrdersSettings` field/param, drop the `Microsoft.Extensions.Options` `using` if no other dep needs it. Use `order.IsEligibleForPacking` and `_eshopOrderClient.MarkAsPackedAsync(...)`. Update the warning log to `"Failed to mark order {OrderCode} as packed"`. |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/UseCases/GetPackingOrder/GetPackingOrderHandler.cs` | Drop `IOptions<ShoptetOrdersSettings>` (no other use exists in this handler — verified). Use `order.IsEligibleForPacking`. Remove the `Microsoft.Extensions.Options` `using`. |
| `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs` | Remove `DefaultOrderSettings` and the `orderSettings` parameter from `CreateHandler`. Set `IsEligibleForPacking = true` in `EligibleOrder()`. In tests that exercise the ineligible path (`StatusId = 99`), set `IsEligibleForPacking = false` explicitly. Update the assertions on `UpdateStatusAsync` to assert `MarkAsPackedAsync("0001234", ...)` instead. |
| `backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/GetPackingOrderHandlerTests.cs` | Same shape: drop the `ShoptetOrdersSettings` setup, set `IsEligibleForPacking` on the mocked `PackingOrder`. |

`ShoptetApiAdapterServiceCollectionExtensions` requires **no change**: both adapter types already go through DI; `IOptions<ShoptetOrdersSettings>` resolves through the binding in `ShoptetOrdersModule`.

### Interfaces and Contracts

```csharp
// IPackingOrderClient.cs
public class PackingOrder
{
    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ShippingMethodName { get; set; } = string.Empty;
    public Cooling Cooling { get; set; } = Cooling.None;
    public bool IsCooled { get; set; }

    /// <summary>Shoptet order status ID. Kept for diagnostic/logging use; do
    /// not read this to derive eligibility — use <see cref="IsEligibleForPacking"/>.</summary>
    public int StatusId { get; set; }

    /// <summary>True when the order is in the configured Shoptet packing state
    /// ("Balí se"). Computed by the adapter so callers do not need to know the
    /// status-id rule.</summary>
    public bool IsEligibleForPacking { get; set; }

    // … unchanged …
}

// IEshopOrderClient.cs
public interface IEshopOrderClient
{
    // … existing members …

    /// <summary>Transitions the order to the configured "packed" state
    /// (Shoptet "Zabaleno", id 52 by default).</summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
```

Contract rules to enforce:

- `IsEligibleForPacking` is set **only** by `ShoptetApiPackingOrderClient`. It must never be read or assigned in `Application.Features.Packaging` source. (Tests are the only legitimate writer outside the adapter, and only in setup helpers.)
- `MarkAsPackedAsync` does **not** accept a status-id parameter and never will. Callers that want generic transitions continue to use `UpdateStatusAsync`.

### Data Flow

**GET `/api/packing-orders/{code}` (unchanged endpoint surface):**

```
HTTP GET → PackingOrderController
        → MediatR → GetPackingOrderHandler
              → IPackingOrderClient.GetPackingOrderAsync(code, ct)
                    → ShoptetApiPackingOrderClient
                         (loads detail, reads ShoptetOrdersSettings.PackingStateId,
                          sets PackingOrder.IsEligibleForPacking)
              → reads order.IsEligibleForPacking
              → returns GetPackingOrderResponse with Eligibility { IsEligible, WarningTitle, WarningBody }
```

**POST `/api/packaging/scan` (unchanged endpoint surface):**

```
HTTP POST → PackagingController → ScanPackingOrderHandler
   ├─ IPackingOrderClient.GetPackingOrderAsync(code, ct)
   │       → ShoptetApiPackingOrderClient (sets IsEligibleForPacking as above)
   ├─ isEligible = order.IsEligibleForPacking
   ├─ if (!isEligible) → return ineligible payload (with shipment if labels exist), DO NOT mark packed
   ├─ else
   │     ├─ create / reuse shipment via IShipmentClient
   │     └─ TryMarkAsPackedAsync(orderCode, ct)
   │            → IEshopOrderClient.MarkAsPackedAsync(orderCode, ct)
   │                  → ShoptetOrderClient.MarkAsPackedAsync
   │                        → UpdateStatusAsync(orderCode, settings.PackedStateId, ct)
   │                              → PATCH /api/orders/{code}/status
   └─ return ScanPackingOrderResponse (same shape as today)
```

No new round-trips. The eligibility flag piggybacks on the existing `GetOrderStatusIdAsync` call inside the adapter.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `PackingStateId`/`PackedStateId` reappear in `Packaging` via a future PR (silent drift back to current state). | Medium | Add an architecture test in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (or a sibling) asserting no source file under `Application/Features/Packaging/**` contains the substrings `ShoptetOrdersSettings`, `PackingStateId`, `PackedStateId`. Fails CI on regression. This is the exact pattern already used to enforce the `Leaflet` ↔ `KnowledgeBase` boundary (see `development_guidelines.md` §Cross-Module Communication Example). |
| `PackingOrder.StatusId` is left in place and a future caller re-derives eligibility from it, recreating the duplicated rule. | Low | XML doc on `StatusId` (see above) explicitly steers readers to `IsEligibleForPacking`. The arch test in the row above covers the worst case (re-introducing the constants). |
| Adapter constructor signatures change — any custom registration outside `AddShoptetApiAdapter` (e.g., test fixtures, integration tests) breaks at runtime. | Low | Compile-time break for `ShoptetApiPackingOrderClient` and `ShoptetOrderClient`; resolved automatically by DI inside `AddShoptetApiAdapter` since `IOptions<ShoptetOrdersSettings>` is already bound by `ShoptetOrdersModule` in the API composition. Confirm no test project new-s up either adapter manually (a quick grep in `backend/test`). |
| Existing handler tests (`ScanPackingOrderHandlerTests`, `GetPackingOrderHandlerTests`) still encode the `StatusId == PackingStateId` rule in setup helpers; if they are left unchanged the tests will fail or, worse, pass by accident. | Medium | Update both test files: drop the `ShoptetOrdersSettings` plumbing, set `IsEligibleForPacking` explicitly on the mocked `PackingOrder` per test, and switch `UpdateStatusAsync` verifications to `MarkAsPackedAsync` verifications. NFR-3 explicitly forbids `Packaging` module tests from asserting a Shoptet status id. |
| `MarkAsPackedAsync` swallows the cancellation token semantic difference between `UpdateStatusAsync` (required `CancellationToken ct`) and the new default-valued signature (`CancellationToken ct = default`). | Low | Match the existing convention in `IEshopOrderClient` (all other methods use `CancellationToken ct = default`). Callers in `ScanPackingOrderHandler` already pass an explicit token. |
| `ShoptetOrderClient` constructor change (new `IOptions<ShoptetOrdersSettings>` param) breaks the `AddHttpClient<ShoptetOrderClient>` factory if anything inspects the closure of `ShoptetOrderClient`. | Low | `AddHttpClient<T>` injects all constructor dependencies from the root container — `IOptions<ShoptetOrdersSettings>` is registered as singleton by `ShoptetOrdersModule.AddOptions<ShoptetOrdersSettings>()`, so the typed-client factory will resolve it normally. No DI changes required. |

## Specification Amendments

The spec is unambiguous and ready to implement as written. Two small additions are recommended:

1. **Add the architectural enforcement test.** The spec defines NFR-3 ("knowledge of status ids confined to four locations") but does not require it to be enforced. Without a test, NFR-3 is documentation only and the same finding will return on the next daily arch-review. Add a reflection/regex test under `backend/test/Anela.Heblo.Tests/Architecture/` asserting:
   - No file under `Anela.Heblo.Application/Features/Packaging/` references `ShoptetOrdersSettings`, `PackingStateId`, or `PackedStateId`.
   - This mirrors the `Leaflet`/`KnowledgeBase` boundary test referenced in §Cross-Module Communication Example of `development_guidelines.md`.
2. **Tighten the XML doc on `PackingOrder.StatusId`.** Because the spec defers the removal of `StatusId` to a follow-up, document on the property itself that it must not be read to derive eligibility — readers must use `IsEligibleForPacking`. This is the cheap version of the deferred deprecation and prevents silent re-introduction of the duplicated rule before the follow-up lands. Suggested text:
   > Kept for diagnostic/logging purposes. Do **not** derive packing eligibility from this value — use `IsEligibleForPacking` instead.

Neither amendment changes behaviour, scope, or contracts beyond what FR-1/FR-2 already specify.

## Prerequisites

None. All required configuration values (`ShoptetOrders:PackingStateId`, `ShoptetOrders:PackedStateId`) already exist and are bound in `ShoptetOrdersModule`; both target environments (development user secrets, staging Key Vault, production Key Vault) already supply them. No migrations, no infrastructure changes, no new packages, no feature-flag wiring.