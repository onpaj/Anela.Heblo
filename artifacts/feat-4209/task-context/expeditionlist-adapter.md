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
