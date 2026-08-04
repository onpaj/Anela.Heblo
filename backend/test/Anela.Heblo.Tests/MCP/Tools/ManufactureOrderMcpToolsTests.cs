using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ManufactureOrderMcpToolsTests
{
    private static readonly string ReadRole = AccessRoles.For(Feature.Manufacture_ManufactureOrders, AccessLevel.Read);

    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly ManufactureOrderMcpTools _tools;

    public ManufactureOrderMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(true);
        _tools = new ManufactureOrderMcpTools(_mediatorMock.Object, _currentUserServiceMock.Object);
    }

    [Fact]
    public async Task GetManufactureOrders_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetManufactureOrdersResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetManufactureOrders();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetManufactureOrdersRequest>(),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetManufactureOrdersResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task GetManufactureOrder_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetManufactureOrderResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrderRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetManufactureOrder(123);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetManufactureOrderRequest>(req => req.Id == 123),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetManufactureOrderResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task GetManufactureOrder_ShouldThrowMcpException_WhenOrderNotFound()
    {
        // Arrange
        var errorResponse = new GetManufactureOrderResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.OrderNotFound,
            Params = new Dictionary<string, string> { { "Id", "999" } }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrderRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetManufactureOrder(999)
        );

        Assert.Contains("NotFound", exception.Message);
    }

    [Fact]
    public async Task GetManufactureOrders_ShouldThrowMcpException_WhenDataNotAvailable()
    {
        // Arrange
        var errorResponse = new GetManufactureOrdersResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.ManufacturingDataNotAvailable,
            Params = new Dictionary<string, string>()
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetManufactureOrders()
        );

        Assert.Contains("ManufacturingDataNotAvailable", exception.Message);
    }

    [Fact]
    public async Task GetCalendarView_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetCalendarViewResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCalendarViewRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.GetCalendarView();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetCalendarViewRequest>(),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<GetCalendarViewResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task GetCalendarView_ShouldThrowMcpException_WhenDataNotAvailable()
    {
        // Arrange
        var errorResponse = new GetCalendarViewResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.ManufacturingDataNotAvailable,
            Params = new Dictionary<string, string>()
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetCalendarViewRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetCalendarView()
        );

        Assert.Contains("ManufacturingDataNotAvailable", exception.Message);
    }

    [Theory]
    [InlineData("GetManufactureOrders")]
    [InlineData("GetManufactureOrder")]
    [InlineData("GetCalendarView")]
    public async Task Tools_ThrowForbidden_AndSkipMediator_WhenUserLacksReadRole(string tool)
    {
        // Arrange
        _currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(false);

        // Act
        var exception = await Assert.ThrowsAsync<McpException>(() => tool switch
        {
            "GetManufactureOrders" => _tools.GetManufactureOrders(),
            "GetManufactureOrder" => _tools.GetManufactureOrder(123),
            _ => _tools.GetCalendarView()
        });

        // Assert
        Assert.Contains("FORBIDDEN", exception.Message);
        Assert.Contains(ReadRole, exception.Message);
        _mediatorMock.Verify(m => m.Send(It.IsAny<GetManufactureOrdersRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediatorMock.Verify(m => m.Send(It.IsAny<GetManufactureOrderRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mediatorMock.Verify(m => m.Send(It.IsAny<GetCalendarViewRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
