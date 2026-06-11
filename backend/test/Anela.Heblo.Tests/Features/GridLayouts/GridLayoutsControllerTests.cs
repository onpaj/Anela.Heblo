using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.GridLayouts.Contracts;
using Anela.Heblo.Application.Features.GridLayouts.UseCases.GetGridLayout;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.GridLayouts;

public class GridLayoutsControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly GridLayoutsController _controller;

    public GridLayoutsControllerTests()
    {
        _controller = new GridLayoutsController(_mediator.Object);
    }

    [Fact]
    public async Task Get_Returns500_WhenHandlerReturnsDatabaseError()
    {
        // Arrange
        var errorResponse = new GetGridLayoutResponse(ErrorCodes.DatabaseError);
        _mediator
            .Setup(m => m.Send(It.IsAny<GetGridLayoutRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _controller.Get("test-grid");

        // Assert
        var statusResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        statusResult.StatusCode.Should().Be(500);
        statusResult.Value.Should().BeOfType<GetGridLayoutResponse>()
            .Which.ErrorCode.Should().Be(ErrorCodes.DatabaseError);
    }

    [Fact]
    public async Task Get_Returns200WithNullBody_WhenNoSavedLayoutExists()
    {
        // Arrange
        var emptyResponse = new GetGridLayoutResponse { Layout = null };
        _mediator
            .Setup(m => m.Send(It.IsAny<GetGridLayoutRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResponse);

        // Act
        var result = await _controller.Get("test-grid");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        okResult.Value.Should().BeNull();
    }

    [Fact]
    public async Task Get_Returns200WithDto_WhenSavedLayoutExists()
    {
        // Arrange
        var dto = new GridLayoutDto
        {
            GridKey = "test-grid",
            Columns = new List<GridColumnStateDto>(),
            LastModified = DateTime.UtcNow
        };
        var successResponse = new GetGridLayoutResponse { Layout = dto };
        _mediator
            .Setup(m => m.Send(It.IsAny<GetGridLayoutRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResponse);

        // Act
        var result = await _controller.Get("test-grid");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
        okResult.Value.Should().BeSameAs(dto);
    }
}
