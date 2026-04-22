using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class InvoiceDqtJobRunnerTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IInvoiceDqtComparer> _comparerMock = new();
    private readonly InvoiceDqtJobRunner _sut;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    public InvoiceDqtJobRunnerTests()
    {
        _sut = new InvoiceDqtJobRunner(
            _repositoryMock.Object,
            _comparerMock.Object,
            NullLogger<InvoiceDqtJobRunner>.Instance);
    }

    private DqtRun CreateRun()
        => DqtRun.Start(DqtTestType.IssuedInvoiceComparison, From, To, DqtTriggerType.Manual);

    [Fact]
    public async Task RunAsync_RunNotFound_LogsAndReturns()
    {
        // Arrange
        var runId = Guid.NewGuid();
        _repositoryMock.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun?)null);

        // Act
        await _sut.RunAsync(runId);

        // Assert — comparer never called, no updates
        _comparerMock.Verify(c => c.CompareAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_NoMismatches_CompletesRunWithZeroMismatches()
    {
        // Arrange
        var run = CreateRun();
        _repositoryMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = new InvoiceDqtComparisonResult
        {
            TotalChecked = 10,
            Mismatches = new List<InvoiceDqtMismatch>()
        };
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RunAsync(run.Id);

        // Assert
        Assert.Equal(DqtRunStatus.Completed, run.Status);
        Assert.Equal(10, run.TotalChecked);
        Assert.Equal(0, run.TotalMismatches);
        Assert.Empty(run.Results);
        _repositoryMock.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithMismatches_AddsResultsAndCompletes()
    {
        // Arrange
        var run = CreateRun();
        _repositoryMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var mismatches = new List<InvoiceDqtMismatch>
        {
            new() { InvoiceCode = "INV-001", MismatchType = InvoiceMismatchType.TotalWithVatDiffers, ShoptetValue = "100", FlexiValue = "101", Details = "Price differs" }
        };
        var result = new InvoiceDqtComparisonResult { TotalChecked = 5, Mismatches = mismatches };

        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RunAsync(run.Id);

        // Assert
        Assert.Equal(DqtRunStatus.Completed, run.Status);
        Assert.Equal(5, run.TotalChecked);
        Assert.Equal(1, run.TotalMismatches);
        Assert.Single(run.Results);
        Assert.Equal("INV-001", run.Results[0].InvoiceCode);
    }

    [Fact]
    public async Task RunAsync_ComparerThrows_FailsRunAndUpdates()
    {
        // Arrange
        var run = CreateRun();
        _repositoryMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("External service down"));
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RunAsync(run.Id);

        // Assert
        Assert.Equal(DqtRunStatus.Failed, run.Status);
        _repositoryMock.Verify(r => r.UpdateAsync(run, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunForDateRangeAsync_CreatesRunAddsAndExecutes_ReturnsRunId()
    {
        // Arrange
        var result = new InvoiceDqtComparisonResult
        {
            TotalChecked = 7,
            Mismatches = new List<InvoiceDqtMismatch>()
        };
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun r, CancellationToken _) => r);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var runId = await _sut.RunForDateRangeAsync(From, To, DqtTriggerType.Scheduled);

        // Assert
        Assert.NotEqual(Guid.Empty, runId);
        _repositoryMock.Verify(r => r.AddAsync(It.Is<DqtRun>(r =>
            r.DateFrom == From && r.DateTo == To && r.TriggerType == DqtTriggerType.Scheduled), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunForDateRangeAsync_CompletesRun_StatusIsCompleted()
    {
        // Arrange
        var mismatches = new List<InvoiceDqtMismatch>
        {
            new() { InvoiceCode = "INV-999", MismatchType = InvoiceMismatchType.TotalWithVatDiffers, ShoptetValue = "200", FlexiValue = "205", Details = "Differs" }
        };
        var result = new InvoiceDqtComparisonResult { TotalChecked = 3, Mismatches = mismatches };
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun r, CancellationToken _) => r);

        DqtRun? capturedRun = null;
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Callback<DqtRun, CancellationToken>((r, _) => capturedRun = r)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RunForDateRangeAsync(From, To, DqtTriggerType.Scheduled);

        // Assert
        Assert.NotNull(capturedRun);
        Assert.Equal(DqtRunStatus.Completed, capturedRun!.Status);
        Assert.Equal(3, capturedRun.TotalChecked);
        Assert.Equal(1, capturedRun.TotalMismatches);
    }

    [Fact]
    public async Task RunForDateRangeAsync_ComparerThrows_FailsRun()
    {
        // Arrange
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun r, CancellationToken _) => r);

        DqtRun? capturedRun = null;
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Callback<DqtRun, CancellationToken>((r, _) => capturedRun = r)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RunForDateRangeAsync(From, To, DqtTriggerType.Scheduled);

        // Assert
        Assert.NotNull(capturedRun);
        Assert.Equal(DqtRunStatus.Failed, capturedRun!.Status);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
