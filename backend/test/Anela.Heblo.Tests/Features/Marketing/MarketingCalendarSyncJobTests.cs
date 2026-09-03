using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Marketing.Configuration;
using Anela.Heblo.Application.Features.Marketing.Contracts;
using Anela.Heblo.Application.Features.Marketing.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Marketing.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Marketing;

public sealed class MarketingCalendarSyncJobTests
{
    private readonly Mock<IMarketingCalendarSyncService> _syncServiceMock = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusCheckerMock = new();
    private readonly Mock<ILogger<MarketingCalendarSyncJob>> _loggerMock = new();

    public MarketingCalendarSyncJobTests()
    {
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync("marketing-calendar-sync", It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(true);

        _syncServiceMock
            .Setup(s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportFromOutlookResponse { Created = 1, Updated = 2, Deleted = 3, Skipped = 4, Failed = 0 });
    }

    private MarketingCalendarSyncJob CreateJob(string groupId = "marketing@example.com")
    {
        return new MarketingCalendarSyncJob(
            _syncServiceMock.Object,
            _statusCheckerMock.Object,
            Options.Create(new MarketingCalendarOptions { GroupId = groupId }),
            _loggerMock.Object);
    }

    [Fact]
    public void Metadata_DescribesHourlySyncJob()
    {
        // Arrange / Act
        var metadata = CreateJob().Metadata;

        // Assert
        metadata.JobName.Should().Be("marketing-calendar-sync");
        metadata.CronExpression.Should().Be("0 * * * *");
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenJobDisabled_DoesNotSync()
    {
        // Arrange
        _statusCheckerMock
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _syncServiceMock.Verify(
            s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGroupIdBlank_DoesNotSync()
    {
        // Act
        await CreateJob(groupId: "  ").ExecuteAsync(CancellationToken.None);

        // Assert
        _syncServiceMock.Verify(
            s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_SyncsExpectedWindowAsSystemActor()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        var after = DateTime.UtcNow;
        _syncServiceMock.Verify(
            s => s.SyncAsync(
                It.Is<DateTime>(from =>
                    from >= before.AddDays(-MarketingCalendarSyncJob.PastDays) &&
                    from <= after.AddDays(-MarketingCalendarSyncJob.PastDays)),
                It.Is<DateTime>(to =>
                    to >= before.AddMonths(MarketingCalendarSyncJob.FutureMonths) &&
                    to <= after.AddMonths(MarketingCalendarSyncJob.FutureMonths)),
                SyncActor.System,
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_LogsCounts()
    {
        // Act
        await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("1 created, 2 updated, 3 deleted, 4 skipped, 0 failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSyncThrows_Propagates()
    {
        // Arrange — Hangfire must see the failure
        _syncServiceMock
            .Setup(s => s.SyncAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<SyncActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph down"));

        // Act
        var act = async () => await CreateJob().ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Graph down");
    }
}
