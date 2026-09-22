using Anela.Heblo.Application.Features.Ecomail.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.Ecomail;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailSyncJobTests
{
    private static EcomailSyncJob CreateJob(Mock<IEcomailSyncService> service, EcomailOptions options)
        => new(service.Object, Options.Create(options), NullLogger<EcomailSyncJob>.Instance);

    private static Mock<IEcomailSyncService> SucceedingService()
    {
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(5, 2, 2, 4, 3, Array.Empty<string>()));
        return service;
    }

    [Fact]
    public void exposes_metadata_driven_by_configuration()
    {
        var options = new EcomailOptions { ApiKey = "k", CronExpression = "0 */6 * * *", TimeZone = "Europe/Prague" };

        var job = CreateJob(SucceedingService(), options);

        job.Metadata.JobName.Should().Be("ecomail-sync");
        job.Metadata.CronExpression.Should().Be("0 */6 * * *");
        job.Metadata.TimeZoneId.Should().Be("Europe/Prague");
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void is_disabled_by_default_when_no_api_key_is_configured()
    {
        var job = CreateJob(SucceedingService(), new EcomailOptions { ApiKey = "" });

        job.Metadata.DefaultIsEnabled.Should().BeFalse(
            "a developer without Ecomail credentials must not get a failing scheduled job");
    }

    [Fact]
    public async Task skips_execution_without_an_api_key()
    {
        var service = SucceedingService();
        var job = CreateJob(service, new EcomailOptions { ApiKey = "" });

        await job.ExecuteAsync();

        service.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task runs_the_sync_when_configured()
    {
        var service = SucceedingService();
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        await job.ExecuteAsync();

        service.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task throws_when_every_part_of_the_run_failed()
    {
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(0, 0, 0, 0, 0, new[] { "pipelines: boom" }));
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        var act = () => job.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*boom*", "a silent failure is how a dead pipeline goes unnoticed for months");
    }

    [Fact]
    public async Task throws_when_metadata_was_upserted_but_no_real_data_landed()
    {
        // CampaignsUpserted and PipelinesUpserted are metadata counters: they increment as soon as
        // a campaign/pipeline is listed, before any stats call. A run where every stats/snapshot/
        // event-count call then fails must still be treated as a failure, not slip through green.
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(
                CampaignsUpserted: 5,
                PipelinesUpserted: 2,
                SnapshotsWritten: 0,
                AutomationMonthsComputed: 0,
                CampaignStatsFetched: 0,
                Errors: new[] { "campaign 1: boom", "pipeline 2 snapshot: boom" }));
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        var act = () => job.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "metadata upserts are not data — this is the exact scenario that must not slip through green");
    }

    [Fact]
    public async Task does_not_throw_on_a_genuine_partial_success()
    {
        // A real data counter is positive (SnapshotsWritten), everything else is zero, and there
        // are still errors from other parts of the run. This pins the boundary so a later change
        // cannot quietly make the guard stricter or looser without a test failing.
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(
                CampaignsUpserted: 0,
                PipelinesUpserted: 0,
                SnapshotsWritten: 3,
                AutomationMonthsComputed: 0,
                CampaignStatsFetched: 0,
                Errors: new[] { "campaigns: boom" }));
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        var act = () => job.ExecuteAsync();

        await act.Should().NotThrowAsync("snapshots were actually written, so this is a partial success, not a dead run");
    }
}
