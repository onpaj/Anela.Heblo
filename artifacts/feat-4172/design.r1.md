# Design: PurchaseOrdersInTransitTile — FormatAmountInThousands test coverage

## Component Design

### `PurchaseOrdersInTransitTileTests` (new test class)
Location: `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs`
Namespace: `Anela.Heblo.Tests.Features.Purchase.DashboardTiles`

Responsibility: exercise `PurchaseOrdersInTransitTile.LoadDataAsync()` with controlled repository data so every branch of the private `FormatAmountInThousands` method executes, and assert the resulting `formattedAmount` string. Follows the exact structural shape of the existing sibling `LowStockEfficiencyTileTests`.

**Fields:**
- `Mock<IPurchaseOrderRepository> _repositoryMock`
- `PurchaseOrdersInTransitTile _tile` — constructed once per test instance (xUnit creates a fresh instance per test method) as `new PurchaseOrdersInTransitTile(_repositoryMock.Object)`.

**Test data helper:**
A private helper builds a single `PurchaseOrder` whose `TotalAmount` equals a given decimal:
```csharp
private static PurchaseOrder BuildOrderWithAmount(decimal amount)
{
    var order = new PurchaseOrder(
        orderNumber: "PO-TEST",
        supplierId: 1,
        supplierName: "Test Supplier",
        orderDate: DateTime.UtcNow,
        expectedDeliveryDate: null,
        contactVia: null,
        notes: null,
        createdBy: "test");

    order.AddLine(
        materialId: "MAT-1",
        materialName: "Test Material",
        quantity: 1,
        unitPrice: amount,
        notes: null,
        updatedBy: "test");

    return order;
}
```
`TotalAmount` is a computed property (`_lines.Sum(l => l.LineTotal)`); with `quantity = 1`, `LineTotal == unitPrice == amount`, so `TotalAmount == amount` exactly.

**Mock setup helper (or inline per test):**
```csharp
_repositoryMock
    .Setup(x => x.GetByStatusAsync(PurchaseOrderStatus.InTransit, It.IsAny<CancellationToken>()))
    .ReturnsAsync(orders); // orders: IEnumerable<PurchaseOrder>, empty or single-element
```

**Assertion helper:**
```csharp
var json = JsonSerializer.Serialize(result);
using var doc = JsonDocument.Parse(json);
doc.RootElement.GetProperty("data").GetProperty("formattedAmount").GetString()
    .Should().Be(expected);
```

### Test cases

| Test | Repository returns | `amount` | Expected `formattedAmount` | Covers |
|---|---|---|---|---|
| `LoadDataAsync_WithNoOrdersInTransit_ReturnsZeroNotZeroK` (Fact) | empty list | 0 (sum of nothing) | `"0"` | FR-1: zero branch |
| `LoadDataAsync_FormatsAmountInThousands` (Theory) | single order, `TotalAmount = amount` | 999 | `"1.0k"` | FR-4: just below boundary, decimal branch |
| ″ | ″ | 1000 | `"1k"` | FR-2 / FR-4: exact boundary, integer branch |
| ″ | ″ | 1001 | `"1.0k"` | FR-4: just above boundary, decimal branch |
| ″ | ″ | 1500 | `"1.5k"` | FR-3: mid-range decimal |
| ″ | ″ | 5000 | `"5k"` | FR-2: integer branch |
| ″ | ″ | 9999 | `"10.0k"` | FR-3: rounding near next integer thousand |
| ″ | ″ | 10000 | `"10k"` | FR-2: larger integer branch |
| ″ | ″ | 999999 | `"1000.0k"` | FR-3: large value |

The Theory uses `[InlineData(999, "1.0k")]` etc. against a single test method with parameters `(decimal amount, string expected)`.

**Sample Theory test body:**
```csharp
[Theory]
[InlineData(999, "1.0k")]
[InlineData(1000, "1k")]
[InlineData(1001, "1.0k")]
[InlineData(1500, "1.5k")]
[InlineData(5000, "5k")]
[InlineData(9999, "10.0k")]
[InlineData(10000, "10k")]
[InlineData(999999, "1000.0k")]
public async Task LoadDataAsync_FormatsAmountInThousands(decimal amount, string expectedFormattedAmount)
{
    // Arrange
    _repositoryMock
        .Setup(x => x.GetByStatusAsync(PurchaseOrderStatus.InTransit, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PurchaseOrder> { BuildOrderWithAmount(amount) });

    // Act
    var result = await _tile.LoadDataAsync();

    // Assert
    var json = JsonSerializer.Serialize(result);
    using var doc = JsonDocument.Parse(json);
    doc.RootElement.GetProperty("data").GetProperty("formattedAmount").GetString()
        .Should().Be(expectedFormattedAmount);
}
```

Note: `xunit` cannot take a `decimal` literal directly in `[InlineData]` in older language/xUnit combinations without an explicit cast issue — if this arises, use `double` or `int` in `InlineData` and cast to `decimal` inside the method body (`(decimal)amount`), matching whatever pattern already compiles cleanly elsewhere in the test project. This is an implementation-time detail the developer resolves against the actual compiler output, not a design decision.

## Data Schemas

No new or changed data schemas. The design reuses the existing `LoadDataAsync` anonymous return shape verbatim:

```
{
  status: "success",
  data: {
    count: number,
    totalAmount: decimal,
    formattedAmount: string   // <- field under test
  },
  metadata: { lastUpdated: DateTime, source: string },
  drillDown: { filters: { state: "InTransit" }, enabled: bool, tooltip: string }
}
```

No request/response DTOs are introduced (tile output is an anonymous object serialized directly, as with all other dashboard tiles). No database schema involved — `PurchaseOrder`/`PurchaseOrderLine` are existing domain entities used here only as in-memory test fixtures via the mocked repository.
