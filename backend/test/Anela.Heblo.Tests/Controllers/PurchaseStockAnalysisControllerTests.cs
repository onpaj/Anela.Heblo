using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class PurchaseStockAnalysisControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly PurchaseStockAnalysisController _controller;

    public PurchaseStockAnalysisControllerTests()
    {
        _controller = new PurchaseStockAnalysisController(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetStockAnalysis_InvalidModelState_ReturnsStandardizedValidationError()
    {
        // Arrange
        _controller.ModelState.AddModelError("PageSize", "The field PageSize must be between 1 and 100.");
        var request = new GetPurchaseStockAnalysisRequest { PageSize = 999 };

        // Act
        var result = await _controller.GetStockAnalysis(request, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<GetPurchaseStockAnalysisResponse>(badRequestResult.Value);
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);

        // MediatR must never be invoked when validation fails
        _mediatorMock.Verify(
            m => m.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetStockAnalysis_ValidModelState_DelegatesToMediator()
    {
        // Arrange
        var request = new GetPurchaseStockAnalysisRequest { PageSize = 20 };
        var expectedResponse = new GetPurchaseStockAnalysisResponse();
        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetStockAnalysis(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expectedResponse, okResult.Value);
    }
}
