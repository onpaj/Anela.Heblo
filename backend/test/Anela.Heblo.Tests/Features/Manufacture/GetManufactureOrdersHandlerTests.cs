using AutoMapper;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetManufactureOrdersHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetManufactureOrdersHandler _handler;

    public GetManufactureOrdersHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _mapperMock = new Mock<IMapper>();
        
        _handler = new GetManufactureOrdersHandler(
            _repositoryMock.Object,
            _mapperMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldReturnOrdersList()
    {
        var request = new GetManufactureOrdersRequest();
        var orders = CreateSampleOrders();
        var orderDtos = CreateSampleOrderDtos();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Orders.Should().HaveCount(2);
        result.Orders.Should().BeEquivalentTo(orderDtos);
    }

    [Fact]
    public async Task Handle_WithStateFilter_ShouldPassStateToRepository()
    {
        var request = new GetManufactureOrdersRequest
        {
            State = ManufactureOrderState.SemiProductManufactured
        };
        
        var orders = CreateSampleOrders();
        var orderDtos = CreateSampleOrderDtos();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                ManufactureOrderState.SemiProductManufactured,
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GetOrdersAsync(
                ManufactureOrderState.SemiProductManufactured,
                null,
                null,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithDateRange_ShouldPassDatesToRepository()
    {
        var dateFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var dateTo = DateOnly.FromDateTime(DateTime.Today);
        
        var request = new GetManufactureOrdersRequest
        {
            DateFrom = dateFrom,
            DateTo = dateTo
        };
        
        var orders = CreateSampleOrders();
        var orderDtos = CreateSampleOrderDtos();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                dateFrom,
                dateTo,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GetOrdersAsync(
                null,
                dateFrom,
                dateTo,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithResponsiblePersonFilter_ShouldPassPersonToRepository()
    {
        var responsiblePerson = "John Doe";
        var request = new GetManufactureOrdersRequest
        {
            ResponsiblePerson = responsiblePerson
        };
        
        var orders = CreateSampleOrders();
        var orderDtos = CreateSampleOrderDtos();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                responsiblePerson,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GetOrdersAsync(
                null,
                null,
                null,
                responsiblePerson,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithOrderNumberFilter_ShouldPassOrderNumberToRepository()
    {
        var orderNumber = "MO-2024-001";
        var request = new GetManufactureOrdersRequest
        {
            OrderNumber = orderNumber
        };
        
        var orders = CreateSampleOrders();
        var orderDtos = CreateSampleOrderDtos();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<string?>(),
                orderNumber,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GetOrdersAsync(
                null,
                null,
                null,
                null,
                orderNumber,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithProductCodeFilter_ShouldPassProductCodeToRepository()
    {
        var productCode = "SEMI001";
        var request = new GetManufactureOrdersRequest
        {
            ProductCode = productCode
        };
        
        var orders = CreateSampleOrders();
        var orderDtos = CreateSampleOrderDtos();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                productCode,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GetOrdersAsync(
                null,
                null,
                null,
                null,
                null,
                productCode,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithAllFilters_ShouldPassAllParametersToRepository()
    {
        var request = new GetManufactureOrdersRequest
        {
            State = ManufactureOrderState.Completed,
            DateFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(-30)),
            DateTo = DateOnly.FromDateTime(DateTime.Today),
            ResponsiblePerson = "Jane Doe",
            OrderNumber = "MO-2024-002",
            ProductCode = "SEMI002"
        };
        
        var orders = CreateSampleOrders();
        var orderDtos = CreateSampleOrderDtos();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                request.State,
                request.DateFrom,
                request.DateTo,
                request.ResponsiblePerson,
                request.OrderNumber,
                request.ProductCode,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(
            x => x.GetOrdersAsync(
                request.State,
                request.DateFrom,
                request.DateTo,
                request.ResponsiblePerson,
                request.OrderNumber,
                request.ProductCode,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithEmptyResult_ShouldReturnEmptyList()
    {
        var request = new GetManufactureOrdersRequest();
        var orders = new List<ManufactureOrder>();
        var orderDtos = new List<ManufactureOrderDto>();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Returns(orderDtos);

        var result = await _handler.Handle(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenRepositoryThrows_ShouldPropagateException()
    {
        var request = new GetManufactureOrdersRequest();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database error");
    }

    [Fact]
    public async Task Handle_WhenMapperThrows_ShouldPropagateException()
    {
        var request = new GetManufactureOrdersRequest();
        var orders = CreateSampleOrders();

        _repositoryMock
            .Setup(x => x.GetOrdersAsync(
                It.IsAny<ManufactureOrderState?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        _mapperMock
            .Setup(x => x.Map<List<ManufactureOrderDto>>(orders))
            .Throws(new AutoMapperMappingException("Mapping error"));

        var action = async () => await _handler.Handle(request, CancellationToken.None);

        await action.Should().ThrowAsync<AutoMapperMappingException>()
            .WithMessage("Mapping error");
    }

    private static List<ManufactureOrder> CreateSampleOrders()
    {
        return new List<ManufactureOrder>
        {
            new ManufactureOrder
            {
                Id = 1,
                OrderNumber = "MO-2024-001",
                CreatedDate = DateTime.UtcNow.AddDays(-5),
                CreatedByUser = "User1",
                ResponsiblePerson = "John Doe",
                State = ManufactureOrderState.Draft,
                StateChangedAt = DateTime.UtcNow.AddDays(-5),
                StateChangedByUser = "User1"
            },
            new ManufactureOrder
            {
                Id = 2,
                OrderNumber = "MO-2024-002",
                CreatedDate = DateTime.UtcNow.AddDays(-3),
                CreatedByUser = "User2",
                ResponsiblePerson = "Jane Doe",
                State = ManufactureOrderState.SemiProductManufactured,
                StateChangedAt = DateTime.UtcNow.AddDays(-2),
                StateChangedByUser = "User2"
            }
        };
    }

    private static List<ManufactureOrderDto> CreateSampleOrderDtos()
    {
        return new List<ManufactureOrderDto>
        {
            new ManufactureOrderDto
            {
                Id = 1,
                OrderNumber = "MO-2024-001",
                CreatedDate = DateTime.UtcNow.AddDays(-5),
                CreatedByUser = "User1",
                ResponsiblePerson = "John Doe",
                State = ManufactureOrderState.Draft
            },
            new ManufactureOrderDto
            {
                Id = 2,
                OrderNumber = "MO-2024-002",
                CreatedDate = DateTime.UtcNow.AddDays(-3),
                CreatedByUser = "User2",
                ResponsiblePerson = "Jane Doe",
                State = ManufactureOrderState.SemiProductManufactured
            }
        };
    }
}