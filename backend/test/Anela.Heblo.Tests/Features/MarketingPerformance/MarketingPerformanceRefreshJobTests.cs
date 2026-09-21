using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceRefreshJobTests
{
    private readonly Mock<IMarketingPerformanceRefreshService> _service = new();
    private readonly MarketingPerformanceRunGuard _guard = new();

    private static Mock<IRecurringJobStatusChecker> StatusChecker(bool enabled)
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(enabled);
        return mock;
    }

    private MarketingPerformanceRefreshJob Job(bool enabled = true, string cron = "0 5 * * *") => new(
        _service.Object, _guard, StatusChecker(enabled).Object,
        Options.Create(new MarketingPerformanceOptions { CronExpression = cron }),
        NullLogger<MarketingPerformanceRefreshJob>.Instance);

    [Fact]
    public void Metadata_UsesAgreedNameAndConfiguredCron()
    {
        var metadata = Job(cron: "30 6 * * *").Metadata;
        metadata.JobName.Should().Be("marketing-performance-refresh");
        metadata.CronExpression.Should().Be("30 6 * * *");
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Disabled_DoesNothing()
    {
        await Job(enabled: false).ExecuteAsync();
        _service.Verify(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Enabled_RefreshesWindow_AndReleasesGuard()
    {
        _service.Setup(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshRunResult { Months = { new MonthRefreshOutcome { Month = new YearMonth(2026, 9), RevenueOk = true, CostsOk = true } } });

        await Job().ExecuteAsync();

        _service.Verify(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()), Times.Once);
        _guard.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AllMonthsFailed_Throws_SoHangfireRetries()
    {
        _service.Setup(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshRunResult { Months = { new MonthRefreshOutcome { Month = new YearMonth(2026, 9), Error = "x" } } });

        var act = () => Job().ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*every month*");
        _guard.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnotherRunIsActive_Skips()
    {
        _guard.TryBegin().Should().BeTrue();
        await Job().ExecuteAsync();
        _service.Verify(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()), Times.Never);
        _guard.End();
    }

    [Fact]
    public async Task RecomputeJob_RunsRangeAndReleasesGuard()
    {
        _service.Setup(s => s.RecomputeRangeAsync(new YearMonth(2023, 1), new YearMonth(2023, 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshRunResult());
        var job = new MarketingPerformanceRecomputeJob(_service.Object, _guard, NullLogger<MarketingPerformanceRecomputeJob>.Instance);

        await job.RunAsync(2023, 1, 2023, 3, CancellationToken.None);

        _service.VerifyAll();
        _guard.IsRunning.Should().BeFalse();
    }
}
