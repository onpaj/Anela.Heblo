using Anela.Heblo.Application.Common.TimePeriods;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CalculateBatchPlanHandlerTests
{
    private readonly Mock<IBatchPlanningService> _batchPlanningServiceMock;
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ITimePeriodResolver> _timePeriodResolverMock;
    private readonly CalculateBatchPlanHandler _handler;

    public CalculateBatchPlanHandlerTests()
    {
        _batchPlanningServiceMock = new Mock<IBatchPlanningService>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _manufactureClientMock = new Mock<IManufactureClient>();
        _timePeriodResolverMock = new Mock<ITimePeriodResolver>();
        _handler = new CalculateBatchPlanHandler(
            _batchPlanningServiceMock.Object,
            _catalogRepositoryMock.Object,
            _manufactureClientMock.Object,
            _timePeriodResolverMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsBatchPlanningService()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "SEMI001",
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
            .Setup(x => x.CalculateBatchPlan(request, It.IsAny<IReadOnlyList<DateRange>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("SEMI001", result.Semiproduct.ProductCode);

        _batchPlanningServiceMock.Verify(
            x => x.CalculateBatchPlan(request, It.IsAny<IReadOnlyList<DateRange>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ServiceThrowsException_PropagatesException()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "INVALID",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0
        };

        _batchPlanningServiceMock
            .Setup(x => x.CalculateBatchPlan(It.IsAny<CalculateBatchPlanRequest>(), It.IsAny<IReadOnlyList<DateRange>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Semiproduct not found"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _handler.Handle(request, CancellationToken.None));
        Assert.Contains("Semiproduct not found", exception.Message);
    }

    [Fact]
    public async Task Handle_TimePeriodSet_ResolvesViaTimePeriodResolver()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            TimePeriod = TimePeriod.Q9M
        };

        var resolvedRanges = new[]
        {
            new DateRange(DateTime.Now.AddMonths(-6), DateTime.Now),
            new DateRange(DateTime.Now.AddYears(-1), DateTime.Now.AddYears(-1).AddMonths(3))
        };

        _timePeriodResolverMock
            .Setup(x => x.Resolve(TimePeriod.Q9M, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Returns(resolvedRanges);

        _batchPlanningServiceMock
            .Setup(x => x.CalculateBatchPlan(request, It.IsAny<IReadOnlyList<DateRange>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalculateBatchPlanResponse { Success = true });

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _timePeriodResolverMock.Verify(
            x => x.Resolve(TimePeriod.Q9M, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()),
            Times.Once);
        _batchPlanningServiceMock.Verify(
            x => x.CalculateBatchPlan(request, resolvedRanges, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TimePeriodNull_UsesSingleRangeFromFromDateToDate()
    {
        // Arrange
        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 3, 31);
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            FromDate = from,
            ToDate = to,
            TimePeriod = null
        };

        IReadOnlyList<DateRange>? capturedRanges = null;
        _batchPlanningServiceMock
            .Setup(x => x.CalculateBatchPlan(request, It.IsAny<IReadOnlyList<DateRange>>(), It.IsAny<CancellationToken>()))
            .Callback<CalculateBatchPlanRequest, IReadOnlyList<DateRange>, CancellationToken>((_, ranges, _) => capturedRanges = ranges)
            .ReturnsAsync(new CalculateBatchPlanResponse { Success = true });

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedRanges);
        Assert.Single(capturedRanges!);
        Assert.Equal(from, capturedRanges![0].From);
        Assert.Equal(to, capturedRanges[0].To);
        _timePeriodResolverMock.Verify(
            x => x.Resolve(It.IsAny<TimePeriod>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()),
            Times.Never);
    }
}
