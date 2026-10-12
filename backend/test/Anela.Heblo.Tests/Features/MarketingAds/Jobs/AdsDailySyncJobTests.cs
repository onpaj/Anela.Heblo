using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Jobs;

public class AdsDailySyncJobTests
{
    private readonly Mock<IAdsDailySyncService> _service = new();

    private static Mock<IRecurringJobStatusChecker> StatusChecker(bool enabled)
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(enabled);
        return mock;
    }

    private AdsDailySyncJob Job(bool enabled = true, AdsSyncOptions? options = null) => new(
        _service.Object, StatusChecker(enabled).Object, Options.Create(options ?? new AdsSyncOptions()),
        NullLogger<AdsDailySyncJob>.Instance);

    [Fact]
    public void Metadata_uses_agreed_name_schedule_and_category()
    {
        var metadata = Job().Metadata;

        metadata.JobName.Should().Be("ads-daily-sync");
        metadata.CronExpression.Should().Be("30 5 * * *");
        metadata.TimeZoneId.Should().Be("Europe/Prague");
        metadata.Category.Should().Be(RecurringJobCategory.Marketing);
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ExecuteAsync_never_retries_automatically()
    {
        var attribute = typeof(AdsDailySyncJob).GetMethod(nameof(AdsDailySyncJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), false).Cast<AutomaticRetryAttribute>().Single();

        attribute.Attempts.Should().Be(0);
    }

    [Fact]
    public void Constructing_with_invalid_options_does_not_throw()
    {
        var act = () => Job(options: new AdsSyncOptions { FactLookbackDays = 999 });

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Run_with_invalid_timeout_fails_the_run_without_calling_the_service()
    {
        var options = new AdsSyncOptions { DailySync = new AdsDailySyncOptions { RunTimeoutMinutes = 0 } };

        await Job(options: options).Invoking(j => j.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*RunTimeoutMinutes*");

        _service.Verify(s => s.RunAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Disabled_job_does_nothing()
    {
        await Job(enabled: false).ExecuteAsync();

        _service.Verify(s => s.RunAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Skipped_run_completes_quietly()
    {
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(AdsSyncReport.Skip(DisabledAdsSyncService.Reason));

        await Job().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Run_with_stream_failures_throws_so_hangfire_shows_failed()
    {
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdsSyncReport(false, null, 2, 40, new[] { "GoogleAds/1/DailyFacts: 403" }));

        await Job().Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*GoogleAds/1/DailyFacts: 403*");
    }
}
