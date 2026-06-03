using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class StockWriteBackDqtComparerTests
{
    private readonly Mock<IStockUpOperationRepository> _operationRepoMock = new();
    private readonly Mock<IStockTakingRepository> _stockTakingRepoMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private StockWriteBackDqtComparer CreateSut(TimeSpan? stuckThreshold = null) =>
        new(_operationRepoMock.Object, _stockTakingRepoMock.Object, stuckThreshold);

    private void SetupNoStockTaking() =>
        _stockTakingRepoMock.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenAllOperationsCompleted()
    {
        // Arrange
        var completedOp = new StockUpOperation("OP001", "P001", 1, StockUpSourceType.TransportBox, 1);
        completedOp.MarkAsSubmitted(DateTime.UtcNow);
        completedOp.MarkAsCompleted(DateTime.UtcNow);

        _operationRepoMock.Setup(r => r.GetAll()).Returns(new[] { completedOp }.AsQueryable());
        SetupNoStockTaking();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ReturnsOperationFailed_WhenOperationInFailedState()
    {
        // Arrange
        var failedOp = new StockUpOperation("OP002", "P002", 5, StockUpSourceType.TransportBox, 1);
        failedOp.MarkAsFailed(DateTime.UtcNow, "HTTP 500 from Shoptet");

        _operationRepoMock.Setup(r => r.GetAll()).Returns(new[] { failedOp }.AsQueryable());
        SetupNoStockTaking();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("P002");
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.OperationFailed);
        result.Mismatches[0].Details.Should().Contain("HTTP 500 from Shoptet");
    }

    [Fact]
    public async Task CompareAsync_ReturnsOperationStuck_WhenPendingOperationExceedsThreshold()
    {
        // Arrange — use TimeSpan.Zero threshold so any Pending operation is "stuck"
        var pendingOp = new StockUpOperation("OP003", "P003", 2, StockUpSourceType.TransportBox, 1);
        // Stays Pending (no state transition called)

        _operationRepoMock.Setup(r => r.GetAll()).Returns(new[] { pendingOp }.AsQueryable());
        SetupNoStockTaking();

        // Act
        var result = await CreateSut(stuckThreshold: TimeSpan.Zero).CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.OperationStuck);
    }

    [Fact]
    public async Task CompareAsync_ReturnsStockTakingErrored_WhenRecordHasError()
    {
        // Arrange
        _operationRepoMock.Setup(r => r.GetAll()).Returns(Array.Empty<StockUpOperation>().AsQueryable());
        _stockTakingRepoMock.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>
            {
                new() { Code = "P004", Error = "Shoptet API timeout", Date = DateTime.UtcNow, AmountNew = 10, AmountOld = 8 }
            });

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("P004");
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.StockTakingErrored);
        result.Mismatches[0].Details.Should().Contain("Shoptet API timeout");
    }
}
