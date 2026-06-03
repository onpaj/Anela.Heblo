using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class StockWriteBackDqtComparerTests
{
    private readonly Mock<IStockOperationQuery> _stockOperationsMock = new();
    private readonly Mock<IStockTakingQuery> _stockTakingsMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private StockWriteBackDqtComparer CreateSut(TimeSpan? stuckThreshold = null) =>
        new(_stockOperationsMock.Object, _stockTakingsMock.Object, stuckThreshold);

    private void SetupNoStockTaking() =>
        _stockTakingsMock.Setup(q => q.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockTakingSnapshot>());

    private void SetupOperations(params StockOperationSnapshot[] snapshots) =>
        _stockOperationsMock.Setup(q => q.GetByCreatedDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenAllOperationsCompleted()
    {
        // Arrange
        SetupOperations(new StockOperationSnapshot
        {
            ProductCode = "P001",
            Amount = 1,
            DocumentNumber = "OP001",
            State = StockOperationStateSnapshot.Completed,
            CreatedAtUtc = DateTime.UtcNow,
        });
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
        SetupOperations(new StockOperationSnapshot
        {
            ProductCode = "P002",
            Amount = 5,
            DocumentNumber = "OP002",
            State = StockOperationStateSnapshot.Failed,
            CreatedAtUtc = DateTime.UtcNow,
            ErrorMessage = "HTTP 500 from Shoptet",
        });
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
        SetupOperations(new StockOperationSnapshot
        {
            ProductCode = "P003",
            Amount = 2,
            DocumentNumber = "OP003",
            State = StockOperationStateSnapshot.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        });
        SetupNoStockTaking();

        // Act
        var result = await CreateSut(stuckThreshold: TimeSpan.Zero)
            .CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.OperationStuck);
    }

    [Fact]
    public async Task CompareAsync_ReturnsStockTakingErrored_WhenRecordHasError()
    {
        // Arrange
        SetupOperations();
        _stockTakingsMock.Setup(q => q.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StockTakingSnapshot { Code = "P004", AmountNew = 10, Error = "Shoptet API timeout" },
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
