### task: add-shipment-delivery-checker-contract-and-adapter

Add the new consumer-owned contract and the provider-owned adapter that implements it, with a unit test for the adapter's delegation behavior.

**Step 1 — write the failing adapter test first.**

Create `backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs` (new file, new folder — mirrors `backend/test/Anela.Heblo.Tests/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapterTests.cs`):

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.ShipmentLabels.Infrastructure;

public class ShipmentLabelsShipmentDeliveryCheckerAdapterTests
{
    [Fact]
    public async Task HasDeliveredShipmentAsync_DelegatesToShipmentClient_WithSameArgumentsAndResult()
    {
        var orderCode = "ORD-123";
        using var cts = new CancellationTokenSource();
        var shipmentClient = new Mock<IShipmentClient>();
        shipmentClient
            .Setup(c => c.HasDeliveredShipmentAsync(orderCode, cts.Token))
            .ReturnsAsync(true);
        var sut = new ShipmentLabelsShipmentDeliveryCheckerAdapter(shipmentClient.Object);

        var result = await sut.HasDeliveredShipmentAsync(orderCode, cts.Token);

        result.Should().BeTrue();
        shipmentClient.Verify(c => c.HasDeliveredShipmentAsync(orderCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task HasDeliveredShipmentAsync_ReturnsFalse_WhenShipmentClientReturnsFalse()
    {
        var shipmentClient = new Mock<IShipmentClient>();
        shipmentClient
            .Setup(c => c.HasDeliveredShipmentAsync("ORD-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = new ShipmentLabelsShipmentDeliveryCheckerAdapter(shipmentClient.Object);

        var result = await sut.HasDeliveredShipmentAsync("ORD-456");

        result.Should().BeFalse();
    }
}
```

Run it and confirm it fails to compile (neither `IShipmentDeliveryChecker` nor `ShipmentLabelsShipmentDeliveryCheckerAdapter` exist yet):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsShipmentDeliveryCheckerAdapterTests"
```

Expect a build error referencing the two missing types.

**Step 2 — create the consumer-owned contract.**

Create `backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs` (new file):

```csharp
namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IShipmentDeliveryChecker
{
    Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default);
}
```

**Step 3 — create the provider-owned adapter.**

Create `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs` (new file, new `Infrastructure/` folder under `ShipmentLabels` — mirrors `CarrierCooling/Infrastructure/` and `Catalog/Infrastructure/`):

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

namespace Anela.Heblo.Application.Features.ShipmentLabels.Infrastructure;

internal sealed class ShipmentLabelsShipmentDeliveryCheckerAdapter : IShipmentDeliveryChecker
{
    private readonly IShipmentClient _shipmentClient;

    public ShipmentLabelsShipmentDeliveryCheckerAdapter(IShipmentClient shipmentClient)
    {
        _shipmentClient = shipmentClient;
    }

    public Task<bool> HasDeliveredShipmentAsync(string orderCode, CancellationToken ct = default)
        => _shipmentClient.HasDeliveredShipmentAsync(orderCode, ct);
}
```

**Step 4 — run the test again and confirm it passes.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentLabelsShipmentDeliveryCheckerAdapterTests"
```

Both `[Fact]`s must pass.

**Step 5 — build the whole solution to confirm nothing else broke.**

```bash
dotnet build Anela.Heblo.sln
```

**Step 6 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IShipmentDeliveryChecker.cs \
        backend/src/Anela.Heblo.Application/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/ShipmentLabels/Infrastructure/ShipmentLabelsShipmentDeliveryCheckerAdapterTests.cs
git commit -m "Add IShipmentDeliveryChecker contract and ShipmentLabels adapter"
```

---

