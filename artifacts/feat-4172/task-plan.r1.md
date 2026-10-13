# PurchaseOrdersInTransitTile FormatAmountInThousands Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit tests that exercise every branch of `PurchaseOrdersInTransitTile.FormatAmountInThousands` (zero, integer-thousands, decimal-thousands, and the integer/decimal boundary) so the file's line coverage rises from 0.0% to at least the 60% threshold.

**Architecture:** `FormatAmountInThousands` is private and reachable only through `LoadDataAsync()`. Tests mock `IPurchaseOrderRepository.GetByStatusAsync` to return zero or one `PurchaseOrder` whose `TotalAmount` (a computed sum of line totals) equals each scenario's target amount, call `LoadDataAsync()`, and assert the serialized `data.formattedAmount` string — mirroring the existing sibling test `LowStockEfficiencyTileTests.cs` in the same directory. No production code changes.

**Tech Stack:** xUnit, Moq, FluentAssertions, System.Text.Json (all already referenced by `Anela.Heblo.Tests`).

---

### task: add-purchase-orders-in-transit-tile-format-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs`

No production files are created or modified — this task is test-only, against the existing, unmodified `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`.

- [ ] **Step 1: Write the new test file**

Create `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs` with the following complete content:

```csharp
using Anela.Heblo.Application.Features.Purchase.DashboardTiles;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase.DashboardTiles;

public class PurchaseOrdersInTransitTileTests
{
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock;
    private readonly PurchaseOrdersInTransitTile _tile;

    public PurchaseOrdersInTransitTileTests()
    {
        _repositoryMock = new Mock<IPurchaseOrderRepository>();
        _tile = new PurchaseOrdersInTransitTile(_repositoryMock.Object);
    }

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

    [Fact]
    public async Task LoadDataAsync_WithNoOrdersInTransit_ReturnsZeroNotZeroK()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetByStatusAsync(PurchaseOrderStatus.InTransit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("data").GetProperty("formattedAmount").GetString()
            .Should().Be("0");
    }

    [Theory]
    [InlineData(999, "1.0k")]
    [InlineData(1000, "1k")]
    [InlineData(1001, "1.0k")]
    [InlineData(1500, "1.5k")]
    [InlineData(5000, "5k")]
    [InlineData(9999, "10.0k")]
    [InlineData(10000, "10k")]
    [InlineData(999999, "1000.0k")]
    public async Task LoadDataAsync_FormatsAmountInThousands(int amount, string expectedFormattedAmount)
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
}
```

Note: `[InlineData]` takes `int amount` (not `decimal`) because xUnit's `InlineDataAttribute` cannot carry `decimal` constants (they are not valid C# attribute-argument types). The test method parameter is `int`, and `BuildOrderWithAmount(decimal amount)` receives it via the existing implicit `int → decimal` conversion — no explicit cast needed.

- [ ] **Step 2: Run the new tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PurchaseOrdersInTransitTileTests"`

Expected: All 9 tests (1 `Fact` + 8 `Theory` cases) PASS. `FormatAmountInThousands`'s existing implementation is not being changed, so no red/failing step is expected here — this is characterization coverage of existing, correct behavior per the spec's analysis of each boundary value.

If any case unexpectedly FAILs, do not "fix" the assertion to match — stop and compare the actual returned string against the spec's FR-2/FR-3/FR-4 worked examples (`artifacts/feat-4172/spec.r1.md`) to determine whether the test's expected value or the production code has the bug, and report it rather than silently adjusting the assertion.

- [ ] **Step 3: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet test`

Expected: All tests PASS (the new file only adds tests; it does not modify any existing test or production file).

- [ ] **Step 4: Format and build check**

Run: `cd backend && dotnet format && dotnet build`

Expected: no formatting diffs requiring changes beyond what `dotnet format` applies automatically, and a clean build with no errors.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs
git commit -m "test: cover FormatAmountInThousands boundary conditions in PurchaseOrdersInTransitTile"
```

---

## Spec Coverage Check

- FR-1 (zero branch) → `LoadDataAsync_WithNoOrdersInTransit_ReturnsZeroNotZeroK`.
- FR-2 (integer-thousands branch) → `LoadDataAsync_FormatsAmountInThousands` cases `1000 → "1k"`, `5000 → "5k"`, `10000 → "10k"`.
- FR-3 (decimal-thousands branch) → cases `1500 → "1.5k"`, `9999 → "10.0k"`, `999999 → "1000.0k"`.
- FR-4 (integer/decimal boundary) → cases `999 → "1.0k"`, `1000 → "1k"`, `1001 → "1.0k"`.
- FR-5 (test via public entry point) → both test methods drive `FormatAmountInThousands` exclusively through `LoadDataAsync()` against a mocked `IPurchaseOrderRepository`, per the architecture review's Decision 1 and Decision 2.
- NFR-1 (60% coverage threshold) → addressed by Step 2/3 verification; the risk analysis in `arch-review.r1.md` explains why line coverage clears the threshold.
- NFR-2 (no production code change) → confirmed: only one new test file is created; `PurchaseOrdersInTransitTile.cs` is untouched.
