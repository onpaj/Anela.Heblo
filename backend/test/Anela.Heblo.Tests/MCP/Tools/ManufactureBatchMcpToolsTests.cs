using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ManufactureBatchMcpToolsTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ManufactureBatchMcpTools _tools;

    public ManufactureBatchMcpToolsTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _tools = new ManufactureBatchMcpTools(_mediatorMock.Object);
    }

    [Fact]
    public async Task GetBatchTemplate_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculatedBatchSizeResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculatedBatchSizeRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tools.GetBatchTemplate("AKL001");

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<CalculatedBatchSizeRequest>(req => req.ProductCode == "AKL001"),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task CalculateBatchBySize_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculatedBatchSizeResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculatedBatchSizeRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var request = new CalculatedBatchSizeRequest { ProductCode = "AKL001" };

        // Act
        var result = await _tools.CalculateBatchBySize(request);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<CalculatedBatchSizeRequest>(req => req.ProductCode == "AKL001"),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task CalculateBatchByIngredient_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculateBatchByIngredientResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchByIngredientRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var request = new CalculateBatchByIngredientRequest();

        // Act
        var result = await _tools.CalculateBatchByIngredient(request);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<CalculateBatchByIngredientRequest>(),
            default
        ), Times.Once);
    }

    [Fact]
    public async Task CalculateBatchPlan_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculateBatchPlanResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchPlanRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var request = new CalculateBatchPlanRequest();

        // Act
        var result = await _tools.CalculateBatchPlan(request);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<CalculateBatchPlanRequest>(),
            default
        ), Times.Once);
    }
}
