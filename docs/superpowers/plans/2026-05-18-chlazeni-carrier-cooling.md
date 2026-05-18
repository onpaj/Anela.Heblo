# Chlazení — Per-Carrier Cooling Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Chlazení" management page under Zákaznické that shows a 6-row matrix (3 carriers × na ruky/box) with 3-radio cooling selectors (None/L1/L2), saving each change immediately with no Save button.

**Architecture:** Domain entities + repository in Logistics feature folder; Shoptet adapter provides `IShippingMethodCatalog` (classifies shipping names into carrier+handling pairs); Application layer has two use cases (GET matrix, PUT upsert one cell); frontend hook with optimistic update.

**Tech Stack:** .NET 8, EF Core + PostgreSQL, MediatR, FluentValidation, React, React Query, Tailwind CSS.

---

## File Map

### New files
| Path | Purpose |
|------|---------|
| `backend/src/Anela.Heblo.Domain/Features/Logistics/DeliveryHandling.cs` | New enum: NaRuky=1, Box=2 |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs` | Entity with composite key (Carrier, DeliveryHandling) |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/ICarrierCoolingRepository.cs` | Repository abstraction |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/IShippingMethodCatalog.cs` | Catalog abstraction |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs` | Static class holding the extracted ShippingList (was private in ExpeditionListSource) |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs` | Implements IShippingMethodCatalog, classifies names to DeliveryHandling |
| `backend/src/Anela.Heblo.Persistence/CarrierCooling/CarrierCoolingSettingConfiguration.cs` | EF Core config, enums as strings, composite key |
| `backend/src/Anela.Heblo.Persistence/CarrierCooling/CarrierCoolingRepository.cs` | Find-or-insert upsert |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierCoolingRowDto.cs` | DTO: DeliveryHandling + Cooling |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierGroupDto.cs` | DTO: Carrier + List<Rows> |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixRequest.cs` | MediatR request |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixResponse.cs` | List<CarrierGroupDto> Groups |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs` | Left-join catalog options with stored settings |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs` | MediatR request: Carrier, DeliveryHandling, Cooling, ModifiedBy |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingResponse.cs` | Extends BaseResponse |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingValidator.cs` | FluentValidation: valid enums + combo in catalog |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs` | Calls UpsertAsync |
| `backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs` | DI: scoped repository, scoped validator |
| `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` | GET /api/carrier-cooling, PUT /api/carrier-cooling |
| `frontend/src/api/hooks/useCarrierCooling.ts` | useCarrierCoolingMatrix (query), useSetCarrierCooling (mutation with optimistic update) |
| `frontend/src/pages/customer/CoolingPage.tsx` | Container page |
| `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx` | Matrix component with radio selectors |
| `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShippingMethodCatalogTests.cs` | Unit tests for classification logic |
| `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/GetCarrierCoolingMatrixHandlerTests.cs` | Unit tests for the GET handler |
| `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs` | Unit + validator tests for SET handler |
| `backend/test/Anela.Heblo.Tests/Controllers/CarrierCoolingControllerTests.cs` | Integration tests via HebloWebApplicationFactory |

### Modified files
| Path | Change |
|------|--------|
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` | Replace private `ShippingList`/`ShippingByGuid` fields with references to `ShippingMethodRegistry` |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs` | Add `services.AddSingleton<IShippingMethodCatalog, ShippingMethodCatalog>()` |
| `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` | Add `DbSet<CarrierCoolingSetting> CarrierCoolingSettings` |
| `backend/src/Anela.Heblo.Application/ApplicationModule.cs` | Add `services.AddCarrierCoolingModule()` |
| `frontend/src/components/Layout/Sidebar.tsx` | Add `{ id: "chlazeni", name: "Chlazení", href: "/customer/cooling" }` to zakaznicke items |
| `frontend/src/App.tsx` | Import `CoolingPage`, add route `/customer/cooling` |

---

## Task 1: Domain Types

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/DeliveryHandling.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/ICarrierCoolingRepository.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/IShippingMethodCatalog.cs`

- [ ] **Step 1.1: Create the DeliveryHandling enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Logistics/DeliveryHandling.cs
namespace Anela.Heblo.Domain.Features.Logistics;

public enum DeliveryHandling
{
    NaRuky = 1,
    Box = 2,
}
```

- [ ] **Step 1.2: Create the CarrierCoolingSetting entity**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Logistics;

public class CarrierCoolingSetting
{
    public Carriers Carrier { get; private set; }
    public DeliveryHandling DeliveryHandling { get; private set; }
    public Cooling Cooling { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    public string ModifiedBy { get; private set; } = null!;

    private CarrierCoolingSetting() { }

    public CarrierCoolingSetting(Carriers carrier, DeliveryHandling deliveryHandling, Cooling cooling, string modifiedBy)
    {
        Carrier = carrier;
        DeliveryHandling = deliveryHandling;
        Cooling = cooling;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }

    public void UpdateCooling(Cooling cooling, string modifiedBy)
    {
        Cooling = cooling;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 1.3: Create ICarrierCoolingRepository**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Logistics/ICarrierCoolingRepository.cs
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Logistics;

public interface ICarrierCoolingRepository
{
    Task<IReadOnlyList<CarrierCoolingSetting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(Carriers carrier, DeliveryHandling deliveryHandling, Cooling cooling, string modifiedBy, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1.4: Create IShippingMethodCatalog**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Logistics/IShippingMethodCatalog.cs
namespace Anela.Heblo.Domain.Features.Logistics;

public interface IShippingMethodCatalog
{
    IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions();
}
```

- [ ] **Step 1.5: Build and verify compilation**

Run from the repo root:
```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 1.6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Logistics/DeliveryHandling.cs \
        backend/src/Anela.Heblo.Domain/Features/Logistics/CarrierCoolingSetting.cs \
        backend/src/Anela.Heblo.Domain/Features/Logistics/ICarrierCoolingRepository.cs \
        backend/src/Anela.Heblo.Domain/Features/Logistics/IShippingMethodCatalog.cs
git commit -m "feat(carrier-cooling): add domain types — DeliveryHandling enum, CarrierCoolingSetting entity, repository and catalog interfaces"
```

---

## Task 2: Shoptet Adapter — ShippingMethodRegistry and ShippingMethodCatalog

**Context:** `ShoptetApiExpeditionListSource.cs` currently holds a private `ShippingList` (lines 24–44) and a derived `ShippingByGuid` dictionary. We extract `ShippingList` + `ShippingByGuid` into a new internal `ShippingMethodRegistry` class so both `ShoptetApiExpeditionListSource` and the new `ShippingMethodCatalog` share a single source of truth. The `ShippingMethod` type (with `Carrier`, `Name`, `Id`, `Guids`, `MaxOrders`, `MaxItems`) already exists in the adapter — do not redefine it.

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs`
- Create: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShippingMethodCatalogTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`

- [ ] **Step 2.1: Write the failing test for ShippingMethodCatalog**

```csharp
// backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShippingMethodCatalogTests.cs
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShippingMethodCatalogTests
{
    private readonly ShippingMethodCatalog _sut = new();

    [Fact]
    public void GetAvailableDeliveryOptions_ReturnsExactlySixDistinctPairs()
    {
        // Arrange + Act
        var result = _sut.GetAvailableDeliveryOptions();

        // Assert
        result.Should().HaveCount(6);
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.NaRuky));
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.Box));
        result.Should().Contain((Carriers.PPL, DeliveryHandling.NaRuky));
        result.Should().Contain((Carriers.PPL, DeliveryHandling.Box));
        result.Should().Contain((Carriers.GLS, DeliveryHandling.NaRuky));
        result.Should().Contain((Carriers.GLS, DeliveryHandling.Box));
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ExcludesOsobak()
    {
        var result = _sut.GetAvailableDeliveryOptions();
        result.Select(x => x.Carrier).Should().NotContain(Carriers.Osobak);
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ExcludesExportMethods()
    {
        // PPL_EXPORT and GLS_EXPORT exist in ShippingList but must not appear.
        // After exclusion the only PPL handling values are NaRuky and Box (not a third value).
        var result = _sut.GetAvailableDeliveryOptions();
        var pplHandlings = result.Where(x => x.Carrier == Carriers.PPL).Select(x => x.Handling).ToList();
        pplHandlings.Should().HaveCount(2);
        pplHandlings.Should().Contain(DeliveryHandling.NaRuky);
        pplHandlings.Should().Contain(DeliveryHandling.Box);
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ClassifiesDoRukyAsNaRuky()
    {
        var result = _sut.GetAvailableDeliveryOptions();
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.NaRuky));
    }

    [Fact]
    public void GetAvailableDeliveryOptions_ClassifiesZpointAndParcelshopAsBox()
    {
        var result = _sut.GetAvailableDeliveryOptions();
        result.Should().Contain((Carriers.Zasilkovna, DeliveryHandling.Box));
        result.Should().Contain((Carriers.PPL, DeliveryHandling.Box));
        result.Should().Contain((Carriers.GLS, DeliveryHandling.Box));
    }
}
```

- [ ] **Step 2.2: Run the test and confirm it fails**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/ \
  --filter "ShippingMethodCatalogTests" -v minimal
```
Expected: Fails because `ShippingMethodCatalog` does not exist yet.

- [ ] **Step 2.3: Create ShippingMethodRegistry**

Read `ShoptetApiExpeditionListSource.cs` lines 24–49 first to copy the exact list. Then create:

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs
namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

internal static class ShippingMethodRegistry
{
    // Copied verbatim from ShoptetApiExpeditionListSource (single source of truth)
    internal static readonly IReadOnlyList<ShippingMethod> ShippingList = new List<ShippingMethod>
    {
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY",              Id = 21,  Guids = ["f6610d4d-578d-11e9-beb1-002590dad85e"] },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT",               Id = 15,  Guids = ["7878c138-578d-11e9-beb1-002590dad85e", "389cea0b-40f1-11ea-beb1-002590dad85e"] },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK",           Id = 385, Guids = ["a6d9a6ce-0ede-11ee-b534-2a01067a25a9"] },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_CHLAZENY",     Id = 370, Guids = ["34d3f7d4-166f-11ee-b534-2a01067a25a9"] },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY",      Id = 373, Guids = ["bac58d34-166f-11ee-b534-2a01067a25a9"] },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_DO_RUKY_SK_CHLAZENY",  Id = 388, Guids = ["75123baa-1671-11ee-b534-2a01067a25a9"] },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_ZDARMA",        Id = 487, Guids = ["79b9ef95-5e46-11f0-ae6d-9237d29d7242"] },
        new() { Carrier = Carriers.Zasilkovna, Name = "ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA", Id = 481, Guids = ["db9bf927-5e44-11f0-ae6d-9237d29d7242"] },
        new() { Carrier = Carriers.PPL,        Name = "PPL_DO_RUKY",                     Id = 6,   Guids = ["2ec88ea7-3fb0-11e2-a723-705ab6a2ba75", "389ce5b4-40f1-11ea-beb1-002590dad85e"] },
        new() { Carrier = Carriers.PPL,        Name = "PPL_PARCELSHOP",                  Id = 80,  Guids = ["c4e6c287-9a85-11ea-beb1-002590dad85e", "83372e07-9a86-11ea-beb1-002590dad85e"] },
        new() { Carrier = Carriers.PPL,        Name = "PPL_EXPORT",                      Id = 86,  Guids = ["f17a0a12-0ebe-11eb-933a-002590dad85e", "2fd96b91-1508-11eb-933a-002590dad85e"] },
        new() { Carrier = Carriers.PPL,        Name = "PPL_DO_RUKY_CHLAZENY",            Id = 358, Guids = ["05ea842d-166a-11ee-b534-2a01067a25a9"] },
        new() { Carrier = Carriers.PPL,        Name = "PPL_PARCELSHOP_CHLAZENY",         Id = 361, Guids = ["0d10802f-166c-11ee-b534-2a01067a25a9"] },
        new() { Carrier = Carriers.PPL,        Name = "PPL_EXPORT_CHLAZENY",             Id = 379, Guids = ["de70f0e4-1670-11ee-b534-2a01067a25a9"] },
        new() { Carrier = Carriers.GLS,        Name = "GLS_DO_RUKY",                     Id = 97,  Guids = ["138ec07f-0119-11ec-a39f-002590dc5efc", "b7e787c5-011d-11ec-a39f-002590dc5efc"] },
        new() { Carrier = Carriers.GLS,        Name = "GLS_EXPORT",                      Id = 109, Guids = ["c06835e6-165e-11ec-a39f-002590dc5efc", "bbbe7223-4ea8-11ec-a39f-002590dc5efc"] },
        new() { Carrier = Carriers.GLS,        Name = "GLS_PARCELSHOP",                  Id = 489, Guids = ["49b79aec-0118-11ec-a39f-002590dc5efc"] },
        new() { Carrier = Carriers.Osobak,     Name = "OSOBAK",                          Id = 4,   Guids = ["8fdb2c89-3fae-11e2-a723-705ab6a2ba75", "389ce19e-40f1-11ea-beb1-002590dad85e"], MaxOrders = 1, MaxItems = int.MaxValue },
    };

    internal static readonly IReadOnlyDictionary<string, ShippingMethod> ByGuid =
        ShippingList
            .SelectMany(s => s.Guids.Select(g => (Guid: g, Method: s)))
            .ToDictionary(x => x.Guid, x => x.Method);
}
```

- [ ] **Step 2.4: Create ShippingMethodCatalog**

```csharp
// backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShippingMethodCatalog : IShippingMethodCatalog
{
    public IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions()
    {
        return ShippingMethodRegistry.ShippingList
            .Where(m => m.Carrier != Carriers.Osobak && !m.Name.Contains("_EXPORT"))
            .Select(m =>
            {
                DeliveryHandling? handling =
                    m.Name.Contains("DO_RUKY") ? DeliveryHandling.NaRuky :
                    m.Name.Contains("PARCELSHOP") || m.Name.Contains("ZPOINT") ? DeliveryHandling.Box :
                    (DeliveryHandling?)null;

                return (m.Carrier, Handling: handling);
            })
            .Where(x => x.Handling.HasValue)
            .Select(x => (x.Carrier, x.Handling!.Value))
            .Distinct()
            .ToList()
            .AsReadOnly();
    }
}
```

- [ ] **Step 2.5: Run the test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/ \
  --filter "ShippingMethodCatalogTests" -v minimal
```
Expected: 5 tests pass.

- [ ] **Step 2.6: Update ShoptetApiExpeditionListSource to use ShippingMethodRegistry**

In `ShoptetApiExpeditionListSource.cs`, delete the two private static fields (lines 24–49):
```csharp
// DELETE these two fields:
private static readonly IReadOnlyList<ShippingMethod> ShippingList = new List<ShippingMethod> { ... };
private static readonly Dictionary<string, ShippingMethod> ShippingByGuid = ...;
```

Then replace every reference to `ShippingList` with `ShippingMethodRegistry.ShippingList` and every reference to `ShippingByGuid` with `ShippingMethodRegistry.ByGuid` throughout the file body.

- [ ] **Step 2.7: Register IShippingMethodCatalog in ShoptetApiAdapterServiceCollectionExtensions**

In `ShoptetApiAdapterServiceCollectionExtensions.cs`, add before the `return services;` line:
```csharp
services.AddSingleton<IShippingMethodCatalog, ShippingMethodCatalog>();
```

Also add the using at the top of the file:
```csharp
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
```

- [ ] **Step 2.8: Build the adapter project**

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 2.9: Run all Shoptet adapter tests**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/ -v minimal
```
Expected: All tests pass (existing + new ShippingMethodCatalogTests).

- [ ] **Step 2.10: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShippingMethodCatalogTests.cs
git commit -m "feat(carrier-cooling): extract ShippingMethodRegistry, add ShippingMethodCatalog implementing IShippingMethodCatalog"
```

---

## Task 3: Persistence — EF Config, Repository, DbContext, Migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/CarrierCooling/CarrierCoolingSettingConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/CarrierCooling/CarrierCoolingRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 3.1: Create EF Core configuration**

```csharp
// backend/src/Anela.Heblo.Persistence/CarrierCooling/CarrierCoolingSettingConfiguration.cs
using Anela.Heblo.Domain.Features.Logistics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.CarrierCooling;

public class CarrierCoolingSettingConfiguration : IEntityTypeConfiguration<CarrierCoolingSetting>
{
    public void Configure(EntityTypeBuilder<CarrierCoolingSetting> builder)
    {
        builder.ToTable("CarrierCoolingSettings", "public");

        builder.HasKey(e => new { e.Carrier, e.DeliveryHandling });

        builder.Property(e => e.Carrier)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.DeliveryHandling)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(e => e.Cooling)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(10);

        builder.Property(e => e.ModifiedAt)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ModifiedBy)
            .IsRequired()
            .HasMaxLength(200);
    }
}
```

- [ ] **Step 3.2: Create CarrierCoolingRepository**

```csharp
// backend/src/Anela.Heblo.Persistence/CarrierCooling/CarrierCoolingRepository.cs
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.CarrierCooling;

public class CarrierCoolingRepository : ICarrierCoolingRepository
{
    private readonly ApplicationDbContext _context;

    public CarrierCoolingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<CarrierCoolingSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CarrierCoolingSettings.ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(Carriers carrier, DeliveryHandling deliveryHandling, Cooling cooling, string modifiedBy, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CarrierCoolingSettings
            .FirstOrDefaultAsync(s => s.Carrier == carrier && s.DeliveryHandling == deliveryHandling, cancellationToken);

        if (existing is null)
        {
            _context.CarrierCoolingSettings.Add(new CarrierCoolingSetting(carrier, deliveryHandling, cooling, modifiedBy));
        }
        else
        {
            existing.UpdateCooling(cooling, modifiedBy);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 3.3: Add DbSet to ApplicationDbContext**

Open `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`. Find the `// Packing Materials module` comment block (around line 84). Add the new DbSet immediately after the last Packing Materials line:

```csharp
// Carrier Cooling module
public DbSet<CarrierCoolingSetting> CarrierCoolingSettings { get; set; } = null!;
```

Also add the using at the top of the file:
```csharp
using Anela.Heblo.Domain.Features.Logistics;
```

- [ ] **Step 3.4: Build the Persistence project**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3.5: Create the EF migration**

Run from the repo root. Make sure the Default connection string in your local `secrets.json` points to `Heblo_TST`:

```bash
dotnet ef migrations add AddCarrierCoolingSettings \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Expected: A new migration file appears under `backend/src/Anela.Heblo.Persistence/Migrations/` with `CreateTable("CarrierCoolingSettings", ...)`.

- [ ] **Step 3.6: Apply the migration locally to Heblo_TST**

```bash
dotnet ef database update \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Expected: "Applying migration 'AddCarrierCoolingSettings'. Done." Verify in pgAdmin or psql that the `CarrierCoolingSettings` table exists with columns: `Carrier`, `DeliveryHandling`, `Cooling`, `ModifiedAt`, `ModifiedBy`.

- [ ] **Step 3.7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/CarrierCooling/ \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(carrier-cooling): add EF config, repository, DbSet, and migration for CarrierCoolingSettings"
```

---

## Task 4: Application — GetCarrierCoolingMatrix Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierCoolingRowDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierGroupDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/GetCarrierCoolingMatrixHandlerTests.cs`

- [ ] **Step 4.1: Write the failing handler tests**

```csharp
// backend/test/Anela.Heblo.Tests/Application/CarrierCooling/GetCarrierCoolingMatrixHandlerTests.cs
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CarrierCooling;

public class GetCarrierCoolingMatrixHandlerTests
{
    private readonly Mock<ICarrierCoolingRepository> _repositoryMock = new();
    private readonly Mock<IShippingMethodCatalog> _catalogMock = new();
    private readonly GetCarrierCoolingMatrixHandler _sut;

    public GetCarrierCoolingMatrixHandlerTests()
    {
        _sut = new GetCarrierCoolingMatrixHandler(_repositoryMock.Object, _catalogMock.Object);
    }

    [Fact]
    public async Task Handle_DefaultsCoolingToNone_WhenNoStoredSetting()
    {
        // Arrange
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.Zasilkovna, DeliveryHandling.NaRuky),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Carrier.Should().Be(Carriers.Zasilkovna);
        result.Groups[0].Rows.Should().HaveCount(1);
        result.Groups[0].Rows[0].DeliveryHandling.Should().Be(DeliveryHandling.NaRuky);
        result.Groups[0].Rows[0].Cooling.Should().Be(Cooling.None);
    }

    [Fact]
    public async Task Handle_UsesStoredCooling_WhenSettingExists()
    {
        // Arrange
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.PPL, DeliveryHandling.Box),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new(Carriers.PPL, DeliveryHandling.Box, Cooling.L1, "user1"),
            });

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert
        result.Groups[0].Rows[0].Cooling.Should().Be(Cooling.L1);
    }

    [Fact]
    public async Task Handle_GroupsByCarrier()
    {
        // Arrange
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.GLS, DeliveryHandling.NaRuky),
                (Carriers.GLS, DeliveryHandling.Box),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>());

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Carrier.Should().Be(Carriers.GLS);
        result.Groups[0].Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_IgnoresStoredSettingsNotInCatalog()
    {
        // Arrange — catalog has only PPL/NaRuky, but DB has a stale Osobak entry
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions()).Returns(
            new List<(Carriers, DeliveryHandling)>
            {
                (Carriers.PPL, DeliveryHandling.NaRuky),
            }.AsReadOnly());

        _repositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CarrierCoolingSetting>
            {
                new(Carriers.Osobak, DeliveryHandling.NaRuky, Cooling.L2, "user1"),
            });

        // Act
        var result = await _sut.Handle(new GetCarrierCoolingMatrixRequest(), CancellationToken.None);

        // Assert — only the PPL row appears, Osobak is not rendered
        result.Groups.Should().HaveCount(1);
        result.Groups[0].Carrier.Should().Be(Carriers.PPL);
    }
}
```

- [ ] **Step 4.2: Run the test and confirm it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "GetCarrierCoolingMatrixHandlerTests" -v minimal
```
Expected: Fails — types do not exist yet.

- [ ] **Step 4.3: Create the DTOs**

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierCoolingRowDto.cs
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.CarrierCooling.Contracts;

public class CarrierCoolingRowDto
{
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/Contracts/CarrierGroupDto.cs
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Application.Features.CarrierCooling.Contracts;

public class CarrierGroupDto
{
    public Carriers Carrier { get; set; }
    public List<CarrierCoolingRowDto> Rows { get; set; } = new();
}
```

- [ ] **Step 4.4: Create the request and response**

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;

public class GetCarrierCoolingMatrixRequest : IRequest<GetCarrierCoolingMatrixResponse>
{
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixResponse.cs
using Anela.Heblo.Application.Features.CarrierCooling.Contracts;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;

public class GetCarrierCoolingMatrixResponse
{
    public List<CarrierGroupDto> Groups { get; set; } = new();
}
```

- [ ] **Step 4.5: Create the handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/GetCarrierCoolingMatrix/GetCarrierCoolingMatrixHandler.cs
using Anela.Heblo.Application.Features.CarrierCooling.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;

public class GetCarrierCoolingMatrixHandler : IRequestHandler<GetCarrierCoolingMatrixRequest, GetCarrierCoolingMatrixResponse>
{
    private readonly ICarrierCoolingRepository _repository;
    private readonly IShippingMethodCatalog _catalog;

    public GetCarrierCoolingMatrixHandler(ICarrierCoolingRepository repository, IShippingMethodCatalog catalog)
    {
        _repository = repository;
        _catalog = catalog;
    }

    public async Task<GetCarrierCoolingMatrixResponse> Handle(
        GetCarrierCoolingMatrixRequest request,
        CancellationToken cancellationToken)
    {
        var available = _catalog.GetAvailableDeliveryOptions();
        var stored = await _repository.GetAllAsync(cancellationToken);
        var storedLookup = stored.ToDictionary(s => (s.Carrier, s.DeliveryHandling), s => s.Cooling);

        var groups = available
            .GroupBy(x => x.Carrier)
            .Select(g => new CarrierGroupDto
            {
                Carrier = g.Key,
                Rows = g.Select(x => new CarrierCoolingRowDto
                {
                    DeliveryHandling = x.Handling,
                    Cooling = storedLookup.TryGetValue((x.Carrier, x.Handling), out var c) ? c : Cooling.None,
                }).ToList(),
            })
            .ToList();

        return new GetCarrierCoolingMatrixResponse { Groups = groups };
    }
}
```

- [ ] **Step 4.6: Run the tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "GetCarrierCoolingMatrixHandlerTests" -v minimal
```
Expected: 4 tests pass.

- [ ] **Step 4.7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CarrierCooling/ \
        backend/test/Anela.Heblo.Tests/Application/CarrierCooling/GetCarrierCoolingMatrixHandlerTests.cs
git commit -m "feat(carrier-cooling): add GetCarrierCoolingMatrix use case — handler left-joins catalog with stored settings"
```

---

## Task 5: Application — SetCarrierCooling Use Case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingValidator.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs`

- [ ] **Step 5.1: Write the failing tests**

```csharp
// backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CarrierCooling;

public class SetCarrierCoolingHandlerTests
{
    private readonly Mock<ICarrierCoolingRepository> _repositoryMock = new();
    private readonly SetCarrierCoolingHandler _sut;

    public SetCarrierCoolingHandlerTests()
    {
        _sut = new SetCarrierCoolingHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_CallsUpsertWithCorrectArgs_AndReturnsSuccess()
    {
        // Arrange
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        _repositoryMock
            .Setup(r => r.UpsertAsync(
                Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "user-123",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.UpsertAsync(
            Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "user-123",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class SetCarrierCoolingValidatorTests
{
    private readonly Mock<IShippingMethodCatalog> _catalogMock = new();

    private SetCarrierCoolingValidator CreateValidator() => new(_catalogMock.Object);

    private void SetupCatalog(params (Carriers, DeliveryHandling)[] options)
    {
        _catalogMock.Setup(c => c.GetAvailableDeliveryOptions())
            .Returns(options.ToList().AsReadOnly());
    }

    [Fact]
    public void Validator_PassesForAvailableCombo()
    {
        // Arrange
        SetupCatalog((Carriers.PPL, DeliveryHandling.NaRuky));
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.PPL,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        // Act
        var result = validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_FailsForUnavailableCarrierHandlingCombo()
    {
        // Arrange — catalog only has PPL/NaRuky, but request asks for GLS/NaRuky
        SetupCatalog((Carriers.PPL, DeliveryHandling.NaRuky));
        var validator = CreateValidator();
        var request = new SetCarrierCoolingRequest
        {
            Carrier = Carriers.GLS,
            DeliveryHandling = DeliveryHandling.NaRuky,
            Cooling = Cooling.L1,
            ModifiedBy = "user-123",
        };

        // Act
        var result = validator.TestValidate(request);

        // Assert
        result.ShouldHaveAnyValidationError();
    }
}
```

- [ ] **Step 5.2: Run the test and confirm it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "SetCarrierCooling" -v minimal
```
Expected: Fails — types do not exist yet.

- [ ] **Step 5.3: Create the request**

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingRequest.cs
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingRequest : IRequest<SetCarrierCoolingResponse>
{
    public Carriers Carrier { get; set; }
    public DeliveryHandling DeliveryHandling { get; set; }
    public Cooling Cooling { get; set; }
    public string ModifiedBy { get; set; } = null!;
}
```

- [ ] **Step 5.4: Create the response**

Find `BaseResponse` namespace by running:
```bash
grep -r "public abstract class BaseResponse" backend/src/ --include="*.cs" -l
```
It is in `Anela.Heblo.Application.Shared`. Then:

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingResponse.cs
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingResponse : BaseResponse
{
}
```

- [ ] **Step 5.5: Create the validator**

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingValidator.cs
using Anela.Heblo.Domain.Features.Logistics;
using FluentValidation;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingValidator : AbstractValidator<SetCarrierCoolingRequest>
{
    public SetCarrierCoolingValidator(IShippingMethodCatalog catalog)
    {
        RuleFor(x => x.Carrier).IsInEnum();
        RuleFor(x => x.DeliveryHandling).IsInEnum();
        RuleFor(x => x.Cooling).IsInEnum();

        RuleFor(x => x)
            .Must(x => catalog.GetAvailableDeliveryOptions().Contains((x.Carrier, x.DeliveryHandling)))
            .WithMessage("Combination of Carrier and DeliveryHandling is not available.");
    }
}
```

- [ ] **Step 5.6: Create the handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/SetCarrierCoolingHandler.cs
using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;

public class SetCarrierCoolingHandler : IRequestHandler<SetCarrierCoolingRequest, SetCarrierCoolingResponse>
{
    private readonly ICarrierCoolingRepository _repository;

    public SetCarrierCoolingHandler(ICarrierCoolingRepository repository)
    {
        _repository = repository;
    }

    public async Task<SetCarrierCoolingResponse> Handle(
        SetCarrierCoolingRequest request,
        CancellationToken cancellationToken)
    {
        await _repository.UpsertAsync(
            request.Carrier,
            request.DeliveryHandling,
            request.Cooling,
            request.ModifiedBy,
            cancellationToken);

        return new SetCarrierCoolingResponse { Success = true };
    }
}
```

- [ ] **Step 5.7: Run the tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "SetCarrierCooling" -v minimal
```
Expected: 3 tests pass.

- [ ] **Step 5.8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CarrierCooling/UseCases/SetCarrierCooling/ \
        backend/test/Anela.Heblo.Tests/Application/CarrierCooling/SetCarrierCoolingHandlerTests.cs
git commit -m "feat(carrier-cooling): add SetCarrierCooling use case — handler, validator, tests"
```

---

## Task 6: Application Module Registration + API Controller

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs`
- Create: `backend/test/Anela.Heblo.Tests/Controllers/CarrierCoolingControllerTests.cs`

- [ ] **Step 6.1: Create CarrierCoolingModule**

```csharp
// backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Persistence.CarrierCooling;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.CarrierCooling;

public static class CarrierCoolingModule
{
    public static IServiceCollection AddCarrierCoolingModule(this IServiceCollection services)
    {
        services.AddScoped<ICarrierCoolingRepository, CarrierCoolingRepository>();
        services.AddScoped<IValidator<SetCarrierCoolingRequest>, SetCarrierCoolingValidator>();

        return services;
    }
}
```

- [ ] **Step 6.2: Register CarrierCoolingModule in ApplicationModule**

Open `backend/src/Anela.Heblo.Application/ApplicationModule.cs`. Add the using and method call alongside the other modules (e.g., after `services.AddPackingMaterialsModule();`):

```csharp
// Add near the top usings:
using Anela.Heblo.Application.Features.CarrierCooling;

// Add in the AddApplicationServices method body:
services.AddCarrierCoolingModule();
```

- [ ] **Step 6.3: Write the failing integration tests**

```csharp
// backend/test/Anela.Heblo.Tests/Controllers/CarrierCoolingControllerTests.cs
using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

[Collection("WebApp")]
public class CarrierCoolingControllerTests : IClassFixture<HebloWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CarrierCoolingControllerTests(HebloWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsMatrixWithSixGroups()
    {
        // Act
        var response = await _client.GetAsync("/api/carrier-cooling");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GetCarrierCoolingMatrixResponse>();
        body.Should().NotBeNull();
        // The real ShippingMethodCatalog returns 6 carrier+handling pairs grouped into 3 carriers
        body!.Groups.Should().HaveCount(3);
        body.Groups.Should().Contain(g => g.Carrier == Carriers.Zasilkovna);
        body.Groups.Should().Contain(g => g.Carrier == Carriers.PPL);
        body.Groups.Should().Contain(g => g.Carrier == Carriers.GLS);
    }

    [Fact]
    public async Task Get_DefaultsCoolingToNone_WhenNothingStored()
    {
        // Act
        var response = await _client.GetAsync("/api/carrier-cooling");
        var body = await response.Content.ReadFromJsonAsync<GetCarrierCoolingMatrixResponse>();

        // Assert
        body!.Groups.SelectMany(g => g.Rows).Should().AllSatisfy(row =>
            row.Cooling.Should().Be(Cooling.None));
    }

    [Fact]
    public async Task Put_StoresCooling_AndGetReturnsUpdatedValue()
    {
        // Arrange
        var putRequest = new
        {
            carrier = (int)Carriers.PPL,
            deliveryHandling = (int)DeliveryHandling.NaRuky,
            cooling = (int)Cooling.L1,
        };

        // Act
        var putResponse = await _client.PutAsJsonAsync("/api/carrier-cooling", putRequest);

        // Assert
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync("/api/carrier-cooling");
        var body = await getResponse.Content.ReadFromJsonAsync<GetCarrierCoolingMatrixResponse>();
        var pplGroup = body!.Groups.First(g => g.Carrier == Carriers.PPL);
        var naRukyRow = pplGroup.Rows.First(r => r.DeliveryHandling == DeliveryHandling.NaRuky);
        naRukyRow.Cooling.Should().Be(Cooling.L1);
    }
}
```

- [ ] **Step 6.4: Run the integration tests and confirm they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "CarrierCoolingControllerTests" -v minimal
```
Expected: Fails — controller route does not exist yet.

- [ ] **Step 6.5: Create the controller**

```csharp
// backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs
using System.Security.Claims;
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.GetCarrierCoolingMatrix;
using Anela.Heblo.Application.Features.CarrierCooling.UseCases.SetCarrierCooling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/carrier-cooling")]
public class CarrierCoolingController : BaseApiController
{
    private readonly IMediator _mediator;

    public CarrierCoolingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetCarrierCoolingMatrixResponse>> GetMatrix(
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCarrierCoolingMatrixRequest(), cancellationToken);
        return Ok(response);
    }

    [HttpPut]
    public async Task<ActionResult<SetCarrierCoolingResponse>> SetCooling(
        [FromBody] SetCarrierCoolingRequest request,
        CancellationToken cancellationToken = default)
    {
        request.ModifiedBy = GetCurrentUserId();
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }

    private string GetCurrentUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("oid")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no identity claim.");
    }
}
```

- [ ] **Step 6.6: Run the integration tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "CarrierCoolingControllerTests" -v minimal
```
Expected: 3 tests pass.

- [ ] **Step 6.7: Run the full BE test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ -v minimal
```
Expected: All tests pass (no regressions).

- [ ] **Step 6.8: dotnet build and dotnet format**

```bash
dotnet build backend/
dotnet format backend/
```
Expected: Build succeeded, 0 errors; formatter reports no changes (or reformats cleanly).

- [ ] **Step 6.9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/CarrierCooling/CarrierCoolingModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs \
        backend/test/Anela.Heblo.Tests/Controllers/CarrierCoolingControllerTests.cs
git commit -m "feat(carrier-cooling): register module, add CarrierCoolingController with integration tests"
```

---

## Task 7: Frontend — API Hook

**Files:**
- Create: `frontend/src/api/hooks/useCarrierCooling.ts`

- [ ] **Step 7.1: Create the hook**

```typescript
// frontend/src/api/hooks/useCarrierCooling.ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";

// Mirrors C# enums — values must match the backend integer serialization
export enum Carriers {
  Zasilkovna = 1,
  PPL = 2,
  GLS = 3,
  Osobak = 4,
}

export enum DeliveryHandling {
  NaRuky = 1,
  Box = 2,
}

export enum Cooling {
  None = 0,
  L1 = 1,
  L2 = 2,
}

export interface CarrierCoolingRowDto {
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
}

export interface CarrierGroupDto {
  carrier: Carriers;
  rows: CarrierCoolingRowDto[];
}

export interface GetCarrierCoolingMatrixResponse {
  groups: CarrierGroupDto[];
}

export interface SetCarrierCoolingRequest {
  carrier: Carriers;
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
}

class CarrierCoolingApiClient {
  private readonly baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl;
  }

  async makeRequest<T>(url: string, options: RequestInit = {}): Promise<T> {
    const apiClient = getAuthenticatedApiClient();
    const fullUrl = `${this.baseUrl}${url}`;
    const response = await (apiClient as any).http.fetch(fullUrl, {
      method: options.method ?? "GET",
      headers: { "Content-Type": "application/json", ...options.headers },
      body: options.body,
    });
    if (!response.ok) {
      throw new Error(`API request failed: ${response.statusText}`);
    }
    return response.json();
  }

  getMatrix(): Promise<GetCarrierCoolingMatrixResponse> {
    return this.makeRequest<GetCarrierCoolingMatrixResponse>("/api/carrier-cooling");
  }

  setCooling(request: SetCarrierCoolingRequest): Promise<void> {
    return this.makeRequest<void>("/api/carrier-cooling", {
      method: "PUT",
      body: JSON.stringify(request),
    });
  }
}

const createApiClient = (): CarrierCoolingApiClient => {
  const apiClient = getAuthenticatedApiClient();
  return new CarrierCoolingApiClient((apiClient as any).baseUrl);
};

const QUERY_KEYS = {
  matrix: ["carrierCooling", "matrix"] as const,
};

export const useCarrierCoolingMatrix = () => {
  return useQuery({
    queryKey: QUERY_KEYS.matrix,
    queryFn: () => createApiClient().getMatrix(),
  });
};

export const useSetCarrierCooling = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: SetCarrierCoolingRequest) =>
      createApiClient().setCooling(request),

    onMutate: async (request) => {
      await queryClient.cancelQueries({ queryKey: QUERY_KEYS.matrix });
      const previousData =
        queryClient.getQueryData<GetCarrierCoolingMatrixResponse>(QUERY_KEYS.matrix);

      queryClient.setQueryData<GetCarrierCoolingMatrixResponse>(
        QUERY_KEYS.matrix,
        (old) => {
          if (!old) return old;
          return {
            groups: old.groups.map((group) => {
              if (group.carrier !== request.carrier) return group;
              return {
                ...group,
                rows: group.rows.map((row) =>
                  row.deliveryHandling !== request.deliveryHandling
                    ? row
                    : { ...row, cooling: request.cooling }
                ),
              };
            }),
          };
        }
      );

      return { previousData };
    },

    onError: (_err, _request, context) => {
      if (context?.previousData) {
        queryClient.setQueryData(QUERY_KEYS.matrix, context.previousData);
      }
    },

    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.matrix });
    },
  });
};
```

- [ ] **Step 7.2: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit
```
Expected: No errors for the new file.

- [ ] **Step 7.3: Commit**

```bash
git add frontend/src/api/hooks/useCarrierCooling.ts
git commit -m "feat(carrier-cooling): add useCarrierCooling hook with optimistic update"
```

---

## Task 8: Frontend — UI Components, Routing, Navigation

**Files:**
- Create: `frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx`
- Create: `frontend/src/pages/customer/CoolingPage.tsx`
- Modify: `frontend/src/components/Layout/Sidebar.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 8.1: Create CarrierCoolingMatrix component**

```tsx
// frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx
import React, { useState } from "react";
import {
  Carriers,
  CarrierGroupDto,
  Cooling,
  DeliveryHandling,
  SetCarrierCoolingRequest,
} from "../../../api/hooks/useCarrierCooling";

interface CarrierCoolingMatrixProps {
  groups: CarrierGroupDto[];
  onSetCooling: (request: SetCarrierCoolingRequest) => void;
  isSaving: boolean;
}

const CARRIER_LABELS: Record<Carriers, string> = {
  [Carriers.Zasilkovna]: "Zásilkovna",
  [Carriers.PPL]: "PPL",
  [Carriers.GLS]: "GLS",
  [Carriers.Osobak]: "Osobní odběr",
};

const HANDLING_LABELS: Record<DeliveryHandling, string> = {
  [DeliveryHandling.NaRuky]: "Na ruky",
  [DeliveryHandling.Box]: "Box",
};

const COOLING_OPTIONS: { value: Cooling; label: string }[] = [
  { value: Cooling.None, label: "Bez chlazení" },
  { value: Cooling.L1, label: "L1" },
  { value: Cooling.L2, label: "L2" },
];

const CarrierCoolingMatrix: React.FC<CarrierCoolingMatrixProps> = ({
  groups,
  onSetCooling,
  isSaving,
}) => {
  const [savingKey, setSavingKey] = useState<string | null>(null);

  const handleChange = (
    carrier: Carriers,
    deliveryHandling: DeliveryHandling,
    cooling: Cooling
  ) => {
    const key = `${carrier}-${deliveryHandling}`;
    setSavingKey(key);
    onSetCooling({ carrier, deliveryHandling, cooling });
    setTimeout(() => setSavingKey(null), 1500);
  };

  return (
    <div className="space-y-4 p-4">
      {groups.map((group) => (
        <div
          key={group.carrier}
          className="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden"
        >
          <div className="px-4 py-3 border-b border-gray-100 bg-gray-50">
            <h2 className="text-sm font-semibold text-gray-800">
              {CARRIER_LABELS[group.carrier] ?? `Dopravce ${group.carrier}`}
            </h2>
          </div>
          <div className="divide-y divide-gray-50">
            {group.rows.map((row) => {
              const key = `${group.carrier}-${row.deliveryHandling}`;
              const isSavingRow = savingKey === key;

              return (
                <div
                  key={row.deliveryHandling}
                  className="flex items-center px-4 py-3 gap-6"
                >
                  <span className="w-24 text-sm text-gray-700 flex-shrink-0">
                    {HANDLING_LABELS[row.deliveryHandling] ??
                      String(row.deliveryHandling)}
                  </span>
                  <div className="flex gap-6">
                    {COOLING_OPTIONS.map((option) => (
                      <label
                        key={option.value}
                        className="flex items-center gap-2 cursor-pointer"
                      >
                        <input
                          type="radio"
                          name={key}
                          value={option.value}
                          checked={row.cooling === option.value}
                          onChange={() =>
                            handleChange(
                              group.carrier,
                              row.deliveryHandling,
                              option.value
                            )
                          }
                          disabled={isSaving}
                          className="h-4 w-4 text-indigo-600 cursor-pointer"
                        />
                        <span className="text-sm text-gray-700">
                          {option.label}
                        </span>
                      </label>
                    ))}
                  </div>
                  {isSavingRow && (
                    <span className="text-xs text-gray-400 ml-2 animate-pulse">
                      Ukládám…
                    </span>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );
};

export default CarrierCoolingMatrix;
```

- [ ] **Step 8.2: Create CoolingPage**

```tsx
// frontend/src/pages/customer/CoolingPage.tsx
import React from "react";
import { Thermometer } from "lucide-react";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";
import CarrierCoolingMatrix from "../../components/customer/cooling/CarrierCoolingMatrix";
import {
  useCarrierCoolingMatrix,
  useSetCarrierCooling,
} from "../../api/hooks/useCarrierCooling";

const CoolingPage: React.FC = () => {
  const { data, isLoading, error } = useCarrierCoolingMatrix();
  const { mutate: setCooling, isPending } = useSetCarrierCooling();

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      <div className="flex-shrink-0 px-4 py-3">
        <h1 className="text-lg font-semibold text-gray-900 flex items-center gap-3">
          <Thermometer className="h-6 w-6 text-indigo-600" />
          Chlazení
        </h1>
        <p className="text-sm text-gray-500 mt-1">
          Nastavení úrovně chlazení pro každého dopravce a typ doručení.
        </p>
      </div>

      <div className="flex-1 overflow-y-auto">
        {isLoading && (
          <div className="flex items-center justify-center h-32">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
          </div>
        )}

        {error && (
          <div className="mx-4 p-4 bg-red-50 border border-red-200 rounded-lg text-red-600 text-sm">
            Nepodařilo se načíst nastavení chlazení. Zkuste obnovit stránku.
          </div>
        )}

        {data && (
          <CarrierCoolingMatrix
            groups={data.groups}
            onSetCooling={setCooling}
            isSaving={isPending}
          />
        )}
      </div>
    </div>
  );
};

export default CoolingPage;
```

- [ ] **Step 8.3: Add Chlazení to the Zákaznické sidebar section**

In `frontend/src/components/Layout/Sidebar.tsx`, find the `zakaznicke` section items array (around line 130). After the `smartsupp` entry, add:

```typescript
{ id: "chlazeni", name: "Chlazení", href: "/customer/cooling" },
```

The full items array for zakaznicke becomes:
```typescript
items: [
  { id: "vydane-faktury", name: "Vydané faktury", href: "/customer/issued-invoices" },
  { id: "prehled-bankovnich-vypisu", name: "Bankovní výpisy", href: "/customer/bank-statements-overview" },
  { id: "archiv-expedic-zakaznicke", name: "Expedice", href: "/logistics/expedition-archive" },
  { id: "knowledge-base", name: "Poradenství (KB)", href: "/knowledge-base" },
  { id: "smartsupp", name: "Smartsupp", href: "/customer/smartsupp" },
  { id: "chlazeni", name: "Chlazení", href: "/customer/cooling" },
],
```

- [ ] **Step 8.4: Add import and route in App.tsx**

In `frontend/src/App.tsx`:

1. Add the import after the existing customer page imports (e.g., after `import SmartsuppChatsPage`):
```typescript
import CoolingPage from "./pages/customer/CoolingPage";
```

2. In the desktop layout Routes block, add the route after the `/customer/smartsupp` route:
```tsx
<Route path="/customer/cooling" element={<CoolingPage />} />
```

- [ ] **Step 8.5: Run the frontend build and lint**

```bash
cd frontend && npm run build && npm run lint
```
Expected: Build succeeds with 0 errors. ESLint passes with 0 errors.

- [ ] **Step 8.6: Commit**

```bash
git add frontend/src/components/customer/cooling/CarrierCoolingMatrix.tsx \
        frontend/src/pages/customer/CoolingPage.tsx \
        frontend/src/components/Layout/Sidebar.tsx \
        frontend/src/App.tsx
git commit -m "feat(carrier-cooling): add CoolingPage, CarrierCoolingMatrix component, sidebar entry, and route"
```

---

## Task 9: End-to-End Verification

- [ ] **Step 9.1: Run full BE test suite**

```bash
dotnet test backend/ -v minimal
```
Expected: All tests pass. 0 failed.

- [ ] **Step 9.2: Run dotnet build and dotnet format on the full solution**

```bash
dotnet build backend/
dotnet format backend/ --verify-no-changes
```
Expected: Build succeeded, formatter finds no issues.

- [ ] **Step 9.3: Run full FE build and lint**

```bash
cd frontend && npm run build && npm run lint
```
Expected: Build and lint both pass.

- [ ] **Step 9.4: Manual smoke test**

Start the application locally (`dotnet run` + `npm start`). Then:

1. Open **Zákaznické → Chlazení** in the sidebar — the page loads with 3 carrier cards (Zásilkovna, PPL, GLS).
2. Each card shows 2 rows (Na ruky, Box), each row has 3 radio buttons (Bez chlazení, L1, L2).
3. All radios start on **Bez chlazení** (None).
4. Click **L1** on PPL / Na ruky — "Ukládám…" appears briefly, no Save button needed.
5. Reload the page — PPL / Na ruky still shows **L1**.
6. Click **L2** on Zásilkovna / Box → reload → **L2** persists.
7. There is no Save button anywhere on the page.

- [ ] **Step 9.5: Commit final verification note (if any formatting fixes were needed)**

If `dotnet format` or lint made changes, stage and commit them:
```bash
git add -u
git commit -m "chore: apply format fixes after carrier-cooling implementation"
```

---

## Spec Coverage Checklist (Self-Review)

| Requirement | Task |
|-------------|------|
| `DeliveryHandling` enum (NaRuky, Box) | Task 1 |
| `CarrierCoolingSetting` entity with composite key | Task 1 |
| `ICarrierCoolingRepository` + `IShippingMethodCatalog` | Task 1 |
| Extract `ShippingList` into `ShippingMethodRegistry` | Task 2 |
| `ShippingMethodCatalog` classifies DO_RUKY→NaRuky, PARCELSHOP/ZPOINT→Box | Task 2 |
| EXPORT and Osobak excluded | Task 2 |
| Register `IShippingMethodCatalog` as singleton | Task 2 |
| EF config with enums as strings, composite key | Task 3 |
| `UpsertAsync` find-or-insert | Task 3 |
| `DbSet<CarrierCoolingSetting>` in ApplicationDbContext | Task 3 |
| Migration `AddCarrierCoolingSettings` (manual) | Task 3 |
| `GetCarrierCoolingMatrix` handler left-joins catalog with stored, defaults None | Task 4 |
| `SetCarrierCooling` handler + FluentValidation (enum values, available combo) | Task 5 |
| `CarrierCoolingModule` registers repository + validator | Task 6 |
| `CarrierCoolingController` GET + PUT | Task 6 |
| `ModifiedBy` from user claims (same pattern as DashboardController) | Task 6 |
| `useCarrierCoolingMatrix` + `useSetCarrierCooling` with optimistic update | Task 7 |
| Sidebar: Zákaznické → Chlazení | Task 8 |
| Route `/customer/cooling` | Task 8 |
| `CarrierCoolingMatrix` — 3 carriers, na ruky/box rows, 3 radios | Task 8 |
| No Save button — immediate fire on radio change | Task 8 |
| BE unit tests: handler, validator, ShippingMethodCatalog | Tasks 2, 4, 5 |
| BE integration tests: GET + PUT | Task 6 |
| Storage only — nothing consumes the setting yet | (not applicable — no consumer added) |
