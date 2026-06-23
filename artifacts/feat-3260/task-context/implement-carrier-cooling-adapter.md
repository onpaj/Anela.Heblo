### task: implement-carrier-cooling-adapter

- [ ] Create `CarrierCooling/Infrastructure/` directory and adapter file
- [ ] Add `using` and DI registration to `CarrierCoolingModule.cs`
- [ ] Create unit test file
- [ ] Run tests to confirm green

**File to create: `src/Anela.Heblo.Application/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapter.cs`**

Note: `CarrierCooling/Infrastructure/` directory does not exist yet — create it.

```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.CarrierCooling.Infrastructure;

internal sealed class CarrierCoolingPackingCarrierCoolingAdapter : IPackingCarrierCoolingSource
{
    private readonly ICarrierCoolingRepository _repository;

    public CarrierCoolingPackingCarrierCoolingAdapter(ICarrierCoolingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(CancellationToken ct = default)
    {
        var settings = await _repository.GetAllAsync(ct);
        return settings.Select(s => new PackingCarrierCoolingSetting
        {
            CarrierName = s.Carrier.ToString(),
            DeliveryHandlingName = s.DeliveryHandling.ToString(),
            Cooling = s.Cooling,
        }).ToList();
    }
}
```

**Modify `CarrierCoolingModule.cs`** — add `using` at top and registration:
```csharp
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
```
```csharp
// Cross-module contract: CarrierCooling implements ShoptetOrders' IPackingCarrierCoolingSource via adapter.
// DI registration is owned by the provider (CarrierCooling), not the consumer (ShoptetOrders).
services.AddTransient<IPackingCarrierCoolingSource, CarrierCoolingPackingCarrierCoolingAdapter>();
```

**New test file: `test/Anela.Heblo.Tests/Features/CarrierCooling/Infrastructure/CarrierCoolingPackingCarrierCoolingAdapterTests.cs`**
```csharp
using Anela.Heblo.Application.Features.CarrierCooling.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders.Contracts;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.CarrierCooling.Infrastructure;

public class CarrierCoolingPackingCarrierCoolingAdapterTests
{
    [Fact]
    public async Task GetAllAsync_MapsCarrierNameAsEnumString()
    {
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CarrierCoolingSetting(Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "test") });
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().ContainSingle();
        result[0].CarrierName.Should().Be("PPL");
        result[0].DeliveryHandlingName.Should().Be("NaRuky");
        result[0].Cooling.Should().Be(Cooling.L1);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyListWhenRepositoryEmpty()
    {
        var repo = new Mock<ICarrierCoolingRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CarrierCoolingSetting>());
        var sut = new CarrierCoolingPackingCarrierCoolingAdapter(repo.Object);

        var result = await sut.GetAllAsync();

        result.Should().BeEmpty();
    }
}
```

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "CarrierCoolingPackingCarrierCoolingAdapterTests"
```

---

