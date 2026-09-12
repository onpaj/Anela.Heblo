using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class DriftDqtJobRunnerTests
{
    private readonly Mock<IDqtRunRepository> _repoMock = new();
    private readonly Mock<IDriftDqtComparer> _comparerMock = new();

    public DriftDqtJobRunnerTests()
    {
        _comparerMock.Setup(c => c.TestType).Returns(DqtTestType.ProductPairing);
    }

    private DriftDqtJobRunner CreateSut() =>
        new(_repoMock.Object, new[] { _comparerMock.Object }, TimeProvider.System, NullLogger<DriftDqtJobRunner>.Instance);

    [Fact]
    public async Task RunAsync_PersistsInformationalResultsWithoutCountingThemAsMismatches()
    {
        // Arrange: a comparer may need to record observations that are not failures — the
        // price check's MissingInShoptet rows, which must stay visible without turning the
        // dashboard tile amber.
        var run = DqtRun.Start(
            DqtTestType.ProductPairing,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            DqtTriggerType.Scheduled,
            DateTime.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _comparerMock
            .Setup(c => c.CompareAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriftComparisonResult
            {
                TotalChecked = 10,
                Mismatches = new[] { new DriftMismatch { EntityKey = "P001", MismatchCode = 1 } },
                Informational = new[] { new DriftMismatch { EntityKey = "P002", MismatchCode = 4 } },
            });

        // Act
        await CreateSut().RunAsync(run.Id);

        // Assert
        _repoMock.Verify(r => r.AddDriftResultsAsync(
            It.Is<IEnumerable<DqtDriftResult>>(e => e.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        run.TotalMismatches.Should().Be(1);
        run.TotalChecked.Should().Be(10);
    }

    [Fact]
    public async Task RunAsync_PersistsDriftResultsAndCompletesRun_WhenComparerSucceeds()
    {
        // Arrange
        var run = DqtRun.Start(
            DqtTestType.ProductPairing,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            DqtTriggerType.Scheduled,
            DateTime.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _comparerMock
            .Setup(c => c.CompareAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriftComparisonResult
            {
                TotalChecked = 10,
                Mismatches = new[]
                {
                    new DriftMismatch
                    {
                        EntityKey = "P001",
                        MismatchCode = (int)ProductPairingMismatch.MissingInErp,
                        ShoptetValue = "Eshop Product"
                    }
                }
            });

        // Act
        await CreateSut().RunAsync(run.Id);

        // Assert
        _repoMock.Verify(r => r.AddDriftResultsAsync(
            It.Is<IEnumerable<DqtDriftResult>>(e => e.Count() == 1 && e.First().EntityKey == "P001"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        run.Status.Should().Be(DqtRunStatus.Completed);
        run.TotalChecked.Should().Be(10);
        run.TotalMismatches.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_FailsRun_WhenComparerThrows()
    {
        // Arrange
        var run = DqtRun.Start(
            DqtTestType.ProductPairing,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            DqtTriggerType.Scheduled,
            DateTime.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        _comparerMock
            .Setup(c => c.CompareAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("External API down"));

        // Act
        await CreateSut().RunAsync(run.Id);

        // Assert
        run.Status.Should().Be(DqtRunStatus.Failed);
        run.ErrorMessage.Should().Contain("External API down");
        _repoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_FailsRun_WhenNoComparerRegisteredForTestType()
    {
        // Arrange — run is StockWriteBack, but only ProductPairing comparer is registered
        var run = DqtRun.Start(
            DqtTestType.StockWriteBackReconciliation,
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today),
            DqtTriggerType.Scheduled,
            DateTime.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        // Act
        var act = async () => await CreateSut().RunAsync(run.Id);

        // Assert — runner must NOT propagate the exception; it must set run.Status = Failed
        await act.Should().NotThrowAsync();
        run.Status.Should().Be(DqtRunStatus.Failed);
    }
}
