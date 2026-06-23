# Task Plan: GetCalendarViewHandler Unit Tests

Feature: feat-3282 — Coverage Gap: Manufacture GetCalendarViewHandler

## Overview

Write 13 unit tests for `GetCalendarViewHandler` in a new file at:
`backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/GetCalendarViewHandlerTests.cs`

---

### task: write-calendar-view-handler-tests

**Goal:** Create the test file with all 13 test cases covering date boundary filtering, cancelled-order exclusion, SemiProduct/Products mapping, sorting, and error handling.

**Step 1 — Create the directory**

```
backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/
```

This directory does not exist yet. Create it before writing the file.

**Step 2 — Write the test file**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/UseCases/GetCalendarView/GetCalendarViewHandlerTests.cs` with the exact content below:

```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.UseCases.GetCalendarView;

public class GetCalendarViewHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly GetCalendarViewHandler _handler;

    // Fixed date range used across all tests
    private static readonly DateTime StartDate = new DateTime(2025, 6, 1);
    private static readonly DateTime EndDate = new DateTime(2025, 6, 30);

    public GetCalendarViewHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _handler = new GetCalendarViewHandler(
            _repositoryMock.Object,
            Mock.Of<ILogger<GetCalendarViewHandler>>());
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ManufactureOrder CreateOrder(
        int id,
        string orderNumber,
        DateOnly plannedDate,
        ManufactureOrderState state = ManufactureOrderState.Planned)
    {
        var order = new ManufactureOrder
        {
            Id = id,
            OrderNumber = orderNumber,
            PlannedDate = plannedDate
        };
        order.InitializeState(state, DateTime.UtcNow, "Test User");
        return order;
    }

    private void SetupRepository(List<ManufactureOrder> orders)
    {
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
    }

    private static GetCalendarViewRequest BuildRequest() =>
        new GetCalendarViewRequest { StartDate = StartDate, EndDate = EndDate };

    // ---------------------------------------------------------------------------
    // Cancelled-order filtering
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithCancelledOrderInRange_ExcludesCancelledOrder()
    {
        // Arrange
        var plannedOrder = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15), ManufactureOrderState.Planned);
        var cancelledOrder = CreateOrder(2, "MO-2025-002", new DateOnly(2025, 6, 16), ManufactureOrderState.Cancelled);
        SetupRepository(new List<ManufactureOrder> { plannedOrder, cancelledOrder });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
        result.Events[0].Id.Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // Date boundary inclusion
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithOrderOnStartDateBoundary_IncludesEvent()
    {
        // Arrange — PlannedDate == StartDate (June 1)
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 1));
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithOrderOnEndDateBoundary_IncludesEvent()
    {
        // Arrange — PlannedDate == EndDate (June 30)
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 30));
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
    }

    // ---------------------------------------------------------------------------
    // Date boundary exclusion
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithOrderBeforeStartDate_ExcludesEvent()
    {
        // Arrange — PlannedDate == May 31 (one day before StartDate)
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 5, 31));
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithOrderAfterEndDate_ExcludesEvent()
    {
        // Arrange — PlannedDate == July 1 (one day after EndDate)
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 7, 1));
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Events.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // SemiProduct — null case
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithNullSemiProduct_SetsEventSemiProductToNull()
    {
        // Arrange — SemiProduct is null; Title should fall back to OrderNumber
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.SemiProduct = null;
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Events.Should().HaveCount(1);
        var ev = result.Events[0];
        ev.SemiProduct.Should().BeNull();
        ev.Title.Should().Be(order.OrderNumber);
    }

    // ---------------------------------------------------------------------------
    // SemiProduct — title suffix stripping
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithSemiProductContainingSuffix_StripsProductNameSuffix()
    {
        // Arrange
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SEMI001",
            ProductName = "Argan Cream - meziprodukt",
            PlannedQuantity = 500m,
            BatchMultiplier = 1m
        };
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Events[0].Title.Should().Be("Argan Cream");
    }

    [Fact]
    public async Task Handle_WithSemiProductWithoutSuffix_LeavesProductNameUnchanged()
    {
        // Arrange
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SEMI002",
            ProductName = "Plain Name",
            PlannedQuantity = 200m,
            BatchMultiplier = 1m
        };
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Events[0].Title.Should().Be("Plain Name");
    }

    // ---------------------------------------------------------------------------
    // SemiProduct — DTO field mapping
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithNonNullSemiProduct_MapsSemiProductDtoCorrectly()
    {
        // Arrange
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SEMI003",
            ProductName = "Rose Hip Oil - meziprodukt",
            PlannedQuantity = 750m,
            ActualQuantity = 800m,
            BatchMultiplier = 2.5m
        };
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Events.Should().HaveCount(1);
        var sp = result.Events[0].SemiProduct;
        sp.Should().NotBeNull();
        sp!.ProductCode.Should().Be("SEMI003");
        sp.ProductName.Should().Be("Rose Hip Oil - meziprodukt");
        sp.PlannedQuantity.Should().Be(750m);
        sp.ActualQuantity.Should().Be(800m);
        sp.BatchMultiplier.Should().Be(2.5m);
    }

    // ---------------------------------------------------------------------------
    // Products — null collection
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithNullProducts_SetsEventProductsToEmptyList()
    {
        // Arrange — Products must be explicitly set to null! because the
        // property default is new List<ManufactureOrderProduct>()
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.Products = null!;
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Events.Should().HaveCount(1);
        var ev = result.Events[0];
        ev.Products.Should().NotBeNull();
        ev.Products.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // Products — DTO field mapping
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithNonNullProducts_MapsProductDtosCorrectly()
    {
        // Arrange
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.Products.Add(new ManufactureOrderProduct
        {
            ProductCode = "PROD001",
            ProductName = "Product Alpha",
            PlannedQuantity = 100m,
            ActualQuantity = 95m,
            SemiProductCode = "SEMI001"
        });
        order.Products.Add(new ManufactureOrderProduct
        {
            ProductCode = "PROD002",
            ProductName = "Product Beta",
            PlannedQuantity = 200m,
            ActualQuantity = null,
            SemiProductCode = "SEMI001"
        });
        SetupRepository(new List<ManufactureOrder> { order });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Events.Should().HaveCount(1);
        var products = result.Events[0].Products;
        products.Should().HaveCount(2);

        products[0].ProductCode.Should().Be("PROD001");
        products[0].ProductName.Should().Be("Product Alpha");
        products[0].PlannedQuantity.Should().Be(100m);
        products[0].ActualQuantity.Should().Be(95m);

        products[1].ProductCode.Should().Be("PROD002");
        products[1].ProductName.Should().Be("Product Beta");
        products[1].PlannedQuantity.Should().Be(200m);
        products[1].ActualQuantity.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Error handling
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenRepositoryThrows_ReturnsInternalServerError()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection lost"));

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert — handler must swallow the exception and return an error response
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    // ---------------------------------------------------------------------------
    // Sorting
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithMultipleOrdersAtDifferentDates_ReturnsSortedByDateAscending()
    {
        // Arrange — deliberately add later date first to verify sort
        var laterOrder = CreateOrder(2, "MO-2025-002", new DateOnly(2025, 6, 20));
        var earlierOrder = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 5));
        SetupRepository(new List<ManufactureOrder> { laterOrder, earlierOrder });

        // Act
        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        // Assert
        result.Events.Should().HaveCount(2);
        result.Events[0].Date.Should().BeBefore(result.Events[1].Date);
    }
}
```

**Step 3 — Verify**

Run the following command from the repository root to confirm all 13 tests pass and none are skipped:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetCalendarViewHandlerTests" \
  --no-build \
  --verbosity normal
```

If `--no-build` fails (first run), drop it:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetCalendarViewHandlerTests" \
  --verbosity normal
```

Expected output: **13 passed, 0 failed, 0 skipped.**

Also run the full build and format check to satisfy project validation requirements:

```bash
dotnet build backend/Anela.Heblo.sln && dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

**Key implementation notes:**

- `InitializeState(state, DateTime.UtcNow, "Test User")` is the only valid way to set `State` on `ManufactureOrder` (the setter is `internal`).
- `order.Products = null!` is required for the null-Products test because the property defaults to `new List<ManufactureOrderProduct>()`.
- Repository is mocked with `It.IsAny<DateOnly>()` for both date arguments — the handler converts `DateTime` → `DateOnly` internally, so exact-value matching would couple the test to that conversion detail unnecessarily.
- The test namespace `Anela.Heblo.Tests.Features.Manufacture.UseCases.GetCalendarView` matches the directory structure required by the project conventions (see `docs/architecture/filesystem.md`).
- No AutoMapper mock needed — the handler builds DTOs by hand.
