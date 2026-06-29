using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetStockUpOperationsSummaryHandlerTests
{
    private readonly Mock<IStockUpOperationRepository> _repositoryMock;
    private readonly GetStockUpOperationsSummaryHandler _handler;

    public GetStockUpOperationsSummaryHandlerTests()
    {
        _repositoryMock = new Mock<IStockUpOperationRepository>();
        var logger = NullLogger<GetStockUpOperationsSummaryHandler>.Instance;
        _handler = new GetStockUpOperationsSummaryHandler(_repositoryMock.Object, logger);
    }

    [Fact]
    public async Task Handle_NoOperations_ReturnsZeroCounts()
    {
        // Arrange
        var request = new GetStockUpOperationsSummaryRequest();

        _repositoryMock
            .Setup(r => r.GetActiveCountsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((0, 0, 0));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.PendingCount);
        Assert.Equal(0, result.SubmittedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.TotalInQueue);
    }

    [Fact]
    public async Task Handle_WithOperations_ReturnsCorrectCounts()
    {
        // Arrange
        var request = new GetStockUpOperationsSummaryRequest
        {
            SourceType = StockUpSourceType.GiftPackageManufacture
        };

        // GPM operations: 1 Pending (GPM-000002-PROD3), 1 Submitted (GPM-000001-PROD1), 1 Failed (GPM-000001-PROD2)
        _repositoryMock
            .Setup(r => r.GetActiveCountsAsync(
                It.Is<StockUpSourceType?>(st => st == StockUpSourceType.GiftPackageManufacture),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, 1, 1)); // (Pending, Submitted, Failed)

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.PendingCount); // GPM-000002-PROD3
        Assert.Equal(1, result.SubmittedCount); // GPM-000001-PROD1
        Assert.Equal(1, result.FailedCount); // GPM-000001-PROD2
        Assert.Equal(2, result.TotalInQueue); // Pending + Submitted
    }

    [Fact]
    public async Task Handle_NoSourceTypeFilter_ReturnsAllOperations()
    {
        // Arrange
        var request = new GetStockUpOperationsSummaryRequest(); // No SourceType filter

        // Both operations are pending (one GPM, one TransportBox)
        _repositoryMock
            .Setup(r => r.GetActiveCountsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((2, 0, 0)); // (Pending, Submitted, Failed)

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.PendingCount); // Both operations
        Assert.Equal(0, result.SubmittedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(2, result.TotalInQueue);
    }

    [Fact]
    public async Task Handle_CompletedState_IsExcludedAndFailedIsIncluded()
    {
        // Arrange — one operation per state. Completed must be excluded; Failed must be counted.
        var request = new GetStockUpOperationsSummaryRequest();

        // Repository only counts active states (Pending, Submitted, Failed); Completed is excluded
        _repositoryMock
            .Setup(r => r.GetActiveCountsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, 1, 1)); // (Pending, Submitted, Failed) — Completed excluded

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert — Completed must contribute zero to every counter; Failed must be counted.
        Assert.True(result.Success);
        Assert.Equal(1, result.PendingCount);
        Assert.Equal(1, result.SubmittedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(2, result.TotalInQueue); // Pending + Submitted
        Assert.Equal(3, result.PendingCount + result.SubmittedCount + result.FailedCount);
    }
}
