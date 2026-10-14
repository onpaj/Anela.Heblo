# Design: Split test-only lifecycle methods out of IEshopOrderClient

## Component Design

### `IShoptetOrderTestClient` (new interface)
- **Location:** `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs`
- **Namespace:** `Anela.Heblo.Adapters.ShoptetApi.Orders`
- **Responsibility:** Declares the 4 Shoptet order lifecycle operations needed only for integration-test setup/teardown against the live Shoptet store — creating a test order, deleting a test order, listing recent orders, and looking up orders by a test-seed external-code prefix. Consumed exclusively by adapter integration tests, never by Application handlers.
- **Contract:**
  - `Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default)`
  - `Task DeleteOrderAsync(string orderCode, CancellationToken ct = default)`
  - `Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default)`
  - `Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default)`
- Signatures are copied verbatim from the current `IEshopOrderClient` — no parameter, return type, or default-value changes.

### `IEshopOrderClient` (narrowed existing interface)
- **Location:** `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` (unchanged file, reduced content)
- **Responsibility:** Application-facing contract for order status/remark management and packing-state transitions — the operations Application handlers (`BlockOrderProcessingHandler`, `ScanPackingOrderHandler`, `CompletePackingOrderHandler`, `CompleteDeliveredOrdersJob`, `PrintExpeditionOrderHandler`) actually call.
- **Contract after change (7 methods, unchanged from today):**
  - `GetOrderStatusIdAsync`
  - `UpdateStatusAsync`
  - `GetEshopRemarkAsync`
  - `UpdateEshopRemarkAsync`
  - `AppendEshopRemarkAsync`
  - `ListOrdersByStatusAsync`
  - `MarkAsPackedAsync`
- **Removed:** `CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync` (relocated to `IShoptetOrderTestClient`).

### `ShoptetOrderClient` (concrete implementation, existing class)
- **Location:** `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs`
- **Change:** Class declaration gains a third implemented interface: `public class ShoptetOrderClient : IEshopOrderClient, IShoptetOrderTestClient, IShoptetExpeditionOrderSource`. No method bodies move, change, or duplicate — all 11 existing method implementations stay exactly where they are; they now simply satisfy two interface contracts (`IEshopOrderClient` for 7 of them, `IShoptetOrderTestClient` for the other 4) instead of one.

### DI Registration (existing extension method, additive change)
- **Location:** `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- **Change:** One new line added immediately after the existing `IEshopOrderClient` / `IShoptetExpeditionOrderSource` registrations:
  ```csharp
  services.AddTransient<IShoptetOrderTestClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
  ```
  This reuses the single existing `AddHttpClient<ShoptetOrderClient>(...)` pipeline — no new `HttpClient`, no new configuration section.

### Consumers (integration test files, updated to match new contract shape)
- **`BlockOrderProcessingIntegrationTests.cs`** — mixed consumer. Gains a second field resolved from the same fixture container:
  ```csharp
  private readonly IEshopOrderClient _client;              // existing, unchanged type
  private readonly IShoptetOrderTestClient _testClient;    // new
  ```
  `_client` keeps calling `UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync`. `_testClient` takes over the `CreateOrderAsync` / `DeleteOrderAsync` call sites.
- **`ShoptetTestEnvironmentHydrationTests.cs`** — single-interface consumer. Its existing field is retyped from `IEshopOrderClient` to `IShoptetOrderTestClient`; every existing call site (`CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`) is unaffected because the method names/signatures are unchanged, only the field's declared interface type changes.
- **`PickingListIntegrationTests.cs`** — single-interface consumer. Same treatment: field retyped from `IEshopOrderClient` to `IShoptetOrderTestClient`, `GetRecentOrdersAsync` call site unaffected.

## Data Schemas
No new or changed data schemas. `CreateEshopOrderRequest` and `EshopOrderSummary` (both defined in `Anela.Heblo.Application.Features.ShoptetOrders`) are reused as-is by the new `IShoptetOrderTestClient` interface exactly as they are used today by `IEshopOrderClient` — no field, shape, or serialization changes. No database schema, no wire-format/API contract with Shoptet itself changes; only the C# interface boundary inside this codebase moves.
