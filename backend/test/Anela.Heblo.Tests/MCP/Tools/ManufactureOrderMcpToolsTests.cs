using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrders;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOrder;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetCalendarView;
using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ManufactureOrderMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ManufactureOrderMcpTools _tools;

    public ManufactureOrderMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new ManufactureOrderMcpTools(_mediatorMock.Object);
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
        var result = await _tools.GetManufactureOrders();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetManufactureOrdersRequest>(),
            default
        ), Times.Once);
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
        var result = await _tools.GetManufactureOrder(123);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetManufactureOrderRequest>(req => req.Id == 123),
            default
        ), Times.Once);
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
        var result = await _tools.GetCalendarView();

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<GetCalendarViewRequest>(),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task GetResponsiblePersons_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new GetGroupMembersResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetGroupMembersRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetResponsiblePersons("group-id-123");

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<GetGroupMembersRequest>(req => req.GroupId == "group-id-123"),
            default
        ), Times.Once);
    }
}
