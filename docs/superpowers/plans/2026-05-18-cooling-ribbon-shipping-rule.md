# Connect Carrier Cooling Config to Expedition List Ribbon — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the expedition list PDF ribbon ("CHLAZENÁ ZÁSILKA") respect the per-carrier cooling matrix, showing only when the shipping method's configured level covers at least one product in the order.

**Architecture:** Thread `CarrierCooling` (a `Cooling` enum value) onto each `ExpeditionOrder` at the time it's built in `ShoptetApiExpeditionListSource.CreatePickingList`. The carrier cooling matrix is loaded once per picking-list run from `ICarrierCoolingRepository`. `ExpeditionOrder.IsCooled` is updated to apply the rule `item.Cooling != None && item.Cooling <= CarrierCooling`. `ExpeditionProtocolDocument` already reads `order.IsCooled` — no changes there.

**Tech Stack:** C# / .NET 8, xUnit, FluentAssertions, Moq

---

## Files

| Action | Path |
|--------|------|
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` |

**No changes needed:**
- `ExpeditionProtocolDocument.cs` — already reads `order.IsCooled`
- `ICarrierCoolingRepository.cs` — consumed read-only, signature unchanged
- `ShoptetApiAdapterServiceCollectionExtensions.cs` — `Transient` lifetime is safe to inject a scoped repository; no changes needed

---

## Task 1: TDD — `ShippingMethodRegistry.ResolveDeliveryHandling`

Extract the `DeliveryHandling` derivation logic into a shared static helper so both `ShippingMethodCatalog` and `ShoptetApiExpeditionListSource` use the same logic.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs`

- [ ] **Step 1.1: Write failing tests for `ResolveDeliveryHandling`**

Add these tests to `ShoptetApiExpeditionListSourceTests.cs`, before the closing `}` of the class:

```csharp
// ─── ResolveDeliveryHandling ──────────────────────────────────────────────────

[Theory]
[InlineData("ZASILKOVNA_DO_RUKY")]
[InlineData("PPL_DO_RUKY")]
[InlineData("PPL_DO_RUKY_CHLAZENY")]
[InlineData("ZASILKOVNA_DO_RUKY_SK")]
[InlineData("ZASILKOVNA_DO_RUKY_SK_CHLAZENY")]
[InlineData("GLS_DO_RUKY")]
public void ResolveDeliveryHandling_ReturnsNaRuky_ForDoRukyMethods(string name)
{
    var method = new ShippingMethod { Carrier = Carriers.PPL, Name = name, Guids = [] };
    ShippingMethodRegistry.ResolveDeliveryHandling(method).Should().Be(DeliveryHandling.NaRuky);
}

[Theory]
[InlineData("PPL_PARCELSHOP")]
[InlineData("PPL_PARCELSHOP_CHLAZENY")]
[InlineData("ZASILKOVNA_ZPOINT")]
[InlineData("ZASILKOVNA_ZPOINT_CHLAZENY")]
[InlineData("ZASILKOVNA_ZPOINT_ZDARMA")]
[InlineData("ZASILKOVNA_ZPOINT_CHLAZENY_ZDARMA")]
[InlineData("GLS_PARCELSHOP")]
public void ResolveDeliveryHandling_ReturnsBox_ForParcelshopAndZpointMethods(string name)
{
    var method = new ShippingMethod { Carrier = Carriers.PPL, Name = name, Guids = [] };
    ShippingMethodRegistry.ResolveDeliveryHandling(method).Should().Be(DeliveryHandling.Box);
}

[Theory]
[InlineData("PPL_EXPORT")]
[InlineData("PPL_EXPORT_CHLAZENY")]
[InlineData("GLS_EXPORT")]
[InlineData("OSOBAK")]
public void ResolveDeliveryHandling_ReturnsNull_ForExportAndOsobakMethods(string name)
{
    var method = new ShippingMethod { Carrier = Carriers.PPL, Name = name, Guids = [] };
    ShippingMethodRegistry.ResolveDeliveryHandling(method).Should().BeNull();
}
```

- [ ] **Step 1.2: Run tests to confirm RED**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "ResolveDeliveryHandling" -v minimal 2>&1 | tail -20
```

Expected: compile error — `ShippingMethodRegistry` has no method `ResolveDeliveryHandling`.

- [ ] **Step 1.3: Add `ResolveDeliveryHandling` to `ShippingMethodRegistry.cs`**

Add the method inside `internal static class ShippingMethodRegistry`, after the `ByGuid` dictionary:

```csharp
internal static DeliveryHandling? ResolveDeliveryHandling(ShippingMethod method) =>
    method.Name.Contains("DO_RUKY") ? DeliveryHandling.NaRuky :
    method.Name.Contains("PARCELSHOP") || method.Name.Contains("ZPOINT") ? DeliveryHandling.Box :
    (DeliveryHandling?)null;
```

The full updated file `ShippingMethodRegistry.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

internal static class ShippingMethodRegistry
{
    // GUIDs discovered via: GET /api/eshop?include=shippingMethods (production store 269953/anela.cz)
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

    internal static DeliveryHandling? ResolveDeliveryHandling(ShippingMethod method) =>
        method.Name.Contains("DO_RUKY") ? DeliveryHandling.NaRuky :
        method.Name.Contains("PARCELSHOP") || method.Name.Contains("ZPOINT") ? DeliveryHandling.Box :
        (DeliveryHandling?)null;
}
```

- [ ] **Step 1.4: Refactor `ShippingMethodCatalog.cs` to use the shared helper**

Replace the full file content of `ShippingMethodCatalog.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShippingMethodCatalog : IShippingMethodCatalog
{
    public IReadOnlyList<(Carriers Carrier, DeliveryHandling Handling)> GetAvailableDeliveryOptions()
    {
        return ShippingMethodRegistry.ShippingList
            .Where(m => m.Carrier != Carriers.Osobak && !m.Name.Contains("_EXPORT"))
            .Select(m => (m.Carrier, Handling: ShippingMethodRegistry.ResolveDeliveryHandling(m)))
            .Where(x => x.Handling.HasValue)
            .Select(x => (x.Carrier, x.Handling!.Value))
            .Distinct()
            .ToList()
            .AsReadOnly();
    }
}
```

- [ ] **Step 1.5: Run tests to confirm GREEN**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "ResolveDeliveryHandling" -v minimal 2>&1 | tail -20
```

Expected: all 3 new test methods pass (total ~16 cases).

- [ ] **Step 1.6: Run full test suite to confirm no regressions**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal 2>&1 | tail -10
```

Expected: all tests pass.

- [ ] **Step 1.7: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodRegistry.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShippingMethodCatalog.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs
git commit -m "refactor: extract ResolveDeliveryHandling into ShippingMethodRegistry"
```

---

## Task 2: TDD — `ExpeditionOrder.CarrierCooling` + new `IsCooled` rule

Add `CarrierCooling` to `ExpeditionOrder`, update `IsCooled` to gate on the carrier level, and update existing tests to match the new semantics.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs`

- [ ] **Step 2.1: Write failing tests for the new `IsCooled` truth table**

Add these tests to `ShoptetApiExpeditionListSourceTests.cs`, after the `ResolveDeliveryHandling` tests:

```csharp
// ─── IsCooled truth table (carrier-aware) ─────────────────────────────────────

[Theory]
// Carrier None → never show ribbon regardless of product cooling
[InlineData(Cooling.None, Cooling.None,  false)]
[InlineData(Cooling.None, Cooling.L1,   false)]
[InlineData(Cooling.None, Cooling.L2,   false)]
// Carrier L1 → only L1 products trigger ribbon (L2 > L1 so does NOT match)
[InlineData(Cooling.L1,   Cooling.None,  false)]
[InlineData(Cooling.L1,   Cooling.L1,   true)]
[InlineData(Cooling.L1,   Cooling.L2,   false)]
// Carrier L2 → L1 and L2 products both trigger ribbon
[InlineData(Cooling.L2,   Cooling.None,  false)]
[InlineData(Cooling.L2,   Cooling.L1,   true)]
[InlineData(Cooling.L2,   Cooling.L2,   true)]
public void IsCooled_MatchesCarrierAwareRule(Cooling carrierCooling, Cooling itemCooling, bool expected)
{
    var order = new ExpeditionOrder
    {
        Code = "ORD001",
        CustomerName = "Test",
        Address = "Praha",
        Phone = "123",
        CarrierCooling = carrierCooling,
        Items = [new ExpeditionOrderItem { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = itemCooling }],
    };

    order.IsCooled.Should().Be(expected);
}

[Fact]
public void IsCooled_True_WhenAtLeastOneItemMatchesCarrierLevel()
{
    // L2 carrier, two items: L2-only item and None item — the L2 item qualifies
    var order = new ExpeditionOrder
    {
        Code = "ORD002",
        CustomerName = "Test",
        Address = "Praha",
        Phone = "123",
        CarrierCooling = Cooling.L2,
        Items =
        [
            new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.None },
            new() { ProductCode = "P002", Name = "B", Variant = string.Empty, WarehousePosition = "A2", Quantity = 1, Cooling = Cooling.L2 },
        ],
    };

    order.IsCooled.Should().BeTrue();
}
```

- [ ] **Step 2.2: Update the three existing `IsCooled` tests to set `CarrierCooling`**

The old tests (`IsCooled_False_WhenAllItemsHaveCoolingNone`, `IsCooled_True_WhenAnyItemHasCoolingL1`, `IsCooled_True_WhenAnyItemHasCoolingL2`) must be updated. They tested the old "any cooled product" semantics. Under the new rule they need `CarrierCooling` set.

Find and replace the three test methods in `ShoptetApiExpeditionListSourceTests.cs`:

```csharp
[Fact]
public void ExpeditionOrder_IsCooled_False_WhenAllItemsHaveCoolingNone()
{
    var order = new ExpeditionOrder
    {
        Code = "ORD001",
        CustomerName = "Test",
        Address = "Praha",
        Phone = "123",
        CarrierCooling = Cooling.L2,
        Items = new List<ExpeditionOrderItem>
        {
            new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.None },
            new() { ProductCode = "P002", Name = "B", Variant = string.Empty, WarehousePosition = "A2", Quantity = 1, Cooling = Cooling.None },
        },
    };

    order.IsCooled.Should().BeFalse();
}

[Fact]
public void ExpeditionOrder_IsCooled_True_WhenAnyItemHasCoolingL1()
{
    var order = new ExpeditionOrder
    {
        Code = "ORD002",
        CustomerName = "Test",
        Address = "Praha",
        Phone = "123",
        CarrierCooling = Cooling.L1,
        Items = new List<ExpeditionOrderItem>
        {
            new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.None },
            new() { ProductCode = "P002", Name = "B", Variant = string.Empty, WarehousePosition = "A2", Quantity = 1, Cooling = Cooling.L1 },
        },
    };

    order.IsCooled.Should().BeTrue();
}

[Fact]
public void ExpeditionOrder_IsCooled_True_WhenAnyItemHasCoolingL2()
{
    var order = new ExpeditionOrder
    {
        Code = "ORD003",
        CustomerName = "Test",
        Address = "Praha",
        Phone = "123",
        CarrierCooling = Cooling.L2,
        Items = new List<ExpeditionOrderItem>
        {
            new() { ProductCode = "P001", Name = "A", Variant = string.Empty, WarehousePosition = "A1", Quantity = 1, Cooling = Cooling.L2 },
        },
    };

    order.IsCooled.Should().BeTrue();
}
```

- [ ] **Step 2.3: Run tests to confirm RED**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "IsCooled" -v minimal 2>&1 | tail -20
```

Expected: compile error — `ExpeditionOrder` has no property `CarrierCooling`.

- [ ] **Step 2.4: Update `ExpeditionProtocolData.cs`**

Replace the full content of `ExpeditionProtocolData.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ExpeditionProtocolData
{
    public string CarrierDisplayName { get; set; } = null!;
    public List<ExpeditionOrder> Orders { get; set; } = new();
}

public class ExpeditionOrder
{
    public string Code { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string Address { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? CustomerRemark { get; set; }
    public string? EshopRemark { get; set; }
    public List<ExpeditionOrderItem> Items { get; set; } = new();
    public Cooling CarrierCooling { get; set; } = Cooling.None;

    public bool IsCooled => Items.Any(i => i.Cooling != Cooling.None && i.Cooling <= CarrierCooling);
}

public class ExpeditionOrderItem
{
    public string ProductCode { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Variant { get; set; } = null!;
    public string WarehousePosition { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal StockCount { get; set; }
    public decimal StockDemand { get; set; }
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsFromSet { get; set; }
    public string? SetName { get; set; }
    public Cooling Cooling { get; set; } = Cooling.None;
}
```

- [ ] **Step 2.5: Run tests to confirm GREEN**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "IsCooled" -v minimal 2>&1 | tail -20
```

Expected: all `IsCooled` tests pass (3 updated + new theory + 1 multi-item = 12 cases).

- [ ] **Step 2.6: Run full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal 2>&1 | tail -10
```

Expected: all tests pass.

- [ ] **Step 2.7: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs
git commit -m "feat: add CarrierCooling to ExpeditionOrder and apply carrier-aware IsCooled rule"
```

---

## Task 3: TDD — Inject carrier cooling into `CreatePickingList`

Wire `ICarrierCoolingRepository` into `ShoptetApiExpeditionListSource`, load the matrix once per run, expose a `ResolveCarrierCooling` static helper (testable), and assign `order.CarrierCooling` in the per-order loop.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`

- [ ] **Step 3.1: Update `BuildSource` test helper and fix direct constructor call**

Find the current `BuildSource` helper in `ShoptetApiExpeditionListSourceTests.cs`:

```csharp
private static ShoptetApiExpeditionListSource BuildSource(ShoptetOrderClient client) =>
    new(client, TimeProvider.System, new Mock<ICatalogRepository>().Object);
```

Replace it with:

```csharp
private static ShoptetApiExpeditionListSource BuildSource(
    ShoptetOrderClient client,
    ICarrierCoolingRepository? carrierCooling = null)
{
    var coolingRepo = carrierCooling ?? BuildEmptyCoolingRepo();
    return new ShoptetApiExpeditionListSource(client, TimeProvider.System, new Mock<ICatalogRepository>().Object, coolingRepo);
}

private static ICarrierCoolingRepository BuildEmptyCoolingRepo()
{
    var mock = new Mock<ICarrierCoolingRepository>();
    mock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<CarrierCoolingSetting>());
    return mock.Object;
}
```

Also find the direct constructor call in `CreatePickingList_EnrichesCooling_FromCatalog` (near line 576):

```csharp
var source = new ShoptetApiExpeditionListSource(client, TimeProvider.System, catalogMock.Object);
```

Replace it with:

```csharp
var source = new ShoptetApiExpeditionListSource(client, TimeProvider.System, catalogMock.Object, BuildEmptyCoolingRepo());
```

- [ ] **Step 3.2: Write failing tests for `ResolveCarrierCooling` and the carrier-aware integration**

Add these tests to `ShoptetApiExpeditionListSourceTests.cs`:

```csharp
// ─── ResolveCarrierCooling ────────────────────────────────────────────────────

[Fact]
public void ResolveCarrierCooling_ReturnsCoolingFromMatrix_WhenKeyExists()
{
    var matrix = new Dictionary<(Carriers, DeliveryHandling), Cooling>
    {
        [(Carriers.PPL, DeliveryHandling.NaRuky)] = Cooling.L1,
    };

    var result = ShoptetApiExpeditionListSource.ResolveCarrierCooling(PplDoRukyGuid, matrix);

    result.Should().Be(Cooling.L1);
}

[Fact]
public void ResolveCarrierCooling_ReturnsNone_WhenGuidNotInRegistry()
{
    var matrix = new Dictionary<(Carriers, DeliveryHandling), Cooling>
    {
        [(Carriers.PPL, DeliveryHandling.NaRuky)] = Cooling.L1,
    };

    var result = ShoptetApiExpeditionListSource.ResolveCarrierCooling("unknown-guid", matrix);

    result.Should().Be(Cooling.None);
}

[Fact]
public void ResolveCarrierCooling_ReturnsNone_WhenMatrixHasNoEntryForCarrierHandling()
{
    // PPL_DO_RUKY maps to (PPL, NaRuky) — not in matrix → None
    var result = ShoptetApiExpeditionListSource.ResolveCarrierCooling(
        PplDoRukyGuid,
        new Dictionary<(Carriers, DeliveryHandling), Cooling>());

    result.Should().Be(Cooling.None);
}

[Fact]
public void ResolveCarrierCooling_ReturnsNone_ForExportMethod()
{
    // PPL_EXPORT GUID — ResolveDeliveryHandling returns null for EXPORT → always None
    const string PplExportGuid = "f17a0a12-0ebe-11eb-933a-002590dad85e";
    var matrix = new Dictionary<(Carriers, DeliveryHandling), Cooling>
    {
        [(Carriers.PPL, DeliveryHandling.NaRuky)] = Cooling.L1,
        [(Carriers.PPL, DeliveryHandling.Box)] = Cooling.L2,
    };

    var result = ShoptetApiExpeditionListSource.ResolveCarrierCooling(PplExportGuid, matrix);

    result.Should().Be(Cooling.None);
}

// ─── CreatePickingList — carrier cooling integration ──────────────────────────

[Fact]
public async Task CreatePickingList_AssignsCarrierCooling_FromMatrix()
{
    // Arrange — PPL_DO_RUKY order with L1 product; matrix says (PPL, NaRuky)=L1
    var listResp = SinglePageList(("P001", PplDoRukyGuid));
    var client = BuildClient(req =>
    {
        if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
            return Json(listResp);
        return Json(DetailFor(req.RequestUri.Segments.Last()));
    });

    var coolingMock = new Mock<ICarrierCoolingRepository>();
    coolingMock
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[]
        {
            new CarrierCoolingSetting(Carriers.PPL, DeliveryHandling.NaRuky, Cooling.L1, "test"),
        });

    var source = BuildSource(client, coolingMock.Object);

    // Act
    var result = await source.CreatePickingList(DefaultRequest(), null);

    // Assert — repository was called exactly once per run
    coolingMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    result.TotalCount.Should().Be(1);

    // Cleanup
    foreach (var file in result.ExportedFiles.Where(File.Exists))
        File.Delete(file);
}

[Fact]
public async Task CreatePickingList_LoadsMatrixOnce_AcrossMultipleOrderBatches()
{
    // Arrange — two batches (PPL and Zasilkovna) → matrix still loaded only once
    var listResp = SinglePageList(("Z001", ZasilkovnaDoRukyGuid), ("P001", PplDoRukyGuid));
    var client = BuildClient(req =>
    {
        if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?"))
            return Json(listResp);
        return Json(DetailFor(req.RequestUri.Segments.Last()));
    });

    var coolingMock = new Mock<ICarrierCoolingRepository>();
    coolingMock
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<CarrierCoolingSetting>());

    var source = BuildSource(client, coolingMock.Object);

    // Act
    var result = await source.CreatePickingList(DefaultRequest(), null);

    // Assert — matrix loaded exactly once, not once per carrier batch
    coolingMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    result.TotalCount.Should().Be(2);

    // Cleanup
    foreach (var file in result.ExportedFiles.Where(File.Exists))
        File.Delete(file);
}
```

- [ ] **Step 3.3: Run tests to confirm RED**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "ResolveCarrierCooling|AssignsCarrierCooling|LoadsMatrixOnce" -v minimal 2>&1 | tail -20
```

Expected: compile error — `ShoptetApiExpeditionListSource` constructor has no `ICarrierCoolingRepository` parameter and `ResolveCarrierCooling` doesn't exist.

- [ ] **Step 3.4: Update `ShoptetApiExpeditionListSource.cs`**

Replace the full file content:

```csharp
using System.Globalization;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Tests")]

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;

public class ShoptetApiExpeditionListSource : IPickingListSource
{
    // ShoptetOrderClient is the only implementation of IEshopOrderClient — safe to cast
    // within this adapter assembly to access expedition-specific methods not on the interface.
    private readonly ShoptetOrderClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ICatalogRepository _catalog;
    private readonly ICarrierCoolingRepository _carrierCooling;

    public ShoptetApiExpeditionListSource(
        IEshopOrderClient client,
        TimeProvider timeProvider,
        ICatalogRepository catalog,
        ICarrierCoolingRepository carrierCooling)
    {
        _client = (ShoptetOrderClient)client;
        _timeProvider = timeProvider;
        _catalog = catalog;
        _carrierCooling = carrierCooling;
    }

    public async Task<PrintPickingListResult> CreatePickingList(
        PrintPickingListRequest request,
        Func<IList<string>, Task>? onBatchFilesReady,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch all orders with the requested source state (paginate)
        var allOrders = await FetchAllOrdersAsync(request.SourceStateId, cancellationToken);

        // 2. Filter to carriers requested; group by carrier
        var carrierFilter = request.Carriers.Any()
            ? new HashSet<Carriers>(request.Carriers)
            : null;

        var ordersByCarrier = new Dictionary<Carriers, List<(string Code, string ShippingGuid)>>();
        foreach (var order in allOrders)
        {
            var shippingGuid = order.Shipping?.Guid;
            if (string.IsNullOrEmpty(shippingGuid) || !ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
                continue;
            if (carrierFilter != null && !carrierFilter.Contains(method.Carrier))
                continue;

            if (!ordersByCarrier.TryGetValue(method.Carrier, out var list))
            {
                list = new List<(string, string)>();
                ordersByCarrier[method.Carrier] = list;
            }

            list.Add((order.Code, shippingGuid));
        }

        var exportedFiles = new List<string>();
        var processedCodes = new List<string>();
        var timestamp = _timeProvider.GetFilenameTimestamp();

        // Load carrier cooling matrix once for the entire run
        var allSettings = await _carrierCooling.GetAllAsync(cancellationToken);
        var coolingMatrix = allSettings.ToDictionary(
            s => (s.Carrier, s.DeliveryHandling),
            s => s.Cooling);

        foreach (var (carrier, orders) in ordersByCarrier)
        {
            // Sort by shippingGuid so same method types are together
            var sorted = orders.OrderBy(o => o.ShippingGuid).ToList();

            // Determine batch limits from the first shipping method for this carrier
            var maxItems = ShippingMethodRegistry.ByGuid.TryGetValue(sorted[0].ShippingGuid, out var sm) ? sm.MaxItems : 20;
            var maxOrders = sm?.MaxOrders ?? int.MaxValue;
            var carrierDisplayName = carrier.ToString();

            // 3. Fetch all order details for this carrier upfront, then batch greedily by item count.
            //    This ensures batches are split based on how much content fits on a printed page,
            //    rather than by an arbitrary order count.
            var allExpeditionOrders = new List<ExpeditionOrder>();
            foreach (var (code, shippingGuid) in sorted)
            {
                var detail = await _client.GetExpeditionOrderDetailAsync(code, cancellationToken);
                var expeditionOrder = MapToExpeditionOrder(detail);
                expeditionOrder.CarrierCooling = ResolveCarrierCooling(shippingGuid, coolingMatrix);
                allExpeditionOrders.Add(expeditionOrder);
                processedCodes.Add(code);
            }

            // Greedy batching: accumulate orders until adding the next would exceed maxItems.
            // A single order with more items than maxItems always becomes its own batch.
            var currentBatch = new List<ExpeditionOrder>();
            var currentItemCount = 0;
            var batchIndex = 0;

            async Task FlushBatchAsync(List<ExpeditionOrder> batch)
            {
                // Enrich with stock counts, warehouse positions, and cooling from catalog.
                // Positions are only applied where the Shoptet API left them blank (set components).
                var productCodes = batch.SelectMany(o => o.Items).Select(i => i.ProductCode).Distinct();
                var stockByCode = new Dictionary<string, decimal>();
                var locationByCode = new Dictionary<string, string>();
                var coolingByCode = new Dictionary<string, Cooling>();
                foreach (var productCode in productCodes)
                {
                    var entry = await _catalog.GetByIdAsync(productCode, cancellationToken);
                    if (entry != null)
                    {
                        stockByCode[productCode] = entry.Stock.Eshop;
                        if (!string.IsNullOrEmpty(entry.Location))
                            locationByCode[productCode] = entry.Location;
                        coolingByCode[productCode] = entry.Properties.Cooling;
                    }
                }
                ApplyEnrichment(
                    batch.SelectMany(o => o.Items),
                    stockByCode,
                    locationByCode,
                    coolingByCode);

                var data = new ExpeditionProtocolData
                {
                    CarrierDisplayName = carrierDisplayName,
                    Orders = batch,
                };

                var pdfBytes = ExpeditionProtocolDocument.Generate(data);
                var fileName = $"{timestamp}_{carrier}_{batchIndex}.pdf";
                var filePath = Path.Combine(Path.GetTempPath(), fileName);
                await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);
                exportedFiles.Add(filePath);

                if (onBatchFilesReady != null)
                    await onBatchFilesReady(new List<string> { filePath });
            }

            foreach (var order in allExpeditionOrders)
            {
                var orderItemCount = order.Items.Count;

                if ((currentItemCount + orderItemCount > maxItems || currentBatch.Count >= maxOrders) && currentBatch.Count > 0)
                {
                    // Flush current batch before starting a new one
                    await FlushBatchAsync(currentBatch);
                    batchIndex++;
                    currentBatch = new List<ExpeditionOrder>();
                    currentItemCount = 0;
                }

                currentBatch.Add(order);
                currentItemCount += orderItemCount;
            }

            // Flush any remaining orders
            if (currentBatch.Count > 0)
            {
                await FlushBatchAsync(currentBatch);
            }
        }

        // 5. Update order states if requested
        if (request.ChangeOrderState)
        {
            foreach (var code in processedCodes)
                await _client.UpdateStatusAsync(code, request.DesiredStateId, cancellationToken);
        }

        return new PrintPickingListResult
        {
            ExportedFiles = exportedFiles,
            TotalCount = processedCodes.Count,
        };
    }

    internal static Cooling ResolveCarrierCooling(
        string shippingGuid,
        IReadOnlyDictionary<(Carriers, DeliveryHandling), Cooling> matrix)
    {
        if (!ShippingMethodRegistry.ByGuid.TryGetValue(shippingGuid, out var method))
            return Cooling.None;

        var handling = ShippingMethodRegistry.ResolveDeliveryHandling(method);
        if (!handling.HasValue)
            return Cooling.None;

        return matrix.TryGetValue((method.Carrier, handling.Value), out var cooling)
            ? cooling
            : Cooling.None;
    }

    private async Task<List<OrderSummary>> FetchAllOrdersAsync(int statusId, CancellationToken ct)
    {
        var all = new List<OrderSummary>();
        var page = 1;
        while (true)
        {
            var response = await _client.GetOrdersByStatusAsync(statusId, page, ct);
            all.AddRange(response.Data.Orders);

            if (page >= response.Data.Paginator.PageCount)
                break;
            page++;
        }
        return all;
    }

    private static ExpeditionOrder MapToExpeditionOrder(Model.ExpeditionOrderDetail detail)
    {
        var addr = detail.DeliveryAddress ?? detail.BillingAddress;
        var address = addr != null
            ? $"{addr.Street} {addr.HouseNumber}, {addr.Zip} {addr.City}".Trim()
            : string.Empty;

        var shipAddr = detail.DeliveryAddress ?? detail.BillingAddress;
        var customerName = !string.IsNullOrWhiteSpace(shipAddr?.FullName)
            ? shipAddr.FullName
            : !string.IsNullOrWhiteSpace(shipAddr?.Company)
                ? shipAddr.Company
                : !string.IsNullOrWhiteSpace(detail.FullName)
                    ? detail.FullName
                    : detail.Company ?? string.Empty;

        return new ExpeditionOrder
        {
            Code = detail.Code,
            CustomerName = customerName,
            Address = address,
            Phone = detail.Phone ?? string.Empty,
            CustomerRemark = detail.Notes?.CustomerRemark,
            EshopRemark = detail.Notes?.EshopRemark,
            Items = MapOrderItems(detail),
        };
    }

    internal static void ApplyEnrichment(
        IEnumerable<ExpeditionOrderItem> items,
        Dictionary<string, decimal> stockByCode,
        Dictionary<string, string> locationByCode,
        Dictionary<string, Cooling> coolingByCode)
    {
        foreach (var item in items)
        {
            if (stockByCode.TryGetValue(item.ProductCode, out var stock))
                item.StockCount = stock;
            if (string.IsNullOrEmpty(item.WarehousePosition) && locationByCode.TryGetValue(item.ProductCode, out var location))
                item.WarehousePosition = location;
            if (coolingByCode.TryGetValue(item.ProductCode, out var cooling))
                item.Cooling = cooling;
        }
    }

    internal static List<ExpeditionOrderItem> MapOrderItems(Model.ExpeditionOrderDetail detail)
    {
        var result = new List<ExpeditionOrderItem>();

        var setItemsByParentId = detail.Completion
            .Where(c => string.Equals(c.ItemType, "product-set-item", StringComparison.OrdinalIgnoreCase)
                     && c.ParentProductSetItemId.HasValue)
            .GroupBy(c => c.ParentProductSetItemId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in detail.Items)
        {
            if (string.Equals(item.ItemType, "product", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ItemType, "gift", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ExpeditionOrderItem
                {
                    ProductCode = item.Code ?? string.Empty,
                    Name = item.Name ?? string.Empty,
                    Variant = item.VariantName ?? string.Empty,
                    WarehousePosition = item.WarehousePosition ?? string.Empty,
                    Quantity = (int)(item.Amount ?? 0),
                    StockDemand = item.StockStatus?.AllDemand ?? 0,
                    Unit = item.Unit ?? string.Empty,
                    UnitPrice = decimal.TryParse(item.ItemPriceWithVat, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0m,
                });
            }
            else if (string.Equals(item.ItemType, "product-set", StringComparison.OrdinalIgnoreCase))
            {
                var setQuantity = (int)(item.Amount ?? 1);
                if (!setItemsByParentId.TryGetValue(item.ItemId, out var setComponents))
                    continue;

                foreach (var component in setComponents)
                {
                    result.Add(new ExpeditionOrderItem
                    {
                        ProductCode = component.Code ?? string.Empty,
                        Name = component.Name ?? string.Empty,
                        Variant = component.VariantName ?? string.Empty,
                        WarehousePosition = string.Empty,
                        Quantity = (int)(component.Amount ?? 0) * setQuantity,
                        Unit = component.Unit ?? string.Empty,
                        UnitPrice = 0m,
                        IsFromSet = true,
                        SetName = item.Name,
                    });
                }
            }
        }

        return result;
    }
}
```

- [ ] **Step 3.5: Run new tests to confirm GREEN**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "ResolveCarrierCooling|AssignsCarrierCooling|LoadsMatrixOnce" -v minimal 2>&1 | tail -20
```

Expected: all 6 new tests pass.

- [ ] **Step 3.6: Run full test suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v minimal 2>&1 | tail -10
```

Expected: all tests pass.

- [ ] **Step 3.7: Build and format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1/backend
dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj -v minimal 2>&1 | tail -5
dotnet format src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj --verify-no-changes 2>&1 | tail -5
```

Expected: build succeeds, format reports no changes. If format reports changes, run without `--verify-no-changes` to apply them, then re-check.

- [ ] **Step 3.8: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/auckland-v1
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
        backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs
git commit -m "feat: connect carrier cooling matrix to expedition list ribbon"
```
