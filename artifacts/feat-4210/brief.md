## Module
ShoptetOrders

## Finding
`IEshopOrderClient` (defined at `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`) has 11 methods. Four have zero callers in production Application code — all call sites are in integration tests under `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/`:

| Method | Only caller |
|---|---|
| `CreateOrderAsync` | `BlockOrderProcessingIntegrationTests.cs:89`, `ShoptetTestEnvironmentHydrationTests.cs:242` |
| `DeleteOrderAsync` | `BlockOrderProcessingIntegrationTests.cs:109,181`, `ShoptetTestEnvironmentHydrationTests.cs:293` |
| `GetRecentOrdersAsync` | `PickingListIntegrationTests.cs:61` |
| `ListByExternalCodePrefixAsync` | `ShoptetTestEnvironmentHydrationTests.cs:176,280` |

These methods manage test data (create/delete orders, list by prefix) — Shoptet API housekeeping operations needed for integration test setup/teardown, not Application business logic.

Issue #3259 ("IEshopOrderClient carries three dead interface methods") was filed and merged. That removed methods with no callers at all. The four above were left because they have callers — but only in tests. This is a distinct ISP finding: the Application-layer interface carries test infrastructure methods.

## Why it matters
- ISP violation: any Application-layer mock of `IEshopOrderClient` (e.g. unit tests for `BlockOrderProcessingHandler`) must provide stub implementations of four methods that the handler never calls
- The interface definition in the Application layer should reflect what Application handlers need, not what adapter integration tests need for order lifecycle management

## Suggested fix
Extract a separate `IShoptetOrderTestClient` (or similar) interface in the adapter project — e.g. in `Anela.Heblo.Adapters.ShoptetApi` — that declares `CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, and `ListByExternalCodePrefixAsync`. Have `ShoptetOrderClient` implement both interfaces. Integration tests reference `IShoptetOrderTestClient` directly from the adapter project; `IEshopOrderClient` in `Application/Features/ShoptetOrders/` shrinks to the 7 methods that Application handlers actually use.

---
_Filed by daily arch-review routine on 2026-09-16._
