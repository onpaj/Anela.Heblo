using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.DeletePackingMaterial;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialLogs;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialsControllerNotFoundTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly PackingMaterialsController _controller;

    public PackingMaterialsControllerNotFoundTests()
    {
        _controller = new PackingMaterialsController(_mediator.Object);
    }

    [Fact]
    public async Task UpdatePackingMaterial_Returns404_WhenHandlerReturnsResourceNotFound()
    {
        // Arrange
        var notFound = new UpdatePackingMaterialResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ResourceNotFound,
            Error = "Packing material with ID 99 not found."
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<UpdatePackingMaterialRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFound);

        var request = new UpdatePackingMaterialRequest
        {
            Id = 99,
            Name = "Anything",
            ConsumptionRate = 1m,
            ConsumptionType = ConsumptionType.PerOrder
        };

        // Act
        var result = await _controller.UpdatePackingMaterial(99, request, CancellationToken.None);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UpdatePackingMaterialQuantity_Returns404_WhenHandlerReturnsResourceNotFound()
    {
        // Arrange
        var notFound = new UpdatePackingMaterialQuantityResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ResourceNotFound,
            Error = "Packing material with ID 99 not found."
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<UpdatePackingMaterialQuantityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFound);

        var body = new UpdateQuantityRequest
        {
            NewQuantity = 5m,
            Date = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        // Act
        var result = await _controller.UpdatePackingMaterialQuantity(99, body, CancellationToken.None);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetPackingMaterialLogs_Returns404_WhenHandlerReturnsResourceNotFound()
    {
        // Arrange
        var notFound = new GetPackingMaterialLogsResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ResourceNotFound,
            Error = "Packing material with ID 99 not found."
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<GetPackingMaterialLogsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFound);

        // Act
        var result = await _controller.GetPackingMaterialLogs(99, 60, CancellationToken.None);

        // Assert
        var notFoundResult = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeletePackingMaterial_Returns404_WhenHandlerReturnsResourceNotFound()
    {
        // Arrange
        var notFound = new DeletePackingMaterialResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ResourceNotFound,
            Error = "Packing material with ID 99 not found."
        };
        _mediator
            .Setup(m => m.Send(It.IsAny<DeletePackingMaterialRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFound);

        // Act
        var result = await _controller.DeletePackingMaterial(99, CancellationToken.None);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeletePackingMaterial_Returns204_WhenHandlerReturnsSuccess()
    {
        // Arrange
        var ok = new DeletePackingMaterialResponse();
        _mediator
            .Setup(m => m.Send(It.IsAny<DeletePackingMaterialRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ok);

        // Act
        var result = await _controller.DeletePackingMaterial(1, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }
}
