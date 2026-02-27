using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchBySize;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchByIngredient;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using MediatR;
using ModelContextProtocol;
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
        var jsonResult = await _tools.GetBatchTemplate("AKL001");

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<CalculatedBatchSizeRequest>(req => req.ProductCode == "AKL001"),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<CalculatedBatchSizeResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task GetBatchTemplate_ShouldThrowMcpException_WhenTemplateNotFound()
    {
        // Arrange
        var errorResponse = new CalculatedBatchSizeResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.ManufactureTemplateNotFound,
            Params = new Dictionary<string, string> { { "ProductCode", "UNKNOWN" } }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculatedBatchSizeRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.GetBatchTemplate("UNKNOWN")
        );

        Assert.Contains("ManufactureTemplateNotFound", exception.Message);
    }

    [Fact]
    public async Task CalculateBatchBySize_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculatedBatchSizeResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculatedBatchSizeRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.CalculateBatchBySize("AKL001", 100.0);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<CalculatedBatchSizeRequest>(req => req.ProductCode == "AKL001" && req.DesiredBatchSize == 100.0),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<CalculatedBatchSizeResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task CalculateBatchBySize_ShouldThrowMcpException_WhenInvalidBatchSize()
    {
        // Arrange
        var errorResponse = new CalculatedBatchSizeResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.InvalidBatchSize,
            Params = new Dictionary<string, string> { { "BatchSize", "-1" } }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculatedBatchSizeRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.CalculateBatchBySize("AKL001", -1.0)
        );

        Assert.Contains("InvalidBatchSize", exception.Message);
    }

    [Fact]
    public async Task CalculateBatchByIngredient_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculateBatchByIngredientResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchByIngredientRequest>(), default))
            .ReturnsAsync(expectedResponse);

        // Act
        var jsonResult = await _tools.CalculateBatchByIngredient("AKL001", "BIS001", 50.0);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.Is<CalculateBatchByIngredientRequest>(req =>
                req.ProductCode == "AKL001" &&
                req.IngredientCode == "BIS001" &&
                req.DesiredIngredientAmount == 50.0),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<CalculateBatchByIngredientResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task CalculateBatchByIngredient_ShouldThrowMcpException_WhenIngredientNotFound()
    {
        // Arrange
        var errorResponse = new CalculateBatchByIngredientResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.IngredientNotFoundInTemplate,
            Params = new Dictionary<string, string> { { "IngredientCode", "UNKNOWN" } }
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchByIngredientRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.CalculateBatchByIngredient("AKL001", "UNKNOWN", 50.0)
        );

        Assert.Contains("IngredientNotFoundInTemplate", exception.Message);
    }

    [Fact]
    public async Task CalculateBatchPlan_ShouldMapParametersCorrectly()
    {
        // Arrange
        var expectedResponse = new CalculateBatchPlanResponse { Success = true };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchPlanRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var request = new CalculateBatchPlanRequest { ProductCode = "AKL001" };

        // Act
        var jsonResult = await _tools.CalculateBatchPlan(request);

        // Assert
        _mediatorMock.Verify(m => m.Send(
            It.IsAny<CalculateBatchPlanRequest>(),
            default
        ), Times.Once);

        var deserialized = JsonSerializer.Deserialize<CalculateBatchPlanResponse>(jsonResult);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
    }

    [Fact]
    public async Task CalculateBatchPlan_ShouldThrowMcpException_WhenDataNotAvailable()
    {
        // Arrange
        var errorResponse = new CalculateBatchPlanResponse
        {
            Success = false,
            ErrorCode = Anela.Heblo.Application.Shared.ErrorCodes.ManufacturingDataNotAvailable,
            Params = new Dictionary<string, string>()
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CalculateBatchPlanRequest>(), default))
            .ReturnsAsync(errorResponse);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(
            () => _tools.CalculateBatchPlan(new CalculateBatchPlanRequest { ProductCode = "AKL001" })
        );

        Assert.Contains("ManufacturingDataNotAvailable", exception.Message);
    }
}
