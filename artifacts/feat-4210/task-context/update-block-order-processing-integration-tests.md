### task: update-block-order-processing-integration-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs`

This file is the one **mixed-usage** consumer: it calls both the 4 relocated methods (`CreateOrderAsync`, `DeleteOrderAsync`) and 3 methods that stay on `IEshopOrderClient` (`UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync`) through the same `_client` field today. It needs a second field for the relocated methods.

- [ ] **Step 1: Add the `IShoptetOrderTestClient` using, field, and constructor resolution**

Change the `using` block at the top of the file (lines 1-10) from:

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
```

to (adding the adapter namespace):

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
```

Change the field block (lines 25-28) from:

```csharp
    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _client;
    private readonly ILogger<BlockOrderProcessingHandler> _logger;
    private readonly ITestOutputHelper _output;
```

to:

```csharp
    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _client;
    private readonly IShoptetOrderTestClient _testClient;
    private readonly ILogger<BlockOrderProcessingHandler> _logger;
    private readonly ITestOutputHelper _output;
```

Change the constructor body (lines 33-37) from:

```csharp
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _logger = fixture.ServiceProvider.GetRequiredService<ILogger<BlockOrderProcessingHandler>>();
        _output = output;
```

to:

```csharp
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _testClient = fixture.ServiceProvider.GetRequiredService<IShoptetOrderTestClient>();
        _logger = fixture.ServiceProvider.GetRequiredService<ILogger<BlockOrderProcessingHandler>>();
        _output = output;
```

- [ ] **Step 2: Repoint the `CreateOrderAsync`/`DeleteOrderAsync` call sites from `_client` to `_testClient`**

There are 3 call sites across 2 methods. In `BlockOrder_PreservesExistingEshopRemark_AndAppendsOnNewLine` (around line 89):

Change:
```csharp
            code = await _client.CreateOrderAsync(BuildOrderRequest("BLOCK-ORDER-TEST-APPEND", shippingGuid, paymentGuid), ct);
```
to:
```csharp
            code = await _testClient.CreateOrderAsync(BuildOrderRequest("BLOCK-ORDER-TEST-APPEND", shippingGuid, paymentGuid), ct);
```

Still in that same method, in the `finally` block (around line 109):

Change:
```csharp
                await _client.DeleteOrderAsync(code, ct);
```
to:
```csharp
                await _testClient.DeleteOrderAsync(code, ct);
```

In `RunTest` (around line 151):

Change:
```csharp
            code = await _client.CreateOrderAsync(BuildOrderRequest(externalCode, shippingGuid, paymentGuid), ct);
```
to:
```csharp
            code = await _testClient.CreateOrderAsync(BuildOrderRequest(externalCode, shippingGuid, paymentGuid), ct);
```

Still in `RunTest`, in its `finally` block (around line 181):

Change:
```csharp
                await _client.DeleteOrderAsync(code, ct);
```
to:
```csharp
                await _testClient.DeleteOrderAsync(code, ct);
```

Leave every other `_client.*` call in this file (`UpdateStatusAsync`, `UpdateEshopRemarkAsync`, `GetEshopRemarkAsync`) exactly as-is — those 3 methods stay on `IEshopOrderClient` and `_client` is still the correct field for them.

- [ ] **Step 3: Build the test project**

Run:
```bash
cd backend && dotnet build test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Run this test class (it self-skips against the live store unless `SHOPTET_BLOCK_ORDER=1` is set, so this proves compile + no-op execution, not live-API behavior)**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~BlockOrderProcessingIntegrationTests"
```
Expected: all 4 tests pass. `BlockOrder_PreservesExistingEshopRemark_AndAppendsOnNewLine` and `RunTest`-based tests early-return without calling Shoptet because `SHOPTET_BLOCK_ORDER` is unset in this environment — this run validates the DI container resolves `IShoptetOrderTestClient` successfully (via the constructor) and that the rewritten call sites compile, without needing live Shoptet credentials.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs
git commit -m "test(shoptet-orders): use IShoptetOrderTestClient for order create/delete in BlockOrderProcessingIntegrationTests"
```

---

