using Anela.Heblo.Adapters.Flexi.Analytics;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

/// <summary>
/// On 2026-09-24 the first live run of flexi-analytics-sync failed all four entities and lost 1 260
/// ledger rows, and Hangfire recorded it as <c>Succeeded</c> — because the job only rethrew on an
/// *unhandled* exception, and an entity that fails is handled: the orchestrator catches it and
/// reports it as a count. The run therefore looked green for six hours while sync_state sat on
/// RUNNING. A job that cannot report its own failure is not monitorable.
/// </summary>
public sealed class FlexiAnalyticsSyncJobHealthTests
{
    private static IOptions<FlexiAnalyticsSyncOptions> Options(int timeoutSeconds = 30) =>
        Microsoft.Extensions.Options.Options.Create(new FlexiAnalyticsSyncOptions
        {
            Enabled = true,
            RequestTimeoutSeconds = timeoutSeconds,
        });

    private static FlexiAnalyticsSyncJob CreateJob(IFlexiAnalyticsSyncService sync, IOptions<FlexiAnalyticsSyncOptions>? opts = null) =>
        new(sync, opts ?? Options(), Mock.Of<ILogger<FlexiAnalyticsSyncJob>>());

    private static Mock<IFlexiAnalyticsSyncService> SyncReturning(FlexiAnalyticsSyncReport report)
    {
        var mock = new Mock<IFlexiAnalyticsSyncService>();
        mock.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(report);
        return mock;
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnyEntityFails_ThrowsSoHangfireRecordsTheRunAsFailed()
    {
        var sync = SyncReturning(new FlexiAnalyticsSyncReport(
            TotalFetched: 0, TotalUpserted: 0, FailedServices: 4, IsFullSuccess: false));

        var act = async () => await CreateJob(sync.Object).ExecuteAsync();

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("4", "the operator needs to know how many entities failed");
    }

    [Fact]
    public async Task ExecuteAsync_WhenOneOfFourEntitiesFails_StillFails()
    {
        // A partial failure is still a hole in the data, not a success with a warning.
        var sync = SyncReturning(new FlexiAnalyticsSyncReport(
            TotalFetched: 700, TotalUpserted: 700, FailedServices: 1, IsFullSuccess: false));

        var act = async () => await CreateJob(sync.Object).ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenEverythingSucceeds_DoesNotThrow()
    {
        var sync = SyncReturning(new FlexiAnalyticsSyncReport(
            TotalFetched: 2361, TotalUpserted: 2361, FailedServices: 0, IsFullSuccess: true));

        var act = async () => await CreateJob(sync.Object).ExecuteAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheHostIsShuttingDown_DoesNotReportAFailure()
    {
        // Hangfire cancels in-flight jobs on shutdown/redeploy. That is not a sync defect, and
        // flagging it as one would train everyone to ignore the alert.
        var sync = new Mock<IFlexiAnalyticsSyncService>();
        sync.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();

        var act = async () => await CreateJob(sync.Object).ExecuteAsync(shutdown.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenItRunsPastItsOwnTimeout_DoesReportAFailure()
    {
        // The opposite case: nothing cancelled us from outside, the job simply could not finish
        // inside RequestTimeoutSeconds. That is a real problem — a bulk touch in FlexiBee can make
        // a "delta" hundreds of thousands of rows — and it has to be visible.
        var sync = new Mock<IFlexiAnalyticsSyncService>();
        sync.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new FlexiAnalyticsSyncReport(0, 0, 0, true);
            });

        var act = async () => await CreateJob(sync.Object, Options(timeoutSeconds: 1)).ExecuteAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void Job_DoesNotAskHangfireToRetry()
    {
        // Repo convention (see ProductExportDownloadJob): rethrow so the run is recorded Failed,
        // with Attempts = 0 so a nightly job is not re-executed against a live ERP on a schedule
        // nobody chose.
        var attr = typeof(FlexiAnalyticsSyncJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .SingleOrDefault();

        attr.Should().NotBeNull();
        attr!.Attempts.Should().Be(0);
    }
}
