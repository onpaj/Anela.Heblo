using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetStockUpOperationsSummary;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MockQueryable;
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
        var emptyOperations = new List<StockUpOperation>();

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(emptyOperations.BuildMock());

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

        var operations = new List<StockUpOperation>
        {
            new("GPM-000001-PROD1", "PROD1", 10, StockUpSourceType.GiftPackageManufacture, 1),
            new("GPM-000001-PROD2", "PROD2", 5, StockUpSourceType.GiftPackageManufacture, 1),
            new("GPM-000002-PROD3", "PROD3", 8, StockUpSourceType.GiftPackageManufacture, 2),
            new("BOX-000001-PROD4", "PROD4", 15, StockUpSourceType.TransportBox, 1),
        };

        // Set states via reflection or state transition methods
        operations[0].MarkAsSubmitted(System.DateTime.UtcNow);
        operations[1].MarkAsSubmitted(System.DateTime.UtcNow);
        operations[1].MarkAsFailed(System.DateTime.UtcNow, "Test error");
        // operations[2] stays Pending
        operations[3].MarkAsSubmitted(System.DateTime.UtcNow);

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

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

        var operations = new List<StockUpOperation>
        {
            new("GPM-000001-PROD1", "PROD1", 10, StockUpSourceType.GiftPackageManufacture, 1),
            new("BOX-000001-PROD2", "PROD2", 5, StockUpSourceType.TransportBox, 1),
        };

        // Both pending
        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

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
        var now = System.DateTime.UtcNow;

        var pending = new StockUpOperation("GPM-Pending", "P1", 1, StockUpSourceType.GiftPackageManufacture, 1);

        var submitted = new StockUpOperation("GPM-Submitted", "P2", 1, StockUpSourceType.GiftPackageManufacture, 1);
        submitted.MarkAsSubmitted(now);

        var completed = new StockUpOperation("GPM-Completed", "P3", 1, StockUpSourceType.GiftPackageManufacture, 1);
        completed.MarkAsCompleted(now); // StockUpOperation.MarkAsCompleted can transition from Pending directly

        var failed = new StockUpOperation("GPM-Failed", "P4", 1, StockUpSourceType.GiftPackageManufacture, 1);
        failed.MarkAsSubmitted(now);
        failed.MarkAsFailed(now, "boom");

        var operations = new List<StockUpOperation> { pending, submitted, completed, failed };

        _repositoryMock.Setup(r => r.GetAll())
            .Returns(operations.BuildMock());

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
