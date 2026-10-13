### task: update-picking-list-integration-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`

This file's `_orderClient` field is used for `GetRecentOrdersAsync` (relocated) **and** `UpdateStatusAsync` (stays on `IEshopOrderClient`, called at lines 80 and 117). So — same correction as the previous task — this is also a **mixed-usage** consumer, not the pure single-interface case the arch-review described; it needs both interfaces.

- [ ] **Step 1: Add the `IShoptetOrderTestClient` using and field**

Change the `using` block (lines 1-13) from:

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.Picking;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
```

to:

```csharp
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Application.Features.Logistics.Picking;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
```

Change the field block (lines 25-30) from:

```csharp
    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _orderClient;
    private readonly IPickingListSource _source;
    private readonly IPrintQueueSink _sink;
    private readonly ITestOutputHelper _output;
    private readonly string _sinkFolder;
```

to:

```csharp
    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _orderClient;
    private readonly IShoptetOrderTestClient _testOrderClient;
    private readonly IPickingListSource _source;
    private readonly IPrintQueueSink _sink;
    private readonly ITestOutputHelper _output;
    private readonly string _sinkFolder;
```

Change the constructor body (lines 36-41) from:

```csharp
        _configuration = fixture.Configuration;
        _orderClient = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _source = fixture.ServiceProvider.GetRequiredService<IPickingListSource>();
        _sink = fixture.ServiceProvider.GetRequiredService<IPrintQueueSink>();
        _output = output;
        _sinkFolder = fixture.ServiceProvider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value.PrintQueueFolder;
```

to:

```csharp
        _configuration = fixture.Configuration;
        _orderClient = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _testOrderClient = fixture.ServiceProvider.GetRequiredService<IShoptetOrderTestClient>();
        _source = fixture.ServiceProvider.GetRequiredService<IPickingListSource>();
        _sink = fixture.ServiceProvider.GetRequiredService<IPrintQueueSink>();
        _output = output;
        _sinkFolder = fixture.ServiceProvider.GetRequiredService<IOptions<PrintPickingListOptions>>().Value.PrintQueueFolder;
```

- [ ] **Step 2: Repoint the `GetRecentOrdersAsync` call site**

Change (around line 61):
```csharp
        var orders = await _orderClient.GetRecentOrdersAsync(20, ct);
```
to:
```csharp
        var orders = await _testOrderClient.GetRecentOrdersAsync(20, ct);
```

Leave lines 80 and 117 (`_orderClient.UpdateStatusAsync(...)`) unchanged — `UpdateStatusAsync` stays on `IEshopOrderClient`.

- [ ] **Step 3: Build the test project**

Run:
```bash
cd backend && dotnet build test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 4: Run this test class**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName~PickingListIntegrationTests"
```
Expected: this test hits the live Shoptet store (guarded by `ShoptetTestGuard.Assert`, not by an env-var early return like the other two files) and requires `Shoptet:IsTestEnvironment=true` plus valid user-secret credentials to run at all. In this sandboxed planning/implementation environment those secrets are not configured, so the expected outcome is either (a) the test throws `InvalidOperationException` from `ShoptetTestGuard.Assert` / missing configuration before making any HTTP call, which still proves the rewritten `_testOrderClient` field and call site compile and resolve from DI, or (b) if credentials happen to be present, the test passes exactly as it did before this refactor since no method body changed. A pre-existing `InvalidOperationException` about missing Shoptet configuration is not a regression introduced by this task; a compile error or a `null`/DI-resolution failure for `IShoptetOrderTestClient` is.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs
git commit -m "test(shoptet-orders): use IShoptetOrderTestClient for GetRecentOrdersAsync in PickingListIntegrationTests"
```

---

