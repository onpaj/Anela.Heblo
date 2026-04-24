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
        _repositoryMock.Setup(r => r.AddResultsAsync(It.IsAny<IEnumerable<InvoiceDqtResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceDqtComparisonResult { TotalChecked = 10, Mismatches = new List<InvoiceDqtMismatch>() });

        // Act
        await _sut.RunAsync(run.Id);

        // Assert
        Assert.Equal(DqtRunStatus.Completed, run.Status);
        Assert.Equal(10, run.TotalChecked);
        Assert.Equal(0, run.TotalMismatches);
        Assert.Empty(run.Results);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithMismatches_AddsResultsAndCompletes()
    {
        // Arrange
        var run = CreateRun();
        _repositoryMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        _repositoryMock.Setup(r => r.AddResultsAsync(It.IsAny<IEnumerable<InvoiceDqtResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mismatches = new List<InvoiceDqtMismatch>
        {
            new() { InvoiceCode = "INV-001", MismatchType = InvoiceMismatchType.TotalWithVatDiffers, ShoptetValue = "100", FlexiValue = "101", Details = "Price differs" }
        };
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceDqtComparisonResult { TotalChecked = 5, Mismatches = mismatches });

        // Act
        await _sut.RunAsync(run.Id);

        // Assert
        Assert.Equal(DqtRunStatus.Completed, run.Status);
        Assert.Equal(5, run.TotalChecked);
        Assert.Equal(1, run.TotalMismatches);
        Assert.Single(run.Results);
        Assert.Equal("INV-001", run.Results[0].InvoiceCode);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Regression: FindAsync without Include() does not set up a change-tracking collection proxy.
    // Items added to run.Results are invisible to EF — SaveChangesAsync would not INSERT them.
    // Fix: explicitly call AddResultsAsync so EF tracks each new InvoiceDqtResult as Added.
    [Fact]
    public async Task RunAsync_WithMismatches_CallsAddResultsAsync_SoEfCanInsertThem()
    {
        // Arrange
        var run = CreateRun();
        _repositoryMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var mismatches = new List<InvoiceDqtMismatch>
        {
            new() { InvoiceCode = "INV-001", MismatchType = InvoiceMismatchType.TotalWithVatDiffers, ShoptetValue = "100", FlexiValue = "101" },
            new() { InvoiceCode = "INV-002", MismatchType = InvoiceMismatchType.TotalWithVatDiffers, ShoptetValue = "200", FlexiValue = "202" }
        };
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceDqtComparisonResult { TotalChecked = 50, Mismatches = mismatches });

        IEnumerable<InvoiceDqtResult>? capturedResults = null;
        _repositoryMock
            .Setup(r => r.AddResultsAsync(It.IsAny<IEnumerable<InvoiceDqtResult>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<InvoiceDqtResult>, CancellationToken>((results, _) => capturedResults = results)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RunAsync(run.Id);

        // Assert — AddResultsAsync called with the 2 result entities
        _repositoryMock.Verify(r => r.AddResultsAsync(It.IsAny<IEnumerable<InvoiceDqtResult>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedResults);
        var captured = capturedResults!.ToList();
        Assert.Equal(2, captured.Count);
        Assert.All(captured, r => Assert.NotEqual(Guid.Empty, r.Id));

        // run.Results mirrors the entities passed to AddResultsAsync
        Assert.Equal(2, run.Results.Count);

        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ComparerThrows_FailsRunAndSavesViaSaveChanges()
    {
        // Arrange
        var run = CreateRun();
        _repositoryMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        _comparerMock.Setup(c => c.CompareAsync(From, To, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("External service down"));

        // Act
        await _sut.RunAsync(run.Id);

        // Assert
        Assert.Equal(DqtRunStatus.Failed, run.Status);
        Assert.Equal("External service down", run.ErrorMessage);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
