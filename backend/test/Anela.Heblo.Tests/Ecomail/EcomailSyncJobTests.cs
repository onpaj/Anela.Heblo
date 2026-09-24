using Anela.Heblo.Application.Features.Ecomail.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Ecomail;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailSyncJobTests
{
    private static Mock<IRecurringJobStatusChecker> StatusChecker(bool enabled)
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(enabled);
        return mock;
    }

    private static EcomailSyncJob CreateJob(Mock<IEcomailSyncService> service, EcomailOptions options, bool jobEnabled = true)
        => new(service.Object, StatusChecker(jobEnabled).Object, Options.Create(options), NullLogger<EcomailSyncJob>.Instance);

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
    public void default_is_enabled_regardless_of_api_key()
    {
        // DefaultIsEnabled must not depend on the API key: the seeder persists this value on
        // first run, so seeding with an empty key would otherwise permanently record the job as
        // disabled, and it would never start once the secret is added without a manual DB fix.
        var job = CreateJob(SucceedingService(), new EcomailOptions { ApiKey = "" });

        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task skips_execution_without_an_api_key()
    {
        // The empty-API-key check is the runtime no-op that protects a developer with no
        // credentials — DefaultIsEnabled no longer does that job.
        var service = SucceedingService();
        var job = CreateJob(service, new EcomailOptions { ApiKey = "" });

        await job.ExecuteAsync();

        service.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task skips_execution_when_disabled_via_the_admin_status_checker()
    {
        var service = SucceedingService();
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" }, jobEnabled: false);

        await job.ExecuteAsync();

        service.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the admin enable/disable toggle must actually stop the job, not just the API-key check");
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
    public async Task throws_when_no_data_landed_even_though_nothing_threw()
    {
        // The guard no longer requires !report.IsFullSuccess: a run that reports zero real data
        // with an empty Errors list (e.g. every endpoint returned an empty/unexpected payload
        // without raising an HTTP error, such as a 403 that got swallowed) must still throw.
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(
                CampaignsUpserted: 0,
                PipelinesUpserted: 0,
                SnapshotsWritten: 0,
                AutomationMonthsComputed: 0,
                CampaignStatsFetched: 0,
                Errors: Array.Empty<string>()));
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        var act = () => job.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a run that reports success while capturing nothing is exactly the failure mode this guard exists to catch");
    }

    [Fact]
    public async Task throws_when_the_campaign_listing_produced_nothing_while_pipelines_listed()
    {
        // The production failure of 2026-09-24: one null field broke the campaigns listing, the
        // service caught it and returned zero campaigns, and because snapshots and months still
        // landed the all-counters-zero guard never fired. The run went green with every newsletter
        // missing. Pipelines listing fine while campaigns yield nothing is not a real state for
        // this account — it only happens when the campaigns stage failed.
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(
                CampaignsUpserted: 0,
                PipelinesUpserted: 4,
                SnapshotsWritten: 4,
                AutomationMonthsComputed: 48,
                CampaignStatsFetched: 0,
                Errors: new[] { "campaigns: The JSON value could not be converted to System.Int32." }));
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        var act = () => job.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*System.Int32*",
                "a run that loses every newsletter must not report success");
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
