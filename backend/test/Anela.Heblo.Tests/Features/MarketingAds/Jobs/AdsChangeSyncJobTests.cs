using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Jobs;

public class AdsChangeSyncJobTests
{
    private readonly Mock<IAdsChangeSyncService> _service = new();

    private AdsChangeSyncJob Job(bool enabled = true, AdsSyncOptions? options = null)
    {
        var checker = new Mock<IRecurringJobStatusChecker>();
        checker.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(enabled);
        return new AdsChangeSyncJob(_service.Object, checker.Object, Options.Create(options ?? new AdsSyncOptions()), NullLogger<AdsChangeSyncJob>.Instance);
    }

    [Fact]
    public void Metadata_is_hourly()
    {
        var metadata = Job().Metadata;

        metadata.JobName.Should().Be("ads-change-sync");
        metadata.CronExpression.Should().Be("15 * * * *");
        metadata.Category.Should().Be(RecurringJobCategory.Marketing);
    }

    [Fact]
    public async Task Run_with_invalid_timeout_fails_the_run_without_calling_the_service()
    {
        var options = new AdsSyncOptions { ChangeSync = new AdsChangeSyncOptions { RunTimeoutMinutes = -1 } };

        await Job(options: options).Invoking(j => j.ExecuteAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*RunTimeoutMinutes*");

        _service.Verify(s => s.RunAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enabled_job_runs_the_service_once()
    {
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AdsSyncReport(false, null, 1, 3, Array.Empty<string>()));

        await Job().ExecuteAsync();

        _service.Verify(s => s.RunAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Many_errors_are_capped_in_the_failure_message()
    {
        var errors = Enumerable.Range(1, 25).Select(i => $"err-{i}").ToArray();
        _service.Setup(s => s.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new AdsSyncReport(false, null, 1, 0, errors));

        var thrown = await Job().Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();

        thrown.Which.Message.Should().Contain("err-10").And.NotContain("err-11").And.Contain("+15 more");
    }
}
