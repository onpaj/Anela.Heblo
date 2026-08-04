using Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class LotStockReconciliationDqtJobTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 2, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly ExpectedDate = new(2026, 8, 2);

    private readonly Mock<IDqtRunRepository> _repositoryMock = new();
    private readonly Mock<IDriftDqtJobRunner> _jobRunnerMock = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly LotStockReconciliationDqtJob _sut;

    public LotStockReconciliationDqtJobTests()
    {
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);

        _sut = new LotStockReconciliationDqtJob(
            _repositoryMock.Object,
            _jobRunnerMock.Object,
            _statusCheckerMock.Object,
            _timeProviderMock.Object,
            NullLogger<LotStockReconciliationDqtJob>.Instance);
    }

    [Fact]
    public void Metadata_UsesExpectedJobNameAndSchedule()
    {
        _sut.Metadata.JobName.Should().Be("daily-lot-stock-dqt");
        _sut.Metadata.CronExpression.Should().Be("0 8 * * *");
        _sut.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>(), true))
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
                    run.TestType == DqtTestType.LotSumVsErpStock &&
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
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(false);

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<DqtRun>(), It.IsAny<CancellationToken>()), Times.Never);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _jobRunnerMock.Verify(j => j.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(_sut.Metadata.JobName, It.IsAny<CancellationToken>(), true))
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

        // Act
        await _sut.ExecuteAsync(CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.AddAsync(
                It.Is<DqtRun>(run => run.DateFrom == ExpectedDate && run.DateTo == ExpectedDate),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
