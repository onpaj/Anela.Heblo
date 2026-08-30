using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Application.Features.DataQuality.UseCases.RunDqt;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class RunDqtHandlerTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IDqtJobRunner> _invoiceJobRunnerMock = new();
    private readonly Mock<IDqtJobRunner> _driftJobRunnerMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly RunDqtHandler _sut;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    public RunDqtHandlerTests()
    {
        _invoiceJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.IssuedInvoiceComparison)).Returns(true);
        _invoiceJobRunnerMock
            .Setup(r => r.CanHandle(It.Is<DqtTestType>(t => t != DqtTestType.IssuedInvoiceComparison)))
            .Returns(false);

        _driftJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.IssuedInvoiceComparison)).Returns(false);
        _driftJobRunnerMock
            .Setup(r => r.CanHandle(It.Is<DqtTestType>(t => t != DqtTestType.IssuedInvoiceComparison)))
            .Returns(true);

        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IEnumerable<IDqtJobRunner>)))
            .Returns(new List<IDqtJobRunner> { _invoiceJobRunnerMock.Object, _driftJobRunnerMock.Object });
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        _sut = new RunDqtHandler(
            _repositoryMock.Object,
            _scopeFactoryMock.Object,
            TimeProvider.System,
            NullLogger<RunDqtHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidRequest_SavesRunAndReturnsId()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _invoiceJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.DqtRunId);
        Assert.Null(response.ErrorCode);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DateFromAfterDateTo_ReturnsInvalidDateRangeError()
    {
        // Arrange
        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = To,
            DateTo = From  // swapped
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DqtInvalidDateRange, response.ErrorCode);
        Assert.Null(response.DqtRunId);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameDateFromAndTo_Succeeds()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _invoiceJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = From  // same date
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.DqtRunId);
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsExceptionError()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.Exception, response.ErrorCode);
        Assert.Null(response.DqtRunId);
    }

    [Fact]
    public async Task Handle_InvoiceTestType_InvokesMatchingRunnerOnly()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _invoiceJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = To
        };

        // Act
        await _sut.Handle(request, CancellationToken.None);
        await Task.Delay(100); // allow the fire-and-forget Task.Run to execute

        // Assert
        _invoiceJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _driftJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DriftTestType_InvokesMatchingRunnerOnly()
    {
        // Arrange
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _driftJobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.ProductPairing,
            DateFrom = From,
            DateTo = To
        };

        // Act
        await _sut.Handle(request, CancellationToken.None);
        await Task.Delay(100); // allow the fire-and-forget Task.Run to execute

        // Assert
        _driftJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _invoiceJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoRunnerCanHandleTestType_ReturnsUnsupportedTestTypeErrorWithoutPersisting()
    {
        // Arrange: simulate "no IDqtJobRunner registered for this TestType" by making both
        // mocks explicitly reject StockWriteBackReconciliation (overrides the constructor's
        // default wiring — Moq uses the most recently configured matching setup).
        _invoiceJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.StockWriteBackReconciliation)).Returns(false);
        _driftJobRunnerMock.Setup(r => r.CanHandle(DqtTestType.StockWriteBackReconciliation)).Returns(false);

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.StockWriteBackReconciliation,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);

        // Assert: rejected synchronously before any DqtRun is ever created.
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DqtUnsupportedTestType, response.ErrorCode);
        Assert.Null(response.DqtRunId);
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _invoiceJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _driftJobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RunnerLookupThrowsInsideFireAndForgetTask_FailsTheRun()
    {
        // Arrange: both runners pass CanHandle at the synchronous pre-check (so the run IS
        // persisted), but the fire-and-forget task's own lookup throws — simulating a runner
        // deregistered/misbehaving between the pre-check and the background task running.
        // We force this by having the scope factory return a *second*, different scope on the
        // second CreateScope() call (the pre-check consumes the first) whose service provider
        // has an empty runner list.
        var run = default(DqtRun);
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun r, CancellationToken _) => { run = r; return r; });
        _repositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => run);
        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var emptyScopeMock = new Mock<IServiceScope>();
        var emptyProviderMock = new Mock<IServiceProvider>();
        emptyProviderMock.Setup(sp => sp.GetService(typeof(IEnumerable<IDqtJobRunner>)))
            .Returns(new List<IDqtJobRunner>());
        emptyProviderMock.Setup(sp => sp.GetService(typeof(IDqtRunRepository)))
            .Returns(_repositoryMock.Object);
        emptyScopeMock.Setup(s => s.ServiceProvider).Returns(emptyProviderMock.Object);

        var callCount = 0;
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                // First call: the synchronous pre-check scope — return the normal wired scope
                // so the pre-check sees a matching runner and the run gets persisted.
                var scopeMock = new Mock<IServiceScope>();
                var providerMock = new Mock<IServiceProvider>();
                providerMock.Setup(sp => sp.GetService(typeof(IEnumerable<IDqtJobRunner>)))
                    .Returns(new List<IDqtJobRunner> { _invoiceJobRunnerMock.Object });
                scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);
                return scopeMock.Object;
            }
            // Second call: the fire-and-forget task's own scope — empty runner list, so its
            // internal lookup throws InvalidOperationException before RunAsync is reached.
            return emptyScopeMock.Object;
        });

        var request = new RunDqtRequest
        {
            TestType = DqtTestType.IssuedInvoiceComparison,
            DateFrom = From,
            DateTo = To
        };

        // Act
        var response = await _sut.Handle(request, CancellationToken.None);
        await Task.Delay(100); // allow the fire-and-forget Task.Run to run its catch block

        // Assert: Handle() itself still reports success (the run was legitimately accepted —
        // the failure happens asynchronously), but the run is now recorded as Failed instead
        // of being stuck in Running forever with no diagnostic trail.
        Assert.True(response.Success);
        Assert.NotNull(run);
        Assert.Equal(DqtRunStatus.Failed, run!.Status);
        Assert.Contains("IssuedInvoiceComparison", run.ErrorMessage);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
