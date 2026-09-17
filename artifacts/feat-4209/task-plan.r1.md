# Decouple Packaging and ExpeditionList from ShoptetOrders.IEshopOrderClient Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the three direct `IEshopOrderClient` injections in `Packaging` and `ExpeditionList` handlers with two narrow, consumer-owned interfaces (`IPackedOrderStatusUpdater`, `IOrderStatusReader`), each implemented by a `ShoptetOrders`-side adapter, following the existing `IShipmentDeliveryChecker` / `ILeafletKnowledgeSource` consumer-owns-contract pattern.

**Architecture:** Each consumer module (`Packaging`, `ExpeditionList`) declares a `Contracts/` interface exposing only the one method it needs. `ShoptetOrders` implements each interface via a thin `Infrastructure/` adapter that delegates 1:1 to the existing, unchanged `IEshopOrderClient`, and registers the DI binding itself (provider-owns-registration). `ModuleBoundariesTests.cs` is updated to stop allowlisting the old `IEshopOrderClient` coupling for Packaging and to add a new, previously-missing enforcement rule for `ExpeditionList -> ShoptetOrders`.

**Tech Stack:** .NET 8, MediatR, Moq + xUnit + FluentAssertions for tests, Microsoft.Extensions.DependencyInjection.

---

### task: packaging-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Application.Features.Packaging.Contracts;

public interface IPackedOrderStatusUpdater
{
    /// <summary>
    /// Transitions the order to the configured "packed" state (Shoptet "Zabaleno", id 52 by default).
    /// Mirrors IEshopOrderClient.MarkAsPackedAsync; Packaging depends only on this narrower surface
    /// via the consumer-owns-contract pattern (see IShipmentDeliveryChecker / ILeafletKnowledgeSource).
    /// </summary>
    Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded (new file adds a compiling, currently-unused interface — no other code references it yet).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/Contracts/IPackedOrderStatusUpdater.cs
git commit -m "feat(packaging): add IPackedOrderStatusUpdater consumer-owned contract"
```

---

### task: packaging-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapter.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapterTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ShoptetOrders.Infrastructure;

public class ShoptetOrdersPackedOrderStatusUpdaterAdapterTests
{
    [Fact]
    public async Task MarkAsPackedAsync_DelegatesToEshopOrderClient_WithSameArguments()
    {
        var orderCode = "0001234";
        using var cts = new CancellationTokenSource();
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync(orderCode, cts.Token))
            .Returns(Task.CompletedTask);
        var sut = new ShoptetOrdersPackedOrderStatusUpdaterAdapter(eshopOrderClient.Object);

        await sut.MarkAsPackedAsync(orderCode, cts.Token);

        eshopOrderClient.Verify(c => c.MarkAsPackedAsync(orderCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task MarkAsPackedAsync_PropagatesException_WhenEshopOrderClientThrows()
    {
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.MarkAsPackedAsync("0005678", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet down"));
        var sut = new ShoptetOrdersPackedOrderStatusUpdaterAdapter(eshopOrderClient.Object);

        var act = () => sut.MarkAsPackedAsync("0005678");

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrdersPackedOrderStatusUpdaterAdapterTests"`
Expected: FAIL — build error, `ShoptetOrdersPackedOrderStatusUpdaterAdapter` does not exist yet.

- [ ] **Step 3: Write the adapter**

```csharp
using Anela.Heblo.Application.Features.Packaging.Contracts;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

internal sealed class ShoptetOrdersPackedOrderStatusUpdaterAdapter : IPackedOrderStatusUpdater
{
    private readonly IEshopOrderClient _eshopOrderClient;

    public ShoptetOrdersPackedOrderStatusUpdaterAdapter(IEshopOrderClient eshopOrderClient)
    {
        _eshopOrderClient = eshopOrderClient;
    }

    public Task MarkAsPackedAsync(string orderCode, CancellationToken ct = default)
        => _eshopOrderClient.MarkAsPackedAsync(orderCode, ct);
}
```

- [ ] **Step 4: Register the adapter in ShoptetOrdersModule.cs**

Modify `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` (currently 17 lines) to:

```csharp
using Anela.Heblo.Application.Features.Packaging.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ShoptetOrders;

public static class ShoptetOrdersModule
{
    public static IServiceCollection AddShoptetOrdersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ShoptetOrdersSettings>(
            configuration.GetSection(ShoptetOrdersSettings.ConfigurationKey));

        // Cross-module contract: ShoptetOrders implements Packaging's IPackedOrderStatusUpdater via
        // adapter. DI registration is owned by the provider (ShoptetOrders), not the consumer
        // (Packaging). Lifetime mirrors IEshopOrderClient's own Transient registration
        // (ShoptetApiAdapterServiceCollectionExtensions).
        services.AddTransient<IPackedOrderStatusUpdater, ShoptetOrdersPackedOrderStatusUpdaterAdapter>();

        return services;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrdersPackedOrderStatusUpdaterAdapterTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersPackedOrderStatusUpdaterAdapterTests.cs \
        backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs
git commit -m "feat(shoptet-orders): implement IPackedOrderStatusUpdater adapter and register it"
```

---

### task: packaging-handlers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs`

- [ ] **Step 1: Update the failing (soon-to-fail) tests first — ScanPackingOrderHandlerTests.cs**

In `backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs`:

- Replace the `using Anela.Heblo.Application.Features.ShoptetOrders;` line (line 4) with:
  `using Anela.Heblo.Application.Features.Packaging.Contracts;`
- Replace line 19:
  ```csharp
  private readonly Mock<IEshopOrderClient> _eshopOrderClient = new();
  ```
  with:
  ```csharp
  private readonly Mock<IPackedOrderStatusUpdater> _packedOrderStatusUpdater = new();
  ```
- Replace line 32 (`_eshopOrderClient.Object,` inside `CreateHandler()`) with:
  ```csharp
  _packedOrderStatusUpdater.Object,
  ```
- Replace every remaining `_eshopOrderClient.Verify(c => c.MarkAsPackedAsync(...` call (lines 141-143, 282-284, 315-317, 362-364, 440-442) with `_packedOrderStatusUpdater.Verify(c => c.MarkAsPackedAsync(...` — same arguments, same `Times.*`, only the mock field name changes.
- Replace the `_eshopOrderClient.Setup(c => c.MarkAsPackedAsync("0001234", It.IsAny<CancellationToken>())).ThrowsAsync(...)` call at lines 334-336 with `_packedOrderStatusUpdater.Setup(...)` — same body.

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — `ScanPackingOrderHandler`'s constructor still expects `IEshopOrderClient` as its 3rd parameter, not `IPackedOrderStatusUpdater`; compile error at `CreateHandler()`.

- [ ] **Step 2: Update ScanPackingOrderHandler.cs**

In `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`:

- Add `using Anela.Heblo.Application.Features.Packaging.Contracts;` to the using block (after line 1).
- Remove `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 3) — no longer needed once the field below is retyped.
- Replace line 17:
  ```csharp
  private readonly IEshopOrderClient _eshopOrderClient;
  ```
  with:
  ```csharp
  private readonly IPackedOrderStatusUpdater _packedOrderStatusUpdater;
  ```
- Replace the constructor parameter on line 27:
  ```csharp
  IEshopOrderClient eshopOrderClient,
  ```
  with:
  ```csharp
  IPackedOrderStatusUpdater packedOrderStatusUpdater,
  ```
- Replace line 36 (`_eshopOrderClient = eshopOrderClient;`) with:
  ```csharp
  _packedOrderStatusUpdater = packedOrderStatusUpdater;
  ```
- In `TryMarkAsPackedAsync` (line 164), replace:
  ```csharp
  await _eshopOrderClient.MarkAsPackedAsync(orderCode, ct);
  ```
  with:
  ```csharp
  await _packedOrderStatusUpdater.MarkAsPackedAsync(orderCode, ct);
  ```

- [ ] **Step 3: Update CompletePackingOrderHandler.cs**

In `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs`:

- Replace `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 2) with:
  `using Anela.Heblo.Application.Features.Packaging.Contracts;`
- Replace line 11:
  ```csharp
  private readonly IEshopOrderClient _eshopOrderClient;
  ```
  with:
  ```csharp
  private readonly IPackedOrderStatusUpdater _packedOrderStatusUpdater;
  ```
- Replace the constructor (lines 14-20):
  ```csharp
  public CompletePackingOrderHandler(
      IPackedOrderStatusUpdater packedOrderStatusUpdater,
      ILogger<CompletePackingOrderHandler> logger)
  {
      _packedOrderStatusUpdater = packedOrderStatusUpdater;
      _logger = logger;
  }
  ```
- In `Handle` (line 28), replace:
  ```csharp
  await _eshopOrderClient.MarkAsPackedAsync(request.OrderCode, cancellationToken);
  ```
  with:
  ```csharp
  await _packedOrderStatusUpdater.MarkAsPackedAsync(request.OrderCode, cancellationToken);
  ```

- [ ] **Step 4: Update CompletePackingOrderHandlerTests.cs**

Replace `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 2) with:
`using Anela.Heblo.Application.Features.Packaging.Contracts;`

Replace line 12:
```csharp
private readonly Mock<IPackedOrderStatusUpdater> _packedOrderStatusUpdater = new();
```

Replace line 14-15:
```csharp
private CompletePackingOrderHandler CreateHandler() =>
    new(_packedOrderStatusUpdater.Object, new Mock<ILogger<CompletePackingOrderHandler>>().Object);
```

Replace both `_eshopOrderClient.Verify(...)` / `_eshopOrderClient.Setup(...)` occurrences (lines 26-28, 34-36) with `_packedOrderStatusUpdater.Verify(...)` / `_packedOrderStatusUpdater.Setup(...)` — same arguments.

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ScanPackingOrderHandlerTests|FullyQualifiedName~CompletePackingOrderHandlerTests"`
Expected: PASS (all tests in both fixtures, including `Handle_MarkAsPackedFails_StillReturnsSuccessfulScanResponse` and `Handle_WhenMarkAsPackedThrows_ReturnsPackingCompletionFailed`, which prove the failure-propagation behavior survived the swap).

- [ ] **Step 6: Full solution build to catch any other reference**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs \
        backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/CompletePackingOrder/CompletePackingOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/ScanPackingOrderHandlerTests.cs \
        backend/test/Anela.Heblo.Tests/Application/Packaging/CompletePackingOrderHandlerTests.cs
git commit -m "refactor(packaging): consume IPackedOrderStatusUpdater instead of IEshopOrderClient"
```

---

### task: expeditionlist-contract

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IOrderStatusReader.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IOrderStatusReader
{
    /// <summary>
    /// Returns the order's current Shoptet status id.
    /// Mirrors IEshopOrderClient.GetOrderStatusIdAsync; may throw HttpRequestException with
    /// StatusCode == HttpStatusCode.NotFound when the order does not exist — callers depend on this
    /// exact exception shape (see PrintExpeditionOrderHandler's 404 handling). Implementations must
    /// let it propagate unmodified.
    /// </summary>
    Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded (new, currently-unused interface).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IOrderStatusReader.cs
git commit -m "feat(expedition-list): add IOrderStatusReader consumer-owned contract"
```

---

### task: expeditionlist-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapter.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapterTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ShoptetOrders.Infrastructure;

public class ShoptetOrdersOrderStatusReaderAdapterTests
{
    [Fact]
    public async Task GetOrderStatusIdAsync_DelegatesToEshopOrderClient_WithSameArgumentsAndResult()
    {
        var orderCode = "0001234";
        using var cts = new CancellationTokenSource();
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.GetOrderStatusIdAsync(orderCode, cts.Token))
            .ReturnsAsync(26);
        var sut = new ShoptetOrdersOrderStatusReaderAdapter(eshopOrderClient.Object);

        var result = await sut.GetOrderStatusIdAsync(orderCode, cts.Token);

        result.Should().Be(26);
        eshopOrderClient.Verify(c => c.GetOrderStatusIdAsync(orderCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetOrderStatusIdAsync_PropagatesNotFoundHttpRequestException_Unmodified()
    {
        var eshopOrderClient = new Mock<IEshopOrderClient>();
        eshopOrderClient
            .Setup(c => c.GetOrderStatusIdAsync("nope", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.NotFound));
        var sut = new ShoptetOrdersOrderStatusReaderAdapter(eshopOrderClient.Object);

        var act = () => sut.GetOrderStatusIdAsync("nope");

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrdersOrderStatusReaderAdapterTests"`
Expected: FAIL — build error, `ShoptetOrdersOrderStatusReaderAdapter` does not exist yet.

- [ ] **Step 3: Write the adapter**

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Infrastructure;

internal sealed class ShoptetOrdersOrderStatusReaderAdapter : IOrderStatusReader
{
    private readonly IEshopOrderClient _eshopOrderClient;

    public ShoptetOrdersOrderStatusReaderAdapter(IEshopOrderClient eshopOrderClient)
    {
        _eshopOrderClient = eshopOrderClient;
    }

    public Task<int> GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)
        => _eshopOrderClient.GetOrderStatusIdAsync(orderCode, ct);
}
```

- [ ] **Step 4: Register the adapter in ShoptetOrdersModule.cs**

Add to `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs` (the file this task's predecessor task already modified):

```csharp
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
```

to the using block, and after the `IPackedOrderStatusUpdater` registration line:

```csharp
        // Cross-module contract: ShoptetOrders implements ExpeditionList's IOrderStatusReader via
        // adapter. DI registration is owned by the provider (ShoptetOrders), not the consumer
        // (ExpeditionList). Lifetime mirrors IEshopOrderClient's own Transient registration
        // (ShoptetApiAdapterServiceCollectionExtensions).
        services.AddTransient<IOrderStatusReader, ShoptetOrdersOrderStatusReaderAdapter>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShoptetOrdersOrderStatusReaderAdapterTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/ShoptetOrders/Infrastructure/ShoptetOrdersOrderStatusReaderAdapterTests.cs \
        backend/src/Anela.Heblo.Application/Features/ShoptetOrders/ShoptetOrdersModule.cs
git commit -m "feat(shoptet-orders): implement IOrderStatusReader adapter and register it"
```

---

### task: expeditionlist-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`

- [ ] **Step 1: Update the tests first**

In `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs`:

- Remove `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 5) — no longer needed.
- Replace line 17:
  ```csharp
  private readonly Mock<IOrderStatusReader> _client = new();
  ```
  (the `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` import on line 2 already covers the new type — no import change needed there.)
- No other line needs to change: every `_client.Setup(c => c.GetOrderStatusIdAsync(...))` call (lines 32-33, 47-48, 68-69, 83-84, 96-97, 116-117, 128-129) keeps working unchanged because `IOrderStatusReader.GetOrderStatusIdAsync` has the identical signature to `IEshopOrderClient.GetOrderStatusIdAsync`.

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL — `PrintExpeditionOrderHandler`'s constructor still expects `IEshopOrderClient` as its 2nd parameter, not `IOrderStatusReader`; compile error at `CreateHandler()`.

- [ ] **Step 2: Update PrintExpeditionOrderHandler.cs**

In `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs`:

- Remove `using Anela.Heblo.Application.Features.ShoptetOrders;` (line 3) — the `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` on line 1 already covers the new type.
- Replace line 24:
  ```csharp
  private readonly IOrderStatusReader _orderStatusReader;
  ```
- Replace the constructor parameter on line 30:
  ```csharp
  IOrderStatusReader orderStatusReader,
  ```
- Replace line 35 (`_eshopOrderClient = eshopOrderClient;`) with:
  ```csharp
  _orderStatusReader = orderStatusReader;
  ```
- In `Handle` (line 47), replace:
  ```csharp
  currentStatusId = await _eshopOrderClient.GetOrderStatusIdAsync(request.OrderCode, cancellationToken);
  ```
  with:
  ```csharp
  currentStatusId = await _orderStatusReader.GetOrderStatusIdAsync(request.OrderCode, cancellationToken);
  ```

The existing `catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)`
block (lines 49-55) is untouched — it already works against whatever throws from the call above, and the
adapter's `expeditionlist-adapter` task test already proves the 404 shape survives the adapter unmodified.

- [ ] **Step 3: Run tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"`
Expected: PASS (all 6 test methods, including `Handle_OrderLookupReturns404_ReturnsNotFoundError` and `Handle_OrderLookupReturns500_PropagatesException`, proving the 404/500 handling paths are unaffected by the swap).

- [ ] **Step 4: Full solution build to catch any other reference**

Run: `cd backend && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs
git commit -m "refactor(expedition-list): consume IOrderStatusReader instead of IEshopOrderClient"
```

---

### task: module-boundary-enforcement

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Remove the now-stale IEshopOrderClient allowlist entries for Packaging**

In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, inside `PackagingShoptetOrdersAllowlist`
(around line 297), delete these two lines:

```csharp
        "Anela.Heblo.Application.Features.Packaging.UseCases.ScanPackingOrder.ScanPackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",
```

and

```csharp
        "Anela.Heblo.Application.Features.Packaging.UseCases.CompletePackingOrder.CompletePackingOrderHandler -> Anela.Heblo.Application.Features.ShoptetOrders.IEshopOrderClient",
```

Leave every `IPackingOrderClient`, `PackingOrder`, and `PackingOrderItem` entry in that allowlist untouched —
that coupling is explicitly out of scope for this issue.

- [ ] **Step 2: Update the allowlist's doc comment**

Replace the comment block directly above `PackagingShoptetOrdersAllowlist` (currently):

```csharp
    // Allowlist for Packaging -> ShoptetOrders. The Packaging module legitimately consumes
    // the IPackingOrderClient / IEshopOrderClient contracts (and their DTOs) defined in
    // Anela.Heblo.Application.Features.ShoptetOrders. Everything else — particularly
    // ShoptetOrdersSettings, PackingStateId, and PackedStateId — must not be referenced
    // from Packaging. This rule pins the 2026-06-05 decoupling in place.
```

with:

```csharp
    // Allowlist for Packaging -> ShoptetOrders. The Packaging module legitimately consumes
    // the IPackingOrderClient contract (and its PackingOrder/PackingOrderItem DTOs) defined in
    // Anela.Heblo.Application.Features.ShoptetOrders. Everything else — particularly
    // ShoptetOrdersSettings, PackingStateId, PackedStateId, and (as of the IEshopOrderClient
    // decoupling) IEshopOrderClient itself — must not be referenced from Packaging. Packaging now
    // consumes MarkAsPackedAsync via its own IPackedOrderStatusUpdater contract, implemented by
    // ShoptetOrders' ShoptetOrdersPackedOrderStatusUpdaterAdapter.
```

- [ ] **Step 3: Add the new ExpeditionList -> ShoptetOrders rule**

Add a new private field, placed near `ExpeditionListLogisticsAllowlist` (around line 228-231) for locality:

```csharp
    // Allowlist for ExpeditionList -> ShoptetOrders. Empty — PrintExpeditionOrderHandler now consumes
    // the ExpeditionList-owned IOrderStatusReader contract; the ShoptetOrders adapter
    // (ShoptetOrdersOrderStatusReaderAdapter) lives in ShoptetOrders.Infrastructure and implements it
    // there, so no ExpeditionList type needs to reference ShoptetOrders directly.
    private static readonly HashSet<string> ExpeditionListShoptetOrdersAllowlist = new(StringComparer.Ordinal);
```

Add a new entry to the `Rules()` `TheoryData<ModuleBoundaryRule>` (in the `public static TheoryData<ModuleBoundaryRule> Rules()` method, alongside the existing `"ExpeditionList -> Logistics"` and `"Packaging -> ShoptetOrders"` entries — insert it directly after the `"ExpeditionList -> Logistics"` entry for locality):

```csharp
        new ModuleBoundaryRule(
            Name: "ExpeditionList -> ShoptetOrders",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.ExpeditionList",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Application.Features.ShoptetOrders",
            },
            Allowlist: ExpeditionListShoptetOrdersAllowlist),
```

- [ ] **Step 4: Run the module boundary tests to verify all rules pass, including the new one**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS — every `[Theory]` case, including the new `"ExpeditionList -> ShoptetOrders"` rule (proving
`PrintExpeditionOrderHandler` no longer references `Anela.Heblo.Application.Features.ShoptetOrders` at all)
and the edited `"Packaging -> ShoptetOrders"` rule (proving `ScanPackingOrderHandler` and
`CompletePackingOrderHandler` no longer reference `IEshopOrderClient`, since that entry is now gone from
the allowlist and any lingering reference would fail the test).

- [ ] **Step 5: Confirm no stray IEshopOrderClient references remain outside ShoptetOrders**

Run: `cd backend && grep -rn "IEshopOrderClient" src/ --include=*.cs | grep -v "^src/Anela.Heblo.Application/Features/ShoptetOrders/" | grep -v "^src/Adapters/Anela.Heblo.Adapters.ShoptetApi/"`
Expected: no output (empty) — confirms FR-7's "no remaining direct ShoptetOrders coupling" acceptance
criterion at the source level, independent of the reflection-based test.

- [ ] **Step 6: Full solution build and full test suite**

Run: `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build succeeded; all tests pass (no regressions anywhere else in the suite).

- [ ] **Step 7: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting violations. If it reports changes, run `dotnet format` and re-run step 6.

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): enforce ExpeditionList/Packaging decoupling from IEshopOrderClient"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (`IPackedOrderStatusUpdater` contract) → `packaging-contract` task.
- FR-2 (ShoptetOrders adapter for it) → `packaging-adapter` task.
- FR-3 (Packaging handlers switched over) → `packaging-handlers` task.
- FR-4 (`IOrderStatusReader` contract) → `expeditionlist-contract` task.
- FR-5 (ShoptetOrders adapter for it) → `expeditionlist-adapter` task.
- FR-6 (ExpeditionList handler switched over) → `expeditionlist-handler` task.
- FR-7 (verify no remaining direct coupling) → `module-boundary-enforcement` task (grep check + new/edited
  reflection-based architecture test).
- NFR-1/NFR-2/NFR-3 (no perf/security/behavior change) → satisfied by every adapter being a bare 1:1
  delegation with no added logic, verified by the "propagates exception unmodified" tests in the two
  adapter tasks and the retained 404/500 handler tests.
- arch-review.r1.md's Specification Amendments (404 pass-through test, `ModuleBoundariesTests.cs` updates,
  new `ExpeditionList -> ShoptetOrders` rule) → covered by `expeditionlist-adapter`'s
  `GetOrderStatusIdAsync_PropagatesNotFoundHttpRequestException_Unmodified` test and the
  `module-boundary-enforcement` task.

**Placeholder scan:** No "TBD"/"TODO"/"add appropriate error handling" phrases used; every step shows
complete code or an exact command with expected output.

**Type consistency:** `IPackedOrderStatusUpdater.MarkAsPackedAsync(string orderCode, CancellationToken ct = default)`
and `IOrderStatusReader.GetOrderStatusIdAsync(string orderCode, CancellationToken ct = default)` are used
identically across every task that references them (contract definition, adapter implementation, adapter
test, handler, handler test).
