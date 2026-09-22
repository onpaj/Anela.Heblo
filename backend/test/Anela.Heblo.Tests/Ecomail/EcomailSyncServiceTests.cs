using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Ecomail;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailSyncServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 3, 0, 0, DateTimeKind.Unspecified);

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ecomail-sync-{Guid.NewGuid()}")
            .Options);

    private static EcomailStatsDto Stats(int conversions = 0, decimal value = 0) => new()
    {
        Inject = 100,
        Delivery = 99,
        Open = 25,
        TotalOpen = 40,
        Click = 5,
        TotalClick = 7,
        Unsub = 2,
        Bounce = 1,
        Spam = 0,
        Conversions = conversions,
        ConversionsValue = value,
        Triggered = 500,
        Ended = 480,
        Send = 450,
    };

    private static EcomailSyncService CreateService(
        ApplicationDbContext context,
        Mock<IEcomailApiClient> api,
        EcomailOptions? options = null,
        DateTime? now = null)
    {
        var opts = options ?? new EcomailOptions
        {
            ApiKey = "k",
            RecomputeWindowMonths = 2,
            BackfillFrom = new DateOnly(2026, 8, 1),
        };

        return new EcomailSyncService(
            api.Object,
            new EcomailRepository(context),
            Options.Create(opts),
            new FakeTimeProvider(now ?? Now),
            NullLogger<EcomailSyncService>.Instance);
    }

    private static Mock<IEcomailApiClient> ApiWith(
        IEnumerable<EcomailCampaignDto>? campaigns = null,
        IEnumerable<EcomailPipelineDto>? pipelines = null)
    {
        var api = new Mock<IEcomailApiClient>();
        api.Setup(a => a.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((campaigns ?? Array.Empty<EcomailCampaignDto>()).ToList());
        api.Setup(a => a.GetPipelinesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((pipelines ?? Array.Empty<EcomailPipelineDto>()).ToList());
        api.Setup(a => a.GetCampaignStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(4, 6057m));
        api.Setup(a => a.GetPipelineStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(150, 314708.90m));
        api.Setup(a => a.GetPipelineEventCountAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        return api;
    }

    private static EcomailCampaignDto Campaign(int id, string type, int status = 3) => new()
    {
        Id = id,
        Title = $"c{id}",
        Subject = "s",
        CampaignType = type,
        Status = status,
        Recipients = 1000,
        SentAt = new DateTime(2026, 8, 15, 6, 0, 0),
    };

    [Fact]
    public async Task stores_every_campaign_including_variations_and_drafts()
    {
        using var context = CreateContext();
        var api = ApiWith(campaigns: new[]
        {
            Campaign(264, "ab"), Campaign(266, "variation"),
            Campaign(300, "email"), Campaign(301, "email", status: 0), Campaign(302, "sms"),
        });

        await CreateService(context, api).SyncAllAsync();

        context.EcomailCampaigns.Should().HaveCount(5,
            "raw rows stay complete for traceability; the reporting rule filters at read time");
    }

    [Fact]
    public async Task fetches_stats_only_for_reportable_campaigns()
    {
        using var context = CreateContext();
        var api = ApiWith(campaigns: new[]
        {
            Campaign(264, "ab"), Campaign(266, "variation"),
            Campaign(301, "email", status: 0), Campaign(302, "sms"),
        });

        await CreateService(context, api).SyncAllAsync();

        api.Verify(a => a.GetCampaignStatsAsync(264, It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(a => a.GetCampaignStatsAsync(266, It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(a => a.GetCampaignStatsAsync(301, It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(a => a.GetCampaignStatsAsync(302, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task rerunning_on_the_same_day_updates_rather_than_duplicates()
    {
        using var context = CreateContext();
        var api = ApiWith(
            campaigns: new[] { Campaign(264, "ab") },
            pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });

        var service = CreateService(context, api);
        await service.SyncAllAsync();
        await service.SyncAllAsync();

        context.EcomailCampaigns.Should().ContainSingle();
        context.EcomailAutomationSnapshots.Should().ContainSingle(
            "one snapshot per automation per day, so a 6-hourly job stays idempotent");
    }

    [Fact]
    public async Task writes_the_cumulative_counters_into_the_snapshot()
    {
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });

        await CreateService(context, api).SyncAllAsync();

        var snapshot = context.EcomailAutomationSnapshots.Single();
        snapshot.PipelineId.Should().Be(14720);
        snapshot.CapturedOn.Should().Be(DateOnly.FromDateTime(Now));
        snapshot.Conversions.Should().Be(150);
        snapshot.ConversionsValue.Should().Be(314708.90m);
        snapshot.Triggered.Should().Be(500);
    }

    [Fact]
    public async Task computes_automation_months_across_the_backfill_range()
    {
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });

        await CreateService(context, api).SyncAllAsync();

        // BackfillFrom 2026-08 through the current month 2026-09 = 2 months.
        var months = context.EcomailAutomationMonths.OrderBy(m => m.Month).ToList();
        months.Should().HaveCount(2);
        months[0].Year.Should().Be(2026);
        months[0].Month.Should().Be(8);
        months[0].Open.Should().Be(11);
        months[0].Send.Should().Be(11);
    }

    [Fact]
    public async Task does_not_recompute_a_locked_month()
    {
        using var context = CreateContext();
        context.EcomailAutomationMonths.Add(new EcomailAutomationMonth
        {
            PipelineId = 14720,
            Year = 2026,
            Month = 8,
            Send = 999,
            Open = 999,
            IsLocked = true,
            ComputedAt = Now.AddMonths(-1),
        });
        await context.SaveChangesAsync();

        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });
        await CreateService(context, api).SyncAllAsync();

        var august = context.EcomailAutomationMonths.Single(m => m.Month == 8);
        august.Open.Should().Be(999, "a locked month is only touched by an explicit recompute");
    }

    [Fact]
    public async Task one_failing_campaign_does_not_abort_the_run()
    {
        using var context = CreateContext();
        var api = ApiWith(campaigns: new[] { Campaign(264, "ab"), Campaign(300, "email") });
        api.Setup(a => a.GetCampaignStatsAsync(264, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var report = await CreateService(context, api).SyncAllAsync();

        report.IsFullSuccess.Should().BeFalse();
        report.Errors.Should().ContainSingle().Which.Should().Contain("264");
        context.EcomailCampaigns.Single(c => c.Id == 300).Open.Should().Be(25,
            "the healthy campaign is still persisted");
    }

    [Fact]
    public async Task campaign_stats_fetched_counts_only_stats_that_were_actually_applied()
    {
        using var context = CreateContext();
        var api = ApiWith(campaigns: new[] { Campaign(264, "ab"), Campaign(300, "email") });
        api.Setup(a => a.GetCampaignStatsAsync(264, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var report = await CreateService(context, api).SyncAllAsync();

        report.CampaignsUpserted.Should().Be(2,
            "the metadata counter still counts every listed campaign, including the one whose stats call failed");
        report.CampaignStatsFetched.Should().Be(1,
            "only the campaign whose stats were actually applied counts as real data landing");
    }

    [Fact]
    public async Task falls_back_to_known_pipeline_ids_when_listing_fails_so_snapshots_still_run()
    {
        using var context = CreateContext();
        context.EcomailPipelines.Add(new EcomailPipeline
        {
            Id = 14720,
            Name = "Kosik",
            SyncedAt = Now.AddDays(-1),
        });
        await context.SaveChangesAsync();

        var api = ApiWith();
        api.Setup(a => a.GetPipelinesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var report = await CreateService(context, api).SyncAllAsync();

        report.Errors.Should().ContainSingle().Which.Should().Contain("pipelines");
        context.EcomailAutomationSnapshots.Should().ContainSingle(
            "the listing call failed, but pipeline 14720 is already known from our own database, " +
            "so today's unbackfillable snapshot must still be captured, not silently dropped");
    }

    [Fact]
    public async Task failing_campaign_listing_does_not_abort_pipelines_and_snapshots()
    {
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });
        api.Setup(a => a.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var report = await CreateService(context, api).SyncAllAsync();

        report.Errors.Should().ContainSingle().Which.Should().Contain("campaigns");
        context.EcomailAutomationSnapshots.Should().ContainSingle(
            "a transient campaigns-listing failure must not cost an automation snapshot day, " +
            "since Ecomail exposes lifetime counters only and a missed day can never be backfilled");
    }

    [Fact]
    public async Task backfill_from_mid_month_is_normalised_to_calendar_month_boundaries()
    {
        const int EventsPerMonth = 4; // send, open, click, unsub

        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });
        var options = new EcomailOptions
        {
            ApiKey = "k",
            RecomputeWindowMonths = 2,
            BackfillFrom = new DateOnly(2026, 8, 15),
        };

        await CreateService(context, api, options).SyncAllAsync();

        // Without normalisation the window would run Aug 15 -> Sep 14 and stop after one
        // iteration entirely (Sep 15 > the Sep 1 loop bound), never reaching a true September window.
        api.Verify(a => a.GetPipelineEventCountAsync(
                14720, It.IsAny<string>(), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), It.IsAny<CancellationToken>()),
            Times.Exactly(EventsPerMonth),
            "August's window must span the whole calendar month");
        api.Verify(a => a.GetPipelineEventCountAsync(
                14720, It.IsAny<string>(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), It.IsAny<CancellationToken>()),
            Times.Exactly(EventsPerMonth),
            "September must still be reached and span the whole calendar month");
    }

    [Fact]
    public async Task first_time_automation_month_failure_still_persists_a_row_with_last_error()
    {
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });
        api.Setup(a => a.GetPipelineEventCountAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var report = await CreateService(context, api).SyncAllAsync();

        report.IsFullSuccess.Should().BeFalse();
        var months = context.EcomailAutomationMonths.ToList();
        months.Should().HaveCount(2,
            "both backfill months (Aug, Sep) must get a durable row even though their first computation failed");
        months.Should().OnlyContain(m => m.LastError != null,
            "LastError is the only discriminator between a failed month and a genuinely zero-event one");
        months.Should().OnlyContain(m => !m.IsLocked, "an unlocked month is retried on the next run");
        months.Should().OnlyContain(m => m.Send == 0 && m.Open == 0 && m.Click == 0 && m.Unsub == 0);
    }

    [Fact]
    public async Task a_historic_month_that_failed_is_retried_rather_than_locked_at_zero()
    {
        // A transient blip partway through a long backfill writes a failure placeholder: all
        // counts zero, LastError set, IsLocked false so the next run retries it. If the lock
        // decision looked only at the recompute window, the next run would freeze that placeholder
        // forever and the month would read as "nothing was sent" with no path back.
        using var context = CreateContext();
        var options = new EcomailOptions
        {
            ApiKey = "k",
            RecomputeWindowMonths = 2,
            BackfillFrom = new DateOnly(2026, 3, 1),
        };
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });

        var march = new DateOnly(2026, 3, 1);
        api.Setup(a => a.GetPipelineEventCountAsync(
                It.IsAny<int>(), It.IsAny<string>(), march, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        await CreateService(context, api, options).SyncAllAsync();

        var failed = context.EcomailAutomationMonths.Single(m => m.Year == 2026 && m.Month == 3);
        failed.LastError.Should().NotBeNull();
        failed.IsLocked.Should().BeFalse("a month that never computed must stay retryable");

        // Second run, Ecomail healthy again. March is six months back — well outside the
        // two-month recompute window — so this is exactly the path that used to lock it.
        api.Setup(a => a.GetPipelineEventCountAsync(
                It.IsAny<int>(), It.IsAny<string>(), march, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);

        await CreateService(context, api, options).SyncAllAsync();

        var healed = context.EcomailAutomationMonths.Single(m => m.Year == 2026 && m.Month == 3);
        healed.LastError.Should().BeNull();
        healed.Send.Should().Be(11, "the retry must overwrite the placeholder's zeros");
        healed.IsLocked.Should().BeTrue("only now, with a real number in it, may the month be frozen");
    }

    [Fact]
    public async Task an_unsupported_event_fails_the_month_instead_of_recording_a_plausible_zero()
    {
        // Ecomail answers an event it does not support with total:null. Recording that as 0 and
        // locking the month would pin a wrong number that looks entirely plausible.
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });
        api.Setup(a => a.GetPipelineEventCountAsync(
                It.IsAny<int>(), "unsub", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var report = await CreateService(context, api).SyncAllAsync();

        report.IsFullSuccess.Should().BeFalse();
        var months = context.EcomailAutomationMonths.ToList();
        months.Should().NotBeEmpty();
        months.Should().OnlyContain(m => m.LastError != null,
            "an unknown count is a failure, not a zero");
        months.Should().OnlyContain(m => !m.IsLocked,
            "a month whose counts are unknown must never be frozen");
    }

    [Fact]
    public async Task an_empty_pipeline_listing_falls_back_to_known_ids_so_snapshots_still_run()
    {
        // The throwing shape of this failure is covered above. This is the silent shape: an empty
        // list with no exception, which would otherwise end snapshot collection on a green run.
        using var context = CreateContext();
        context.EcomailPipelines.Add(new EcomailPipeline
        {
            Id = 14720,
            Name = "Kosik",
            SyncedAt = Now.AddDays(-1),
        });
        await context.SaveChangesAsync();

        var api = ApiWith();

        var report = await CreateService(context, api).SyncAllAsync();

        report.Errors.Should().ContainSingle().Which.Should().Contain("pipelines");
        context.EcomailAutomationSnapshots.Should().ContainSingle(
            "an empty listing while pipelines are already known is never legitimate");
    }

    [Fact]
    public async Task the_next_day_appends_a_snapshot_and_leaves_yesterdays_untouched()
    {
        // Monthly automation conversions exist nowhere else — they are derived by differencing
        // consecutive snapshots. That only works if each day appends a row rather than updating
        // the previous one, which same-day dedup alone does not prove.
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });
        api.Setup(a => a.GetPipelineStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(150, 314708.90m));

        await CreateService(context, api).SyncAllAsync();

        api.Setup(a => a.GetPipelineStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(162, 330000m));

        await CreateService(context, api, now: Now.AddDays(1)).SyncAllAsync();

        var snapshots = context.EcomailAutomationSnapshots.OrderBy(x => x.CapturedOn).ToList();
        snapshots.Should().HaveCount(2, "each day appends; yesterday's row is never overwritten");
        snapshots[0].CapturedOn.Should().Be(DateOnly.FromDateTime(Now));
        snapshots[1].CapturedOn.Should().Be(DateOnly.FromDateTime(Now.AddDays(1)));
        snapshots[0].Conversions.Should().Be(150, "yesterday's cumulative counter must be preserved");
        snapshots[1].Conversions.Should().Be(162);

        // The whole point of the storage shape: the delta is the day's conversions.
        (snapshots[1].Conversions - snapshots[0].Conversions).Should().Be(12);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTime _now;
        public FakeTimeProvider(DateTime now) => _now = now;
        public override DateTimeOffset GetUtcNow() => new(_now, TimeSpan.Zero);
    }
}
