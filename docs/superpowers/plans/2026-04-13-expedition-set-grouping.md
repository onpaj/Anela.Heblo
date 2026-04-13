# Expedition Set Grouping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In the expedition print PDF, group set component items under a labelled sub-header row per set in each order's table; set items remain in the aggregated summary list unchanged.

**Architecture:** Add `SetName` to the data model, populate it during order mapping, and render a spanning header row before each set group in the order table. The summary page is untouched.

**Tech Stack:** C# / .NET 8, QuestPDF (PDF rendering), xUnit + FluentAssertions (tests)

---

### Task 1: Add `SetName` to `ExpeditionOrderItem`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs`

- [ ] **Step 1: Add the property**

Open `ExpeditionProtocolData.cs`. Add `SetName` after `IsFromSet`:

```csharp
public bool IsFromSet { get; set; }
public string? SetName { get; set; }
```

Full file after change:

```csharp
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
    public List<ExpeditionOrderItem> Items { get; set; } = new();
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
}
```

- [ ] **Step 2: Build to confirm no errors**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs
git commit -m "feat: add SetName property to ExpeditionOrderItem"
```

---

### Task 2: Populate `SetName` in the order mapper

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/MapOrderItemsTests.cs`

The method `MapOrderItems` is `private static`. To make it testable, change its access modifier to `internal static` and add `[assembly: InternalsVisibleTo]` to the adapter project.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/MapOrderItemsTests.cs`:

```csharp
using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;
using FluentAssertions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class MapOrderItemsTests
{
    [Fact]
    public void MapOrderItems_WithProductSet_SetsSetNameOnComponents()
    {
        var detail = new ExpeditionOrderDetail
        {
            Code = "0001",
            Items =
            [
                new ExpeditionOrderItemDto
                {
                    ItemType = "product-set",
                    ItemId = 10,
                    Name = "Starter Kit",
                    Amount = 1,
                },
            ],
            Completion =
            [
                new ExpeditionCompletionItemDto
                {
                    ItemType = "product-set-item",
                    ItemId = 101,
                    ParentProductSetItemId = 10,
                    Code = "SKU-A",
                    Name = "Component A",
                    Amount = 2,
                },
                new ExpeditionCompletionItemDto
                {
                    ItemType = "product-set-item",
                    ItemId = 102,
                    ParentProductSetItemId = 10,
                    Code = "SKU-B",
                    Name = "Component B",
                    Amount = 1,
                },
            ],
        };

        var items = ShoptetApiExpeditionListSource.MapOrderItems(detail);

        items.Should().HaveCount(2);
        items.Should().AllSatisfy(i => i.SetName.Should().Be("Starter Kit"));
        items.Should().AllSatisfy(i => i.IsFromSet.Should().BeTrue());
    }

    [Fact]
    public void MapOrderItems_WithRegularProduct_HasNullSetName()
    {
        var detail = new ExpeditionOrderDetail
        {
            Code = "0002",
            Items =
            [
                new ExpeditionOrderItemDto
                {
                    ItemType = "product",
                    ItemId = 20,
                    Code = "SKU-X",
                    Name = "Regular Product",
                    Amount = 3,
                },
            ],
            Completion = [],
        };

        var items = ShoptetApiExpeditionListSource.MapOrderItems(detail);

        items.Should().HaveCount(1);
        items[0].SetName.Should().BeNull();
        items[0].IsFromSet.Should().BeFalse();
    }

    [Fact]
    public void MapOrderItems_WithMultipleSets_SetsCorrectSetNamePerGroup()
    {
        var detail = new ExpeditionOrderDetail
        {
            Code = "0003",
            Items =
            [
                new ExpeditionOrderItemDto { ItemType = "product-set", ItemId = 10, Name = "Kit Alpha", Amount = 1 },
                new ExpeditionOrderItemDto { ItemType = "product-set", ItemId = 20, Name = "Kit Beta",  Amount = 2 },
            ],
            Completion =
            [
                new ExpeditionCompletionItemDto { ItemType = "product-set-item", ItemId = 101, ParentProductSetItemId = 10, Code = "A1", Name = "Alpha Part", Amount = 1 },
                new ExpeditionCompletionItemDto { ItemType = "product-set-item", ItemId = 201, ParentProductSetItemId = 20, Code = "B1", Name = "Beta Part",  Amount = 3 },
            ],
        };

        var items = ShoptetApiExpeditionListSource.MapOrderItems(detail);

        items.Should().HaveCount(2);
        items.Single(i => i.ProductCode == "A1").SetName.Should().Be("Kit Alpha");
        items.Single(i => i.ProductCode == "B1").SetName.Should().Be("Kit Beta");
        // Kit Beta has quantity 2 (set) * 3 (component) = 6
        items.Single(i => i.ProductCode == "B1").Quantity.Should().Be(6);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~MapOrderItemsTests" -v normal
```

Expected: FAIL — `MapOrderItems` is not accessible (`private`).

- [ ] **Step 3: Make `MapOrderItems` internal**

In `ShoptetApiExpeditionListSource.cs`, change the `MapOrderItems` signature from `private static` to `internal static`:

```csharp
internal static List<ExpeditionOrderItem> MapOrderItems(Model.ExpeditionOrderDetail detail)
```

- [ ] **Step 4: Add `InternalsVisibleTo` to the adapter project**

In `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`, add at the top of the file (below the existing `using` statements, before the `namespace`):

```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anela.Heblo.Adapters.Shoptet.Tests")]
```

The file top should look like:

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

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition;
```

- [ ] **Step 5: Populate `SetName` in the `product-set` branch of `MapOrderItems`**

Find this block in `MapOrderItems` (around line 263-274):

```csharp
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
    });
}
```

Replace with:

```csharp
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
        SetName = item.Name ?? string.Empty,
    });
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "FullyQualifiedName~MapOrderItemsTests" -v normal
```

Expected: 3 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs \
        backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/MapOrderItemsTests.cs
git commit -m "feat: populate SetName on set component items in expedition order mapper"
```

---

### Task 3: Render set sub-header rows in the PDF order table

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

The order section currently iterates `order.Items` directly. We need to split items into regular and set groups, then render a spanning header row before each set group.

- [ ] **Step 1: Replace the item-rendering loop in the order table**

In `ExpeditionProtocolDocument.cs`, find the loop inside the order table (starts at line ~119):

```csharp
foreach (var item in order.Items)
{
    Func<IContainer, IContainer> cell = item.IsFromSet ? SetDataCell : DataCell;
    Func<IContainer, IContainer> centeredCell = item.IsFromSet ? SetCenteredDataCell : CenteredDataCell;

    table.Cell().Element(cell).Text(item.ProductCode);
    table.Cell().Element(cell).Text(item.Name);
    table.Cell().Element(cell).Text(FormatVariant(item.Variant)).FontSize(8);

    table.Cell().Element(centeredCell)
        .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(11).Bold();
    table.Cell().Element(centeredCell)
        .Text(item.WarehousePosition ?? string.Empty).FontSize(8);
    table.Cell().Element(centeredCell)
        .Text(item.StockCount.ToString("0.##"));
}
```

Replace the entire `foreach` with:

```csharp
static IContainer SetHeaderCell(IContainer c) =>
    c.Border(0.5f).BorderColor(Colors.Grey.Lighten1)
     .Background(Colors.Blue.Lighten3)
     .Padding(2);

var regularItems = order.Items.Where(i => i.SetName == null).ToList();
var setGroups = order.Items
    .Where(i => i.SetName != null)
    .GroupBy(i => i.SetName!)
    .ToList();

foreach (var item in regularItems)
{
    table.Cell().Element(DataCell).Text(item.ProductCode);
    table.Cell().Element(DataCell).Text(item.Name);
    table.Cell().Element(DataCell).Text(FormatVariant(item.Variant)).FontSize(8);
    table.Cell().Element(CenteredDataCell)
        .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(11).Bold();
    table.Cell().Element(CenteredDataCell)
        .Text(item.WarehousePosition ?? string.Empty).FontSize(8);
    table.Cell().Element(CenteredDataCell)
        .Text(item.StockCount.ToString("0.##"));
}

foreach (var group in setGroups)
{
    // Sub-header spanning all 6 columns
    table.Cell().ColumnSpan(6).Element(SetHeaderCell)
        .Text($"Sada: {group.Key}").Bold().FontSize(8);

    foreach (var item in group)
    {
        table.Cell().Element(SetDataCell).Text(item.ProductCode);
        table.Cell().Element(SetDataCell).Text(item.Name);
        table.Cell().Element(SetDataCell).Text(FormatVariant(item.Variant)).FontSize(8);
        table.Cell().Element(SetCenteredDataCell)
            .Text(FormatAmount(item.Quantity, item.Unit)).FontSize(11).Bold();
        table.Cell().Element(SetCenteredDataCell)
            .Text(item.WarehousePosition ?? string.Empty).FontSize(8);
        table.Cell().Element(SetCenteredDataCell)
            .Text(item.StockCount.ToString("0.##"));
    }
}
```

- [ ] **Step 2: Build to confirm no errors**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run dotnet format**

```bash
cd backend && dotnet format src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Anela.Heblo.Adapters.ShoptetApi.csproj
```

Expected: No output (already formatted) or minor whitespace fixes.

- [ ] **Step 4: Run all unit tests in the adapter test project**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj \
  --filter "Category!=Integration" -v normal
```

Expected: All tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs
git commit -m "feat: group set items under labelled sub-header rows in expedition order table"
```
