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
