using AutoMapper;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetManufactureOrderHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetManufactureOrderHandler _handler;

    private const int ValidOrderId = 1;
    private const int NonExistentOrderId = 999;

    public GetManufactureOrderHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _mapperMock = new Mock<IMapper>();

        _handler = new GetManufactureOrderHandler(
            _repositoryMock.Object,
            _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_WithExistingOrder_ShouldReturnOrderDto()
    {
        var request = new GetManufactureOrderRequest { Id = ValidOrderId };
        var order = CreateSampleOrder();
        var orderDto = CreateSampleOrderDto();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _mapperMock
            .Setup(x => x.Map<ManufactureOrderDto>(order))
            .Returns(orderDto);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Order.Should().NotBeNull();
        result.Order.Should().BeEquivalentTo(orderDto);
    }

    [Fact]
    public async Task Handle_WithNonExistentOrder_ShouldReturnResourceNotFoundError()
    {
        var request = new GetManufactureOrderRequest { Id = NonExistentOrderId };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(NonExistentOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        result.Params.Should().ContainKey("id");
        result.Params!["id"].Should().Be(NonExistentOrderId.ToString());
        result.Order.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldCallRepositoryWithCorrectId()
    {
        var request = new GetManufactureOrderRequest { Id = ValidOrderId };
        var order = CreateSampleOrder();
        var orderDto = CreateSampleOrderDto();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _mapperMock
            .Setup(x => x.Map<ManufactureOrderDto>(order))
            .Returns(orderDto);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldCallMapperWithOrder()
    {
        var request = new GetManufactureOrderRequest { Id = ValidOrderId };
        var order = CreateSampleOrder();
        var orderDto = CreateSampleOrderDto();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _mapperMock
            .Setup(x => x.Map<ManufactureOrderDto>(order))
            .Returns(orderDto);

        await _handler.Handle(request, CancellationToken.None);

        _mapperMock.Verify(
            x => x.Map<ManufactureOrderDto>(order),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrows_ShouldPropagateException()
    {
        var request = new GetManufactureOrderRequest { Id = ValidOrderId };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database error");
    }

    [Fact]
    public async Task Handle_WhenMapperThrows_ShouldPropagateException()
    {
        var request = new GetManufactureOrderRequest { Id = ValidOrderId };
        var order = CreateSampleOrder();

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _mapperMock
            .Setup(x => x.Map<ManufactureOrderDto>(order))
            .Throws(new AutoMapperMappingException("Mapping error"));

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<AutoMapperMappingException>()
            .WithMessage("Mapping error");
    }

    [Fact]
    public async Task Handle_WithValidOrder_ShouldNotCallMapperForNullOrder()
    {
        var request = new GetManufactureOrderRequest { Id = NonExistentOrderId };

        _repositoryMock
            .Setup(x => x.GetOrderByIdAsync(NonExistentOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        await _handler.Handle(request, CancellationToken.None);

        _mapperMock.Verify(
            x => x.Map<ManufactureOrderDto>(It.IsAny<ManufactureOrder>()),
            Times.Never);
    }

    private static ManufactureOrder CreateSampleOrder()
    {
        var order = new ManufactureOrder
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2024-001",
            CreatedDate = DateTime.UtcNow.AddDays(-2),
            CreatedByUser = "Test User",
            ResponsiblePerson = "John Doe",
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow.AddDays(-2),
            StateChangedByUser = "Test User"
        };

        order.SemiProduct = new ManufactureOrderSemiProduct
        {
            Id = 1,
            ProductCode = "SEMI001",
            ProductName = "Test Semi Product",
            PlannedQuantity = 1000m,
            ActualQuantity = 1000m
        };

        order.Products.Add(new ManufactureOrderProduct
        {
            Id = 1,
            ProductCode = "PROD001",
            ProductName = "Test Product 1",
            PlannedQuantity = 100m,
            ActualQuantity = 0m,
            SemiProductCode = "SEMI001"
        });

        order.Notes.Add(new ManufactureOrderNote
        {
            Id = 1,
            Text = "Test note",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedByUser = "Test User"
        });


        return order;
    }

    private static ManufactureOrderDto CreateSampleOrderDto()
    {
        return new ManufactureOrderDto
        {
            Id = ValidOrderId,
            OrderNumber = "MO-2024-001",
            CreatedDate = DateTime.UtcNow.AddDays(-2),
            CreatedByUser = "Test User",
            ResponsiblePerson = "John Doe",
            PlannedDate = DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            State = ManufactureOrderState.Draft,
            StateChangedAt = DateTime.UtcNow.AddDays(-2),
            StateChangedByUser = "Test User"
        };
    }
}