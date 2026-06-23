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

    private static readonly DateTime StartDate = new DateTime(2025, 6, 1);
    private static readonly DateTime EndDate = new DateTime(2025, 6, 30);

    public GetCalendarViewHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _handler = new GetCalendarViewHandler(
            _repositoryMock.Object,
            Mock.Of<ILogger<GetCalendarViewHandler>>());
    }

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
        order.InitializeState(state, new DateTime(2025, 6, 1), "Test User");
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

    [Fact]
    public async Task Handle_WithCancelledOrderInRange_ExcludesCancelledOrder()
    {
        var plannedOrder = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15), ManufactureOrderState.Planned);
        var cancelledOrder = CreateOrder(2, "MO-2025-002", new DateOnly(2025, 6, 16), ManufactureOrderState.Cancelled);
        SetupRepository(new List<ManufactureOrder> { plannedOrder, cancelledOrder });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
        result.Events[0].Id.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithOrderOnStartDateBoundary_IncludesEvent()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 1));
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithOrderOnEndDateBoundary_IncludesEvent()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 30));
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithOrderBeforeStartDate_ExcludesEvent()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 5, 31));
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithOrderAfterEndDate_ExcludesEvent()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 7, 1));
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNullSemiProduct_SetsEventSemiProductToNull()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.SemiProduct = null;
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
        var ev = result.Events[0];
        ev.SemiProduct.Should().BeNull();
        ev.Title.Should().Be(order.OrderNumber);
    }

    [Fact]
    public async Task Handle_WithSemiProductContainingSuffix_StripsProductNameSuffix()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SEMI001",
            ProductName = "Argan Cream - meziprodukt",
            PlannedQuantity = 500m,
            BatchMultiplier = 1m
        };
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events[0].Title.Should().Be("Argan Cream");
    }

    [Fact]
    public async Task Handle_WithSemiProductWithoutSuffix_LeavesProductNameUnchanged()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SEMI002",
            ProductName = "Plain Name",
            PlannedQuantity = 200m,
            BatchMultiplier = 1m
        };
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events[0].Title.Should().Be("Plain Name");
    }

    [Fact]
    public async Task Handle_WithNonNullSemiProduct_MapsSemiProductDtoCorrectly()
    {
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

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
        var sp = result.Events[0].SemiProduct;
        sp.Should().NotBeNull();
        sp!.ProductCode.Should().Be("SEMI003");
        sp.ProductName.Should().Be("Rose Hip Oil - meziprodukt");
        sp.PlannedQuantity.Should().Be(750m);
        sp.ActualQuantity.Should().Be(800m);
        sp.BatchMultiplier.Should().Be(2.5m);
    }

    [Fact]
    public async Task Handle_WithNullProducts_SetsEventProductsToEmptyList()
    {
        var order = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 15));
        order.Products = null!;
        SetupRepository(new List<ManufactureOrder> { order });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(1);
        var ev = result.Events[0];
        ev.Products.Should().NotBeNull();
        ev.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNonNullProducts_MapsProductDtosCorrectly()
    {
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

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
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

    [Fact]
    public async Task Handle_WhenRepositoryThrows_ReturnsInternalServerError()
    {
        _repositoryMock
            .Setup(r => r.GetOrdersForDateRangeAsync(
                It.IsAny<DateOnly>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection lost"));

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
    }

    [Fact]
    public async Task Handle_WithMultipleOrdersAtDifferentDates_ReturnsSortedByDateAscending()
    {
        var laterOrder = CreateOrder(2, "MO-2025-002", new DateOnly(2025, 6, 20));
        var earlierOrder = CreateOrder(1, "MO-2025-001", new DateOnly(2025, 6, 5));
        SetupRepository(new List<ManufactureOrder> { laterOrder, earlierOrder });

        var result = await _handler.Handle(BuildRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Events.Should().HaveCount(2);
        result.Events[0].Date.Should().BeBefore(result.Events[1].Date);
    }
}
