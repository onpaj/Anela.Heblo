using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculateBatchPlanHandlerTests
{
    private readonly Mock<IBatchPlanningService> _batchPlanningServiceMock;
    private readonly CalculateBatchPlanHandler _handler;

    public CalculateBatchPlanHandlerTests()
    {
        _batchPlanningServiceMock = new Mock<IBatchPlanningService>();
        _handler = new CalculateBatchPlanHandler(_batchPlanningServiceMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsBatchPlanningService()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0
        };

        var expectedResponse = new CalculateBatchPlanResponse
        {
            Success = true,
            Semiproduct = new SemiproductInfoDto
            {
                ProductCode = "SEMI001",
                ProductName = "Test Semiproduct",
                AvailableStock = 1000
            },
            ProductSizes = new List<BatchPlanItemDto>(),
            Summary = new BatchPlanSummaryDto
            {
                UsedControlMode = BatchPlanControlMode.MmqMultiplier,
                TotalProductSizes = 0,
                TotalVolumeUsed = 0,
                TotalVolumeAvailable = 1000,
                VolumeUtilizationPercentage = 0
            }
        };

        _batchPlanningServiceMock
            .Setup(x => x.CalculateBatchPlan(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("SEMI001", result.Semiproduct.ProductCode);
        
        _batchPlanningServiceMock.Verify(
            x => x.CalculateBatchPlan(request, It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task Handle_ServiceThrowsException_PropagatesException()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            SemiproductCode = "INVALID",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0
        };

        _batchPlanningServiceMock
            .Setup(x => x.CalculateBatchPlan(It.IsAny<CalculateBatchPlanRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Semiproduct not found"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => 
            _handler.Handle(request, CancellationToken.None));
        Assert.Contains("Semiproduct not found", exception.Message);
    }
}