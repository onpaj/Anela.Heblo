# Architecture Review: Split test-only lifecycle methods out of IEshopOrderClient

## Skip Design: true

## Architectural Fit Assessment
This is a backend-only, compile-time interface reorganization inside the Shoptet adapter and its consuming Application feature (`ShoptetOrders`). It has no UI, no data model, and no new runtime behavior — it aligns cleanly with this codebase's existing Clean Architecture separation: `IEshopOrderClient` is defined in `Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` and implemented by `ShoptetOrderClient` in `Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`. The concrete class already implements two interfaces (`IEshopOrderClient` and `IShoptetExpeditionOrderSource`, the latter defined in the adapter's own `Expedition` area), so adding a third, adapter-owned interface on the same concrete class is consistent with an existing pattern in this file, not a new one.

I verified the finding against the current source:
- `IEshopOrderClient` currently declares 11 methods (confirmed by reading `IEshopOrderClient.cs`).
- `ShoptetOrderClient : IEshopOrderClient, IShoptetExpeditionOrderSource` (confirmed by reading `ShoptetOrderClient.cs` line 12).
- DI registration (`ShoptetApiAdapterServiceCollectionExtensions.cs`): `services.AddHttpClient<ShoptetOrderClient>(...)` followed by `services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>())` and a parallel line for `IShoptetExpeditionOrderSource`. This is the exact pattern the new `IShoptetOrderTestClient` registration should follow.
- Confirmed all 4 call sites named in the brief and their exact usage:
  - `BlockOrderProcessingIntegrationTests.cs`: injects a single `IEshopOrderClient _client` and calls **both** the 4 test-only methods (`CreateOrderAsync`, `DeleteOrderAsync`) **and** methods that stay on `IEshopOrderClient` (`UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync`) on the same instance. This file needs **both** interfaces after the split.
  - `ShoptetTestEnvironmentHydrationTests.cs`: injects `IEshopOrderClient _client` but only ever calls `CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync` — no calls to any surviving `IEshopOrderClient` method. This file can switch its field entirely to the new interface.
  - `PickingListIntegrationTests.cs`: injects `IEshopOrderClient _orderClient`, calls only `GetRecentOrdersAsync`. Switches entirely to the new interface.
- `Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces an explicit allowlist of cross-module references, including one line permitting `Packaging.ScanPackingOrderHandler -> Application.Features.ShoptetOrders.IEshopOrderClient`. Removing 4 methods from `IEshopOrderClient` does not touch this allowlist (the allowlist is per-type, not per-member), so no change is needed there — but it is worth a grep sweep in implementation to confirm nothing else in Application code references the 4 removed methods, matching FR-2's regression check.

## Proposed Architecture

### Component Overview
```
Anela.Heblo.Application.Features.ShoptetOrders
  IEshopOrderClient (7 methods — Application-facing contract)
        ▲
        │ implements
        │
Anela.Heblo.Adapters.ShoptetApi.Orders
  ShoptetOrderClient : IEshopOrderClient, IShoptetOrderTestClient, IShoptetExpeditionOrderSource
        │
        │ implements
        ▼
Anela.Heblo.Adapters.ShoptetApi.Orders   (new file, same folder as ShoptetOrderClient)
  IShoptetOrderTestClient (4 methods — test-infrastructure contract)
        ▲
        │ referenced by
        │
Anela.Heblo.Adapters.Shoptet.Tests (integration tests)
  BlockOrderProcessingIntegrationTests   → needs IEshopOrderClient AND IShoptetOrderTestClient
  ShoptetTestEnvironmentHydrationTests   → needs IShoptetOrderTestClient only
  PickingListIntegrationTests            → needs IShoptetOrderTestClient only
```

### Key Design Decisions

#### Decision 1: New interface location and name
**Options considered:**
- (a) Put the new interface in the Application layer alongside `IEshopOrderClient`, just under a different name.
- (b) Put it in the adapter project (`Anela.Heblo.Adapters.ShoptetApi`), as the brief suggests.

**Chosen approach:** (b) — `IShoptetOrderTestClient`, placed at `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs`, next to `ShoptetOrderClient.cs`.

**Rationale:** The whole point of the finding is that these methods are test/adapter housekeeping concerns, not Application business logic. Leaving the interface in the Application layer (option a) would still let Application-layer code depend on it and would not change the module-boundary story. Placing it in the adapter project matches the brief's suggested fix, matches where `IShoptetExpeditionOrderSource` already lives (same adapter project, same general area), and correctly signals "adapter/integration-test concern" to future readers.

#### Decision 2: How integration tests obtain the new interface
**Options considered:**
- (a) Each test resolves `ShoptetOrderClient` concretely from DI and casts/uses it directly.
- (b) Each test resolves `IShoptetOrderTestClient` from DI (registered alongside `IEshopOrderClient`), keeping tests interface-based.

**Chosen approach:** (b).

**Rationale:** The existing tests already resolve everything through interfaces via `fixture.ServiceProvider.GetRequiredService<T>()` — resolving the concrete class directly would be a step backward in testability/consistency and is unnecessary since DI registration cost is trivial. `BlockOrderProcessingIntegrationTests` will hold two fields (`_client` for `IEshopOrderClient`, `_testClient` for `IShoptetOrderTestClient`), both resolved from the same fixture container, both backed by the same singleton/transient `ShoptetOrderClient` instance per the existing `AddHttpClient<ShoptetOrderClient>` registration.

#### Decision 3: DI registration lifetime/shape
**Options considered:**
- (a) Register `IShoptetOrderTestClient` with its own `AddHttpClient` pipeline (separate HttpClient instance).
- (b) Register it exactly like `IShoptetExpeditionOrderSource` is registered today — `services.AddTransient<IShoptetOrderTestClient>(sp => sp.GetRequiredService<ShoptetOrderClient>())`, reusing the single `AddHttpClient<ShoptetOrderClient>` pipeline.

**Chosen approach:** (b).

**Rationale:** There is exactly one concrete `ShoptetOrderClient` and one HTTP pipeline for it today; the interfaces are just different facets of the same object, mirroring how `IEshopOrderClient` and `IShoptetExpeditionOrderSource` are both wired to the same `ShoptetOrderClient` registration two lines apart in `ShoptetApiAdapterServiceCollectionExtensions.cs`. Adding a third `AddTransient<IShoptetOrderTestClient>(...)` line immediately after the existing two is the minimal, convention-matching change.

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs` — declares the 4 relocated method signatures, namespace `Anela.Heblo.Adapters.ShoptetApi.Orders`.
- Edit: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` — remove the 4 method declarations (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) and their XML doc comments if any (none of these 4 currently carry doc comments; the surrounding doc comments belong to other, surviving methods and must stay attached to those).
- Edit: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` — add `IShoptetOrderTestClient` to the class's interface list (`public class ShoptetOrderClient : IEshopOrderClient, IShoptetOrderTestClient, IShoptetExpeditionOrderSource`). No method bodies move or change.
- Edit: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` — add `services.AddTransient<IShoptetOrderTestClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());` immediately after the existing `IEshopOrderClient`/`IShoptetExpeditionOrderSource` registrations.
- Edit: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs` — add a `using Anela.Heblo.Adapters.ShoptetApi.Orders;`, add a second field `IShoptetOrderTestClient _testClient`, resolve it in the constructor, and repoint the `CreateOrderAsync`/`DeleteOrderAsync` call sites (lines ~89, ~109, ~181 per the brief) from `_client` to `_testClient`. Leave `UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync` calls on `_client` (`IEshopOrderClient`) unchanged.
- Edit: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs` — change the `_client` field's declared type from `IEshopOrderClient` to `IShoptetOrderTestClient` and its resolution accordingly (add the adapter namespace `using`); no call-site changes needed since every call on `_client` in this file already targets one of the 4 relocated methods.
- Edit: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` — same treatment: change `_orderClient` field type from `IEshopOrderClient` to `IShoptetOrderTestClient`, add the adapter namespace `using`.

### Interfaces and Contracts
```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs
namespace Anela.Heblo.Adapters.ShoptetApi.Orders;

/// <summary>
/// Shoptet order lifecycle operations needed only for integration-test setup/teardown
/// (creating and deleting real test orders in the live Shoptet store, and looking them
/// up by test-seed prefix). Not part of the Application-layer contract — Application
/// handlers must never depend on this interface.
/// </summary>
public interface IShoptetOrderTestClient
{
    Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
    Task DeleteOrderAsync(string orderCode, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default);
}
```
Note: `CreateEshopOrderRequest` and `EshopOrderSummary` are currently defined in the Application project (`Anela.Heblo.Application.Features.ShoptetOrders`). The new adapter interface will need a `using Anela.Heblo.Application.Features.ShoptetOrders;` to reference these DTOs — this is an acceptable, pre-existing dependency direction (adapter depends on Application DTOs), identical to how `ShoptetOrderClient` itself already references them today. Moving these DTOs is explicitly out of scope per the spec.

`IEshopOrderClient` after the change (7 methods): `GetOrderStatusIdAsync`, `UpdateStatusAsync`, `GetEshopRemarkAsync`, `UpdateEshopRemarkAsync`, `AppendEshopRemarkAsync`, `ListOrdersByStatusAsync`, `MarkAsPackedAsync`.

### Data Flow
No data flow changes — same HTTP calls to the same Shoptet endpoints, through the same `ShoptetOrderClient` instance. The only change is which C# interface a given caller uses to reach the same method body.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Mixed usage in `BlockOrderProcessingIntegrationTests.cs` is missed and the file fails to compile after removing methods from `IEshopOrderClient` | Medium | Explicitly called out in Implementation Guidance: this file needs both interfaces, unlike the other two test files. |
| A production Application code path unexpectedly calls one of the 4 removed methods (contradicting the brief's "zero callers" claim) | Low | FR-2's acceptance criteria requires a repo-wide grep/compile check for the 4 method names in production code before removing them; a build failure would surface this immediately since it's a compile-time interface change. |
| DTOs (`CreateEshopOrderRequest`, `EshopOrderSummary`) end up referenced from the adapter's new interface, creating a slightly awkward Adapter→Application DTO dependency | Low | Already how `ShoptetOrderClient` works today (it's in the adapter project and already returns/consumes these Application DTOs); no new dependency direction is introduced, only a new interface reusing existing types. Explicitly out of scope to relocate these DTOs. |

## Specification Amendments
- FR-1 / API design: confirm the new interface's namespace is `Anela.Heblo.Adapters.ShoptetApi.Orders` (same namespace as `ShoptetOrderClient`), not a new sub-namespace — no reason found in the codebase to introduce one.
- FR-3: DI wiring should be `services.AddTransient<IShoptetOrderTestClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());`, placed immediately after the existing `IEshopOrderClient` / `IShoptetExpeditionOrderSource` registrations in `ShoptetApiAdapterServiceCollectionExtensions.cs`.
- FR-4: `BlockOrderProcessingIntegrationTests.cs` requires **both** interfaces (mixed usage), while `ShoptetTestEnvironmentHydrationTests.cs` and `PickingListIntegrationTests.cs` can switch their existing field entirely to `IShoptetOrderTestClient` (confirmed via source inspection — no surviving-`IEshopOrderClient` calls exist in those two files).

## Prerequisites
None. No migrations, no config, no infrastructure changes — this can be implemented directly against the current `main`/feature branch state.
