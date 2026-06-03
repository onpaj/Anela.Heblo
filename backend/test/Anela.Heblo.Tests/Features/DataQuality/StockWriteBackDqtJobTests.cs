using Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class StockWriteBackDqtJobTests
{
    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IDriftDqtJobRunner> _jobRunnerMock = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock = new();
    private readonly StockWriteBackDqtJob _sut;

    public StockWriteBackDqtJobTests()
    {
        _sut = new StockWriteBackDqtJob(
            _repositoryMock.Object,
            _jobRunnerMock.Object,
            _statusCheckerMock.Object,
            NullLogger<StockWriteBackDqtJob>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var calls = new List<string>();

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("AddAsync"))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);

        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("SaveChangesAsync"))
            .ReturnsAsync(1);

        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("RunAsync"))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        Assert.Equal(new[] { "AddAsync", "SaveChangesAsync", "RunAsync" }, calls);

        _repositoryMock.Verify(
            r => r.AddAsync(
                It.Is<DqtRun>(run =>
                    run.TestType == DqtTestType.StockWriteBackReconciliation &&
                    run.TriggerType == DqtTriggerType.Scheduled &&
                    run.Status == DqtRunStatus.Running),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_JobDisabled_DoesNotPersistOrInvokeRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _jobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationTokenToSaveChanges()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DqtRun run, CancellationToken _) => run);
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _jobRunnerMock
            .Setup(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        // Act
        await _sut.ExecuteAsync(token);

        // Assert
        _repositoryMock.Verify(r => r.SaveChangesAsync(token), Times.Once);
    }
}
