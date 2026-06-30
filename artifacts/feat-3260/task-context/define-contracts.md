### task: define-contracts

- [ ] Create `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs`
- [ ] Create `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs`
- [ ] Build Application project to confirm no errors

**File 1: `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingProductSource.cs`**
```csharp
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingProductSource
{
    Task<IReadOnlyDictionary<string, PackingProductInfo>> GetByCodesAsync(
        IEnumerable<string> productCodes, CancellationToken ct = default);
}

public class PackingProductInfo
{
    public Cooling Cooling { get; init; }
    public int? WeightGrams { get; init; }
    public string? ImageUrl { get; init; }
}
```

**File 2: `src/Anela.Heblo.Application/Features/ShoptetOrders/Contracts/IPackingCarrierCoolingSource.cs`**
```csharp
using Anela.Heblo.Domain.Shared;

namespace Anela.Heblo.Application.Features.ShoptetOrders.Contracts;

public interface IPackingCarrierCoolingSource
{
    Task<IReadOnlyList<PackingCarrierCoolingSetting>> GetAllAsync(CancellationToken ct = default);
}

public class PackingCarrierCoolingSetting
{
    public string CarrierName { get; init; } = string.Empty;
    public string DeliveryHandlingName { get; init; } = string.Empty;
    public Cooling Cooling { get; init; }
}
```

Build check:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

---

