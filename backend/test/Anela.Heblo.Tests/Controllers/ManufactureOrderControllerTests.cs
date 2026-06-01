using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Application.Features.Manufacture.UseCases.DuplicateManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

/// <summary>
/// Unit tests for ManufactureOrderController using mocked dependencies
/// These tests focus on controller logic, request/response handling, and validation
/// </summary>
public class ManufactureOrderControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ManufactureOrderController _controller;

    public ManufactureOrderControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new ManufactureOrderController(_mediatorMock.Object);

        // Setup HttpContext for BaseApiController.Logger
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = serviceProvider;

        _controller.ControllerContext = new ControllerContext()
        {
            HttpContext = httpContext
        };
    }

    #region GetOrders Tests

    [Fact]
    public async Task GetOrders_Should_Return_Ok_With_Response()
    {
        // Arrange
        var request = new GetManufactureOrdersRequest
        {
            State = ManufactureOrderState.Planned,
            DateFrom = DateOnly.FromDateTime(DateTime.Now.AddDays(-30)),
            DateTo = DateOnly.FromDateTime(DateTime.Now),
            ResponsiblePerson = "test@anela.cz",
            OrderNumber = "MO-2024-001",
            ProductCode = "PROD001"
        };

        var expectedResponse = new GetManufactureOrdersResponse
        {
            Orders = new List<ManufactureOrderDto>
            {
                new ManufactureOrderDto
                {
                    Id = 1,
                    OrderNumber = "MO-2024-001",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByUser = "test@anela.cz",
                    ResponsiblePerson = "test@anela.cz",
                    PlannedDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
                    State = ManufactureOrderState.Planned,
                    StateChangedAt = DateTime.UtcNow,
                    StateChangedByUser = "test@anela.cz",
                    SemiProduct = new ManufactureOrderSemiProductDto
                    {
                        Id = 1,
                        ProductCode = "SEMI001",
                        ProductName = "Semi Product",
                        PlannedQuantity = 100m,
                        ActualQuantity = 0m,
                        BatchMultiplier = 1.0m,
                        ExpirationMonths = 12
                    },
                    Products = new List<ManufactureOrderProductDto>
                    {
                        new ManufactureOrderProductDto
                        {
                            Id = 1,
                            ProductCode = "PROD001",
                            ProductName = "Final Product",
                            SemiProductCode = "SEMI001",
                            PlannedQuantity = 50m,
                            ActualQuantity = 0m
                        }
                    },
                    Notes = new List<ManufactureOrderNoteDto>()
                }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetOrders(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufactureOrdersResponse>();

        response.Subject.Orders.Should().HaveCount(1);
        response.Subject.Orders[0].Id.Should().Be(1);
        response.Subject.Orders[0].OrderNumber.Should().Be("MO-2024-001");
        response.Subject.Orders[0].State.Should().Be(ManufactureOrderState.Planned);

        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrders_Should_Handle_Empty_Response()
    {
        // Arrange
        var request = new GetManufactureOrdersRequest();
        var expectedResponse = new GetManufactureOrdersResponse
        {
            Orders = new List<ManufactureOrderDto>()
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetOrders(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufactureOrdersResponse>();

        response.Subject.Orders.Should().BeEmpty();
        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft)]
    [InlineData(ManufactureOrderState.Planned)]
    [InlineData(ManufactureOrderState.SemiProductManufactured)]
    [InlineData(ManufactureOrderState.Completed)]
    [InlineData(ManufactureOrderState.Cancelled)]
    public async Task GetOrders_Should_Handle_Various_States(ManufactureOrderState state)
    {
        // Arrange
        var request = new GetManufactureOrdersRequest { State = state };
        var expectedResponse = new GetManufactureOrdersResponse { Orders = new List<ManufactureOrderDto>() };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureOrdersRequest>(r => r.State == state), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetOrders(request);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        _mediatorMock.Verify(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetOrder Tests

    [Fact]
    public async Task GetOrder_Should_Return_Ok_With_Response()
    {
        // Arrange
        var orderId = 1;
        var expectedResponse = new GetManufactureOrderResponse
        {
            Order = new ManufactureOrderDto
            {
                Id = orderId,
                OrderNumber = "MO-2024-001",
                CreatedDate = DateTime.UtcNow,
                CreatedByUser = "test@anela.cz",
                State = ManufactureOrderState.Planned,
                StateChangedAt = DateTime.UtcNow,
                StateChangedByUser = "test@anela.cz"
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureOrderRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetOrder(orderId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetManufactureOrderResponse>();

        response.Subject.Order.Id.Should().Be(orderId);
        response.Subject.Order.OrderNumber.Should().Be("MO-2024-001");

        _mediatorMock.Verify(m => m.Send(It.Is<GetManufactureOrderRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrder_Should_Handle_Failed_Response()
    {
        // Arrange
        var orderId = 999;
        var failedResponse = new GetManufactureOrderResponse(ErrorCodes.OrderNotFound);

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetManufactureOrderRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        // Act
        var result = await _controller.GetOrder(orderId);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
        _mediatorMock.Verify(m => m.Send(It.Is<GetManufactureOrderRequest>(r => r.Id == orderId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CreateOrder Tests

    [Fact]
    public async Task CreateOrder_Should_Return_Ok_With_Response()
    {
        // Arrange
        var request = new CreateManufactureOrderRequest
        {
            ProductCode = "SEMI001",
            ProductName = "Semi Product",
            OriginalBatchSize = 100.0,
            NewBatchSize = 200.0,
            ScaleFactor = 2.0,
            PlannedDate = DateOnly.FromDateTime(DateTime.Now.AddDays(7)),
            ResponsiblePerson = "test@anela.cz",
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new CreateManufactureOrderProductRequest
                {
                    ProductCode = "PROD001",
                    ProductName = "Final Product",
                    PlannedQuantity = 100.0
                }
            }
        };

        var expectedResponse = new CreateManufactureOrderResponse
        {
            Id = 1,
            OrderNumber = "MO-2024-001"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.CreateOrder(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<CreateManufactureOrderResponse>();

        response.Subject.Id.Should().Be(1);
        response.Subject.OrderNumber.Should().Be("MO-2024-001");

        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrder_Should_Handle_Validation_Error()
    {
        // Arrange
        var request = new CreateManufactureOrderRequest(); // Invalid request with missing required fields
        var failedResponse = new CreateManufactureOrderResponse(ErrorCodes.ValidationError);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        // Act
        var result = await _controller.CreateOrder(request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateOrder Tests

    [Fact]
    public async Task UpdateOrder_Should_Return_Ok_With_Response()
    {
        // Arrange
        var orderId = 1;
        var request = new UpdateManufactureOrderRequest
        {
            Id = orderId,
            ResponsiblePerson = "updated@anela.cz",
            PlannedDate = DateOnly.FromDateTime(DateTime.Now.AddDays(10))
        };

        var expectedResponse = new UpdateManufactureOrderResponse
        {
            Order = new UpdateManufactureOrderDto
            {
                Id = orderId,
                OrderNumber = "MO-2024-001",
                CreatedDate = DateTime.UtcNow,
                CreatedByUser = "test@anela.cz",
                ResponsiblePerson = "updated@anela.cz",
                State = "Planned",
                StateChangedAt = DateTime.UtcNow,
                StateChangedByUser = "test@anela.cz"
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.UpdateOrder(orderId, request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<UpdateManufactureOrderResponse>();

        response.Subject.Order.Should().NotBeNull();
        response.Subject.Order.Id.Should().Be(orderId);
        response.Subject.Order.OrderNumber.Should().Be("MO-2024-001");

        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateOrder_Should_Return_BadRequest_When_Ids_Mismatch()
    {
        // Arrange
        var urlId = 1;
        var requestId = 2;
        var request = new UpdateManufactureOrderRequest { Id = requestId };

        // Act
        var result = await _controller.UpdateOrder(urlId, request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>();
        badRequestResult.Subject.Value.Should().Be("ID in URL does not match ID in request body.");

        // Verify MediatR was not called
        _mediatorMock.Verify(m => m.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateOrder_Should_Handle_Not_Found()
    {
        // Arrange
        var orderId = 999;
        var request = new UpdateManufactureOrderRequest { Id = orderId };
        var failedResponse = new UpdateManufactureOrderResponse(ErrorCodes.OrderNotFound);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateManufactureOrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        // Act
        var result = await _controller.UpdateOrder(orderId, request);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateOrderStatus Tests

    [Fact]
    public async Task UpdateOrderStatus_Should_Return_Ok_With_Response()
    {
        // Arrange
        var orderId = 1;
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = ManufactureOrderState.SemiProductManufactured
        };

        var expectedResponse = new UpdateManufactureOrderStatusResponse
        {
            OldState = "Planned",
            NewState = "SemiProductManufactured",
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = "test@anela.cz"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.UpdateOrderStatus(orderId, request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<UpdateManufactureOrderStatusResponse>();

        response.Subject.OldState.Should().Be("Planned");
        response.Subject.NewState.Should().Be("SemiProductManufactured");

        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateOrderStatus_Should_Return_BadRequest_When_Ids_Mismatch()
    {
        // Arrange
        var urlId = 1;
        var requestId = 2;
        var request = new UpdateManufactureOrderStatusRequest { Id = requestId, NewState = ManufactureOrderState.Planned };

        // Act
        var result = await _controller.UpdateOrderStatus(urlId, request);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>();
        badRequestResult.Subject.Value.Should().Be("ID in URL does not match ID in request body.");

        // Verify MediatR was not called
        _mediatorMock.Verify(m => m.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Planned)]
    [InlineData(ManufactureOrderState.Planned, ManufactureOrderState.SemiProductManufactured)]
    [InlineData(ManufactureOrderState.SemiProductManufactured, ManufactureOrderState.Completed)]
    [InlineData(ManufactureOrderState.Draft, ManufactureOrderState.Cancelled)]
    public async Task UpdateOrderStatus_Should_Handle_Valid_State_Transitions(ManufactureOrderState fromState, ManufactureOrderState toState)
    {
        // Arrange
        var orderId = 1;
        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = orderId,
            NewState = toState
        };

        var expectedResponse = new UpdateManufactureOrderStatusResponse
        {
            OldState = fromState.ToString(),
            NewState = toState.ToString(),
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = "test@anela.cz"
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateManufactureOrderStatusRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.UpdateOrderStatus(orderId, request);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetCalendarView Tests

    [Fact]
    public async Task GetCalendarView_Should_Return_Ok_With_Response()
    {
        // Arrange
        var request = new GetCalendarViewRequest
        {
            StartDate = DateTime.Now.AddDays(-30),
            EndDate = DateTime.Now.AddDays(30)
        };

        var expectedResponse = new GetCalendarViewResponse
        {
            Events = new List<CalendarEventDto>
            {
                new CalendarEventDto
                {
                    Id = 1,
                    Title = "MO-2024-001",
                    Date = DateTime.Now.AddDays(7),
                    State = ManufactureOrderState.Planned,
                    OrderNumber = "MO-2024-001",
                    ResponsiblePerson = "test@anela.cz"
                }
            }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCalendarViewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetCalendarView(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCalendarViewResponse>();

        response.Subject.Events.Should().HaveCount(1);
        response.Subject.Events[0].Title.Should().Be("MO-2024-001");

        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCalendarView_Should_Handle_Empty_Date_Range()
    {
        // Arrange
        var request = new GetCalendarViewRequest();
        var expectedResponse = new GetCalendarViewResponse { Events = new List<CalendarEventDto>() };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCalendarViewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetCalendarView(request);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<GetCalendarViewResponse>();

        response.Subject.Events.Should().BeEmpty();
        _mediatorMock.Verify(m => m.Send(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DuplicateOrder Tests

    [Fact]
    public async Task DuplicateOrder_Should_Return_Ok_With_Response()
    {
        // Arrange
        var sourceOrderId = 1;
        var expectedResponse = new DuplicateManufactureOrderResponse
        {
            Id = 2,
            OrderNumber = "MO-2024-002"
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<DuplicateManufactureOrderRequest>(r => r.SourceOrderId == sourceOrderId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DuplicateOrder(sourceOrderId);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>();
        var response = okResult.Subject.Value.Should().BeOfType<DuplicateManufactureOrderResponse>();

        response.Subject.Id.Should().Be(2);
        response.Subject.OrderNumber.Should().Be("MO-2024-002");

        _mediatorMock.Verify(m => m.Send(It.Is<DuplicateManufactureOrderRequest>(r => r.SourceOrderId == sourceOrderId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DuplicateOrder_Should_Handle_Source_Not_Found()
    {
        // Arrange
        var sourceOrderId = 999;
        var failedResponse = new DuplicateManufactureOrderResponse(ErrorCodes.OrderNotFound);

        _mediatorMock
            .Setup(m => m.Send(It.Is<DuplicateManufactureOrderRequest>(r => r.SourceOrderId == sourceOrderId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        // Act
        var result = await _controller.DuplicateOrder(sourceOrderId);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
        _mediatorMock.Verify(m => m.Send(It.Is<DuplicateManufactureOrderRequest>(r => r.SourceOrderId == sourceOrderId), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ConfirmSemiProductManufacture Tests

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_Ok_When_Successful()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmSemiProductManufactureRequest { Id = orderId, ActualQuantity = 10m };
        var handlerResponse = new ConfirmSemiProductManufactureResponse
        {
            Message = "Polotovar byl úspěšně vyroben se skutečným množstvím 10",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(orderId, request);

        // Assert
        actionResult.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_500_When_InternalServerError()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmSemiProductManufactureRequest { Id = orderId, ActualQuantity = 10m };
        var handlerResponse = new ConfirmSemiProductManufactureResponse(ErrorCodes.InternalServerError)
        {
            Message = "DB error",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(orderId, request);

        // Assert
        var statusResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_502_When_ErpGatewayError()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmSemiProductManufactureRequest { Id = orderId, ActualQuantity = 10m };
        var handlerResponse = new ConfirmSemiProductManufactureResponse(ErrorCodes.ErpGatewayError)
        {
            Message = "ERP timeout",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(orderId, request);

        // Assert
        var statusResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task ConfirmSemiProductManufacture_Should_Return_BadRequest_When_Id_Mismatch()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 2, ActualQuantity = 10m };

        // Act
        var actionResult = await _controller.ConfirmSemiProductManufacture(1, request);

        // Assert
        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<ConfirmSemiProductManufactureRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ConfirmProductCompletion Tests

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_Ok_When_Successful()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse();

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        actionResult.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_Ok_When_RequiresConfirmation()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse
        {
            RequiresConfirmation = true,
            Distribution = new ResidueDistributionDto
            {
                ActualSemiProductQuantity = 15m,
                IsWithinAllowedThreshold = false,
            },
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var responseValue = okResult.Value.Should().BeOfType<ConfirmProductCompletionResponse>().Subject;
        responseValue.RequiresConfirmation.Should().BeTrue();
        responseValue.Distribution.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_BadRequest_When_InvalidOperation()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse(ErrorCodes.InvalidOperation)
        {
            Message = "workflow failure",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_500_When_InternalServerError()
    {
        // Arrange
        var orderId = 1;
        var request = new ConfirmProductCompletionRequest
        {
            Id = orderId,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };
        var handlerResponse = new ConfirmProductCompletionResponse(ErrorCodes.InternalServerError)
        {
            Message = "boom",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handlerResponse);

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(orderId, request);

        // Assert
        var statusResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task ConfirmProductCompletion_Should_Return_BadRequest_When_Id_Mismatch()
    {
        // Arrange
        var request = new ConfirmProductCompletionRequest
        {
            Id = 2,
            Products = new List<ProductActualQuantityRequest>
            {
                new() { Id = 10, ActualQuantity = 5m },
            },
        };

        // Act
        var actionResult = await _controller.ConfirmProductCompletion(1, request);

        // Assert
        actionResult.Result.Should().BeOfType<BadRequestObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<ConfirmProductCompletionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}