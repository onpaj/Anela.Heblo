using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Catalog.UseCases.RecalculateProductWeight;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class RecalculateProductWeightHandlerTests
{
    private readonly Mock<IProductWeightRecalculationService> _serviceMock;
    private readonly Mock<ILogger<RecalculateProductWeightHandler>> _loggerMock;
    private readonly RecalculateProductWeightHandler _handler;

    public RecalculateProductWeightHandlerTests()
    {
        _serviceMock = new Mock<IProductWeightRecalculationService>();
        _loggerMock = new Mock<ILogger<RecalculateProductWeightHandler>>();
        _handler = new RecalculateProductWeightHandler(_serviceMock.Object, _loggerMock.Object);
    }

    // FR-1: Single-product recalculation path
    [Fact]
    public async Task Handle_WithProductCode_DispatchesToSingleProductRecalculation()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = "PROD001" };
        var serviceResult = new ProductWeightRecalculationResult
        {
            ProcessedCount = 1,
            SuccessCount = 1,
            ErrorCount = 0,
            ErrorMessages = new List<string>()
        };
        _serviceMock
            .Setup(s => s.RecalculateProductWeight("PROD001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert - dispatch pinned in both directions
        _serviceMock.Verify(
            s => s.RecalculateProductWeight("PROD001", It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.Verify(
            s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - response mirrors the service result
        response.ProcessedCount.Should().Be(1);
        response.SuccessCount.Should().Be(1);
        response.ErrorCount.Should().Be(0);
        response.ErrorMessages.Should().BeEquivalentTo(serviceResult.ErrorMessages);
    }

    // FR-2: Full-catalog recalculation path (null and empty are one equivalence class)
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_WithoutProductCode_DispatchesToFullCatalogRecalculation(string? productCode)
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = productCode };
        var serviceResult = new ProductWeightRecalculationResult
        {
            ProcessedCount = 42,
            SuccessCount = 42,
            ErrorCount = 0,
            ErrorMessages = new List<string>()
        };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ReturnsAsync(serviceResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert - dispatch pinned in both directions
        _serviceMock.Verify(
            s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()),
            Times.Once);
        _serviceMock.Verify(
            s => s.RecalculateProductWeight(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert - response mirrors the service result
        response.ProcessedCount.Should().Be(42);
        response.SuccessCount.Should().Be(42);
        response.ErrorCount.Should().Be(0);
    }

    // FR-3: Success flag derivation - no errors -> Success == true
    [Fact]
    public async Task Handle_WhenServiceReturnsNoErrors_SetsSuccessTrue()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = null };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductWeightRecalculationResult
            {
                ProcessedCount = 10,
                SuccessCount = 10,
                ErrorCount = 0,
                ErrorMessages = new List<string>()
            });

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
    }

    // FR-3: Success flag derivation - errors present -> Success == false, counts/messages pass through.
    // Load-bearing because BaseResponse.Success defaults to true: this proves the handler's
    // `Success = result.ErrorCount == 0` line actually executed and flipped the default.
    [Fact]
    public async Task Handle_WhenServiceReturnsErrors_SetsSuccessFalseAndPassesThrough()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = null };
        var errorMessages = new List<string> { "Product XYZ has no ingredients" };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductWeightRecalculationResult
            {
                ProcessedCount = 10,
                SuccessCount = 9,
                ErrorCount = 1,
                ErrorMessages = errorMessages
            });

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ProcessedCount.Should().Be(10);
        response.SuccessCount.Should().Be(9);
        response.ErrorCount.Should().Be(1);
        response.ErrorMessages.Should().BeEquivalentTo(errorMessages);
    }

    // FR-4: Exception fallback path (catch wraps both branches; exercised via empty ProductCode)
    [Fact]
    public async Task Handle_WhenServiceThrows_ReturnsFallbackResponseWithoutRethrowing()
    {
        // Arrange
        var request = new RecalculateProductWeightRequest { ProductCode = null };
        _serviceMock
            .Setup(s => s.RecalculateAllProductWeights(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        // Act - awaiting a returned response (not throwing) is itself proof the handler did not rethrow
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.ProcessedCount.Should().Be(0);
        response.SuccessCount.Should().Be(0);
        response.ErrorCount.Should().Be(1);
        response.Success.Should().BeFalse();
        response.ErrorMessages.Should().ContainSingle();
        response.ErrorMessages.Should().Contain(m => m.Contains("Internal error") && m.Contains("boom"));
    }
}
