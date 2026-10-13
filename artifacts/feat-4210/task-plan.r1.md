# Split test-only lifecycle methods out of IEshopOrderClient Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the 4 Shoptet test-data lifecycle methods (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) out of the Application-layer `IEshopOrderClient` interface into a new adapter-owned interface `IShoptetOrderTestClient`, so the Application-layer contract only exposes the 7 methods handlers actually call.

**Architecture:** `ShoptetOrderClient` (adapter project, `Anela.Heblo.Adapters.ShoptetApi.Orders`) already implements `IEshopOrderClient` (Application layer) and `IShoptetExpeditionOrderSource` (adapter layer). We add a third adapter-owned interface, `IShoptetOrderTestClient`, declaring the 4 relocated methods verbatim, have `ShoptetOrderClient` implement it (no method bodies move), register it in DI alongside the existing `IEshopOrderClient` registration (same `AddTransient(sp => sp.GetRequiredService<ShoptetOrderClient>())` pattern), then repoint the 3 integration test files that call the 4 methods from `IEshopOrderClient` to `IShoptetOrderTestClient`. This is a pure compile-time interface reorganization — no runtime behavior changes.

**Tech Stack:** .NET 8, C#, ASP.NET Core DI (`Microsoft.Extensions.DependencyInjection`), xUnit, FluentAssertions.

---

## File Structure

| File | Change |
|---|---|
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs` | **Create.** New adapter-owned interface, 4 method signatures copied verbatim from `IEshopOrderClient`. |
| `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs` | **Modify.** Remove the 4 relocated method declarations. 7 methods remain. |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` | **Modify.** Add `IShoptetOrderTestClient` to the class's implemented-interface list (line 12). No method bodies move. |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` | **Modify.** Add one DI registration line after line 52. |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/BlockOrderProcessingIntegrationTests.cs` | **Modify.** Add a second field/resolution for `IShoptetOrderTestClient`; repoint `CreateOrderAsync`/`DeleteOrderAsync` call sites to it. Keep `IEshopOrderClient` field for the methods that stay. |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/ShoptetTestEnvironmentHydrationTests.cs` | **Modify.** Retype the existing `_client` field from `IEshopOrderClient` to `IShoptetOrderTestClient`. |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` | **Modify.** Retype the existing `_orderClient` field from `IEshopOrderClient` to `IShoptetOrderTestClient`. |

No other files need to change. A repo-wide grep already confirmed (during planning) that no Application-layer production code or unit test references any of the 4 method names via `IEshopOrderClient` — FR-2's and FR-5's regression checks are satisfied by construction; each task below still re-runs the grep after its own edit as a belt-and-suspenders check.

Since this is a behavior-preserving refactor (NFR-3) with no new logic to unit-test, "TDD" here means: after each edit, prove the change compiles and the existing test suite for the affected project(s) still passes — that is the executable spec for "nothing broke."

---

### task: create-shoptet-order-test-client-interface

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs`

- [ ] **Step 1: Create the new interface file**

Create `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs` with this exact content:

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders;

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

Note: `CreateEshopOrderRequest` and `EshopOrderSummary` live in `Anela.Heblo.Application.Features.ShoptetOrders` (Application layer). The `using` above is required — this mirrors the existing, pre-approved dependency direction: `ShoptetOrderClient.cs` (same adapter project) already has `using Anela.Heblo.Application.Features.ShoptetOrders;` at its top to reference these same DTOs. Do not move the DTOs — that is explicitly out of scope.

- [ ] **Step 2: Build the adapter project to verify the new file compiles standalone**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded.` The new interface is not referenced anywhere yet, so this only proves the file itself is valid C# with a resolvable `using`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/IShoptetOrderTestClient.cs
git commit -m "feat(shoptet-orders): add IShoptetOrderTestClient adapter interface"
```

---

### task: shrink-ieshoporderclient-interface

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`

- [ ] **Step 1: Remove the 4 relocated method declarations**

The current file (`backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs`) is:

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders;

public interface IEshopOrderClient
{
    Task<string> CreateOrderAsync(CreateEshopOrderRequest request, CancellationToken ct = default);
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    /// <summary>
    /// Returns the current internal (staff-facing) remark for the given order,
    /// as returned by GET /api/orders/{code}?include=notes → data.order.notes.eshopRemark.
    /// Returns an empty string if Shoptet sends null or the notes object is absent.
    /// </summary>
    Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Overwrites the order's internal (staff-facing) remark via
    /// PATCH /api/orders/{code}/notes with body {"data":{"eshopRemark":"..."}}.
    /// The caller is responsible for preserving any existing content (read-modify-write).
    /// </summary>
    Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default);

    /// <summary>
    /// Read-modify-write helper: appends <paramref name="text"/> to the order's current
    /// eshop remark, separated by a newline. If the order has no remark yet, the remark
    /// becomes <paramref name="text"/> verbatim (no leading separator).
    /// Equivalent to:
    ///   var current = await GetEshopRemarkAsync(orderCode, ct);
    ///   var updated = string.IsNullOrEmpty(current) ? text : $"{current}\n{text}";
    ///   await UpdateEshopRemarkAsync(orderCode, updated, ct);
    /// </summary>
    Task AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default);

    Task DeleteOrderAsync(string orderCode, CancellationToken ct = default);
    Task<List<EshopOrderSummary>> GetRecentOrdersAsync(int count = 20, CancellationToken ct = default);

    /// <summary>
    /// Returns every order currently in the given Shoptet status, across all pages
    /// (GET /api/orders?statusId={id}, itemsPerPage=50). Maps to the Application-layer
    /// <see cref="EshopOrderSummary"/> (code, externalCode, email, statusId).
    /// </summary>
    Task<List<EshopOrderSummary>> ListOrdersByStatusAsync(int statusId, CancellationToken ct = default);

    Task<List<EshopOrderSummary>> ListByExternalCodePrefixAsync(string prefix, string? emailFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Transitions the order to the configured "packed" state
    /// (Shoptet "Zabaleno", id 52 by default). Called by the Balení screen
    /// after a successful scan + shipment creation.
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
```

Replace it with this exact content — the 4 relocated method lines (`CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, `ListByExternalCodePrefixAsync`) are removed; every remaining method, its doc comment, and its ordering is otherwise untouched:

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders;

public interface IEshopOrderClient
{
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
    Task UpdateStatusAsync(string orderCode, int statusId, CancellationToken ct = default);
    /// <summary>
    /// Returns the current internal (staff-facing) remark for the given order,
    /// as returned by GET /api/orders/{code}?include=notes → data.order.notes.eshopRemark.
    /// Returns an empty string if Shoptet sends null or the notes object is absent.
    /// </summary>
    Task<string> GetEshopRemarkAsync(string orderCode, CancellationToken ct = default);

    /// <summary>
    /// Overwrites the order's internal (staff-facing) remark via
    /// PATCH /api/orders/{code}/notes with body {"data":{"eshopRemark":"..."}}.
    /// The caller is responsible for preserving any existing content (read-modify-write).
    /// </summary>
    Task UpdateEshopRemarkAsync(string orderCode, string eshopRemark, CancellationToken ct = default);

    /// <summary>
    /// Read-modify-write helper: appends <paramref name="text"/> to the order's current
    /// eshop remark, separated by a newline. If the order has no remark yet, the remark
    /// becomes <paramref name="text"/> verbatim (no leading separator).
    /// Equivalent to:
    ///   var current = await GetEshopRemarkAsync(orderCode, ct);
    ///   var updated = string.IsNullOrEmpty(current) ? text : $"{current}\n{text}";
    ///   await UpdateEshopRemarkAsync(orderCode, updated, ct);
    /// </summary>
    Task AppendEshopRemarkAsync(string orderCode, string text, CancellationToken ct = default);

    /// <summary>
    /// Returns every order currently in the given Shoptet status, across all pages
    /// (GET /api/orders?statusId={id}, itemsPerPage=50). Maps to the Application-layer
    /// <see cref="EshopOrderSummary"/> (code, externalCode, email, statusId).
    /// </summary>
    Task<List<EshopOrderSummary>> ListOrdersByStatusAsync(int statusId, CancellationToken ct = default);

    /// <summary>
    /// Transitions the order to the configured "packed" state
    /// (Shoptet "Zabaleno", id 52 by default). Called by the Balení screen
    /// after a successful scan + shipment creation.
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
```

- [ ] **Step 2: Try building the Application project — expect it to still succeed (no production callers of the 4 methods)**

Run:
```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: `Build succeeded.` If this fails with "does not implement interface member" or "no such method" errors referencing `CreateOrderAsync`/`DeleteOrderAsync`/`GetRecentOrdersAsync`/`ListByExternalCodePrefixAsync`, STOP — this means the spec's "zero Application-layer callers" claim (FR-2's regression check) was wrong; do not proceed with the remaining tasks until that caller is found and resolved.

- [ ] **Step 3: Grep the Application project for any remaining reference to the 4 removed methods (defense in depth)**

Run:
```bash
cd backend && grep -rn "CreateOrderAsync\|DeleteOrderAsync\|GetRecentOrdersAsync\|ListByExternalCodePrefixAsync" src/Anela.Heblo.Application/
```
Expected: no output (no matches). This confirms FR-2's acceptance criterion — "No Application-layer production code references the 4 removed methods via `IEshopOrderClient`."

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs
git commit -m "refactor(shoptet-orders): remove test-only lifecycle methods from IEshopOrderClient"
```

---

### task: implement-test-client-on-shoptet-order-client

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs:12`

- [ ] **Step 1: Add `IShoptetOrderTestClient` to the class's implemented-interface list**

At `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs` line 12, change:

```csharp
public class ShoptetOrderClient : IEshopOrderClient, IShoptetExpeditionOrderSource
```

to:

```csharp
public class ShoptetOrderClient : IEshopOrderClient, IShoptetOrderTestClient, IShoptetExpeditionOrderSource
```

No other change to this file — all 11 method bodies stay exactly where they are (the 4 relocated methods' implementations, e.g. `GetRecentOrdersAsync` at line 33, `ListByExternalCodePrefixAsync` at line 51, `CreateOrderAsync` at line 107, `DeleteOrderAsync` at line 189, already satisfy `IShoptetOrderTestClient`'s signatures verbatim since the interface was copied from the same method signatures).

- [ ] **Step 2: Build the adapter project to verify `ShoptetOrderClient` satisfies both interfaces**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded.` If it fails with "does not implement interface member 'IShoptetOrderTestClient.X'", compare the failing signature character-for-character against `IShoptetOrderTestClient.cs` from the previous task — the two most common causes are a mismatched default-parameter value or a `CancellationToken ct = default` vs. no-default mismatch.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs
git commit -m "feat(shoptet-orders): implement IShoptetOrderTestClient on ShoptetOrderClient"
```

---

### task: register-test-client-in-di

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs:51-52`

- [ ] **Step 1: Add the new DI registration**

At `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`, the existing registrations at lines 51-52 read:

```csharp
        services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
        services.AddTransient<IShoptetExpeditionOrderSource>(sp => sp.GetRequiredService<ShoptetOrderClient>());
```

Insert a new line immediately after the `IEshopOrderClient` registration (before the `IShoptetExpeditionOrderSource` line), so the block reads:

```csharp
        services.AddTransient<IEshopOrderClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
        services.AddTransient<IShoptetOrderTestClient>(sp => sp.GetRequiredService<ShoptetOrderClient>());
        services.AddTransient<IShoptetExpeditionOrderSource>(sp => sp.GetRequiredService<ShoptetOrderClient>());
```

No `using` addition is needed — `IShoptetOrderTestClient` is in namespace `Anela.Heblo.Adapters.ShoptetApi.Orders`, which this file already imports via `using Anela.Heblo.Adapters.ShoptetApi.Orders;` at line 7.

- [ ] **Step 2: Build the adapter project**

Run:
```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs
git commit -m "feat(shoptet-orders): register IShoptetOrderTestClient in DI"
```

---

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

### task: full-solution-verification

**Files:** none (verification only).

- [ ] **Step 1: Full backend build**

Run:
```bash
cd backend && dotnet build
```
Expected: `Build succeeded.` with 0 errors across every project in the backend (Application, the ShoptetApi adapter, the API host, and every test project).

- [ ] **Step 2: Format check**

Run:
```bash
cd backend && dotnet format --verify-no-changes
```
Expected: no formatting violations reported. If it reports violations in files this plan touched, run `dotnet format` (no flag) and re-stage/re-commit just those files with message `style(shoptet-orders): apply dotnet format`.

- [ ] **Step 3: Run the full Application unit test suite (FR-5 regression check)**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: all tests pass, including `BlockOrderProcessingHandlerTests`, `ScanPackingOrderHandlerTests`, `ScanPackingOrderHandlerPackagePersistenceTests`, `CompletePackingOrderHandlerTests`, `CompleteDeliveredOrdersJobTests`, `PrintExpeditionOrderHandlerTests`, and `ModuleBoundariesTests` — none of these mock/stub `CreateOrderAsync`, `DeleteOrderAsync`, `GetRecentOrdersAsync`, or `ListByExternalCodePrefixAsync` today (confirmed via repo-wide grep during planning), so the narrowed `IEshopOrderClient` requires no test changes here. `ModuleBoundariesTests` specifically re-verifies its `IEshopOrderClient` allowlist entries for `ScanPackingOrderHandler` and `CompletePackingOrderHandler` still resolve correctly against the now-7-method interface (the allowlist is per-type, not per-member, so no allowlist edit is needed — this step just proves that holds).

- [ ] **Step 4: Grep the whole backend for any stray reference that still resolves the 4 relocated methods through `IEshopOrderClient`**

Run:
```bash
cd backend && grep -rn "IEshopOrderClient" --include="*.cs" . | grep -v "IShoptetOrderTestClient"
```
Read through the output manually: every remaining `IEshopOrderClient` reference should be either its own declaration/implementation, a DI registration, or a consumer that only needs the 7 surviving methods. There should be no line where `IEshopOrderClient` is injected solely to call one of the 4 relocated methods (FR-4's acceptance criterion).

- [ ] **Step 5: Run the full adapter integration test project one more time as a final compile/DI-resolution gate**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj
```
Expected: all tests pass or self-skip exactly as they did before this refactor (guarded by the same `SHOPTET_BLOCK_ORDER`/`SHOPTET_HYDRATE` env vars and `ShoptetTestGuard.Assert` as before) — no new failures attributable to this change.

- [ ] **Step 6: No commit needed for this task** — it is verification-only. If any step above required a fix, that fix was already committed under its own task; do not create an empty commit here.

---

## Self-Review

**Spec coverage:**
- FR-1 (new `IShoptetOrderTestClient` in the adapter project, 4 methods verbatim) → `create-shoptet-order-test-client-interface`.
- FR-2 (shrink `IEshopOrderClient` to 7 methods, regression grep) → `shrink-ieshoporderclient-interface` (Steps 1-3).
- FR-3 (`ShoptetOrderClient` implements both interfaces, DI registers both) → `implement-test-client-on-shoptet-order-client` + `register-test-client-in-di`.
- FR-4 (update the 3 integration test files to the new interface) → `update-block-order-processing-integration-tests`, `update-shoptet-test-environment-hydration-tests`, `update-picking-list-integration-tests`.
- FR-5 (no Application-layer mocking burden for removed methods) → confirmed by planning-time grep (no existing setups reference the 4 methods) and re-verified in `full-solution-verification` Step 3.
- NFR-1/NFR-2 (no perf/security impact) → satisfied by construction (no method bodies change).
- NFR-3 (zero behavior change) → every task explicitly preserves method bodies; only interface membership and call-site types change.
- "Out of Scope" items (no HTTP behavior change, no change to the 7 surviving methods, no DTO relocation, no further ISP cleanup) → respected; no task touches HTTP logic, the 7 surviving method bodies, or `CreateEshopOrderRequest`/`EshopOrderSummary`.

**Placeholder scan:** No "TBD"/"add appropriate handling"/"similar to Task N" phrasing anywhere above; every step shows the literal before/after code or the literal command and expected output.

**Type consistency:** `IShoptetOrderTestClient`'s 4 signatures (Step 1 of task 1) are reused character-for-character in `ShoptetOrderClient`'s existing method bodies (task 3, no change needed there) and in every call site rewritten in tasks 5-7 (`_testClient.CreateOrderAsync(...)`, `_testClient.DeleteOrderAsync(...)`, `_testClient.GetRecentOrdersAsync(...)`, `_testClient.ListByExternalCodePrefixAsync(...)` / `_testOrderClient.GetRecentOrdersAsync(...)` in the picking-list file, which names its test-client field `_testOrderClient` to mirror its existing `_orderClient` naming convention — this is intentionally the only file that doesn't use the literal name `_testClient`, called out here so it isn't mistaken for an inconsistency).

**Correction to the arch-review during planning:** The arch-review's file-by-file table claimed `ShoptetTestEnvironmentHydrationTests.cs` and `PickingListIntegrationTests.cs` are pure single-interface consumers that can retype their existing field entirely to `IShoptetOrderTestClient`. Re-reading the actual source during this planning pass found both files also call `UpdateStatusAsync` (which stays on `IEshopOrderClient`) — `ShoptetTestEnvironmentHydrationTests.HydrateTestEnvironment`/`PurgeTestOrders` at lines 245/252/291, and `PickingListIntegrationTests.PrintPickingList_ProducesPdfs_ForRecentOrders` at lines 80/117. Both are therefore **mixed-usage** consumers like `BlockOrderProcessingIntegrationTests`, and this plan's tasks 6 and 7 add a second field rather than retyping the existing one, to avoid breaking those `UpdateStatusAsync` call sites.
