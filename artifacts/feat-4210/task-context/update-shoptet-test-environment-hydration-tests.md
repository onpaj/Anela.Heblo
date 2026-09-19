### task: update-shoptet-test-environment-hydration-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs`

This file's `_client` field is a **single-interface consumer** — every call it makes (`CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`, plus `UpdateStatusAsync`) — wait: this file also calls `UpdateStatusAsync`, which stays on `IEshopOrderClient`. Re-checking the source: `HydrateTestEnvironment` calls `ListByExternalCodePrefixAsync` (line 176), `CreateOrderAsync` (line 242), `UpdateStatusAsync` (lines 245, 252, 291), and `PurgeTestOrders` calls `ListByExternalCodePrefixAsync` (line 280), `UpdateStatusAsync` (line 291), `DeleteOrderAsync` (line 293).

Because `UpdateStatusAsync` stays on `IEshopOrderClient` and is used in this file, **this file is also a mixed-usage consumer**, not a pure single-interface one — the arch-review's file-by-file read undercounted this. It needs both `_client: IEshopOrderClient` (for `UpdateStatusAsync`) and a new `_testClient: IShoptetOrderTestClient` (for `CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`), following the same pattern as `BlockOrderProcessingIntegrationTests`.

- [ ] **Step 1: Add the `IShoptetOrderTestClient` using and field**

Change the `using` block (lines 1-7) from:

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
```

to:

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
```

Change the field block (lines 15-19) from:

```csharp
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _client;
    private readonly IEshopStockClient _stockClient;
    private readonly ITestOutputHelper _output;
```

to:

```csharp
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _client;
    private readonly IShoptetOrderTestClient _testClient;
    private readonly IEshopStockClient _stockClient;
    private readonly ITestOutputHelper _output;
```

Change the constructor body (lines 63-67) from:

```csharp
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _stockClient = fixture.ServiceProvider.GetRequiredService<IEshopStockClient>();
        _output = output;
```

to:

```csharp
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _testClient = fixture.ServiceProvider.GetRequiredService<IShoptetOrderTestClient>();
        _stockClient = fixture.ServiceProvider.GetRequiredService<IEshopStockClient>();
        _output = output;
```

- [ ] **Step 2: Repoint the relocated-method call sites in `HydrateTestEnvironment`**

Change (around line 176):
```csharp
        var existingOrders = await _client.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);
```
to:
```csharp
        var existingOrders = await _testClient.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);
```

Change (around line 242):
```csharp
                var code = await _client.CreateOrderAsync(request, ct);
```
to:
```csharp
                var code = await _testClient.CreateOrderAsync(request, ct);
```

Leave lines 245, 252 (`_client.UpdateStatusAsync(...)`) unchanged — `UpdateStatusAsync` stays on `IEshopOrderClient`.

- [ ] **Step 3: Repoint the relocated-method call sites in `PurgeTestOrders`**

Change (around line 280):
```csharp
        var orders = await _client.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);
```
to:
```csharp
        var orders = await _testClient.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);
```

Leave line 291 (`_client.UpdateStatusAsync(order.Code, deletableStateId, ct)`) unchanged.

Change (around line 293):
```csharp
                await _client.DeleteOrderAsync(order.Code, ct);
```
to:
```csharp
                await _testClient.DeleteOrderAsync(order.Code, ct);
```

- [ ] **Step 4: Build the test project**

Run:
```bash
cd backend && dotnet build test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 5: Run this test class**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~ShoptetTestEnvironmentHydrationTests"
```
Expected: all tests pass. The 4 `Guard_*` tests run for real (they only touch `ShoptetTestGuard`, no HTTP); `HydrateTestEnvironment` and `PurgeTestOrders` early-return because `SHOPTET_HYDRATE` is unset in this environment — this proves DI resolves `IShoptetOrderTestClient` in the constructor and the rewritten call sites compile.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs
git commit -m "test(shoptet-orders): use IShoptetOrderTestClient for order lifecycle calls in ShoptetTestEnvironmentHydrationTests"
```

---

