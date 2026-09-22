using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// The behaviour this whole direction depends on: GA4 keeps revising a day for ~48 hours, so a
/// run must re-ask for a trailing window and overwrite what it already stored.
/// </summary>
public class TrailingReprocessWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 4, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task re_pulls_the_trailing_window_instead_of_resuming_after_the_watermark()
    {
        // Arrange — a previous run left the watermark at yesterday.
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o => o.TrailingReprocessDays = 3);
        var client = new FakeGa4ReportClient(_ => Array.Empty<Ga4Row>());
        var repo = new Ga4SyncWatermarkRepository(dbContext);

        var state = await repo.GetOrCreateAsync("traffic_daily");
        state.WatermarkDate = new DateOnly(2026, 3, 9);
        await repo.SaveAsync(state);

        // Act
        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        // Assert — the window starts 3 days BEFORE the watermark, not the day after it.
        client.Requests.Should().ContainSingle();
        client.Requests[0].StartDate.Should().Be(new DateOnly(2026, 3, 6));
        client.Requests[0].EndDate.Should().Be(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public async Task upserts_a_revised_day_in_place_rather_than_appending_a_second_row()
    {
        // Arrange — first run sees GA4's partial figure for 8 March.
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o => o.TrailingReprocessDays = 3);

        var sessions = 100L;
        var client = new FakeGa4ReportClient(_ => new[] { Ga4TestHarness.TrafficRow("20260308", "Organic Search", sessions) });

        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        (await dbContext.TrafficDaily.SingleAsync()).Sessions.Should().Be(100);

        // Act — GA4 finishes reprocessing and the next run sees the corrected figure.
        sessions = 137L;
        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        // Assert — one row, corrected. Not two rows, and not still 100.
        var rows = await dbContext.TrafficDaily.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Sessions.Should().Be(137);
        rows[0].Date.Should().Be(new DateOnly(2026, 3, 8));
    }

    [Fact]
    public async Task removes_a_row_that_the_revised_report_no_longer_returns()
    {
        // Arrange — a channel that GA4 later reattributes away must not linger and inflate totals.
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o => o.TrailingReprocessDays = 3);

        var rows = new List<Ga4Row>
        {
            Ga4TestHarness.TrafficRow("20260308", "Organic Search", 100),
            Ga4TestHarness.TrafficRow("20260308", "Unassigned", 40),
        };
        var client = new FakeGa4ReportClient(_ => rows);

        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();
        (await dbContext.TrafficDaily.CountAsync()).Should().Be(2);

        // Act — reprocessing moved the Unassigned sessions into Organic Search.
        rows = new List<Ga4Row> { Ga4TestHarness.TrafficRow("20260308", "Organic Search", 140) };
        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        // Assert
        var stored = await dbContext.TrafficDaily.ToListAsync();
        stored.Should().ContainSingle();
        stored[0].ChannelGroup.Should().Be("Organic Search");
        stored[0].Sessions.Should().Be(140);
    }

    [Fact]
    public async Task first_run_backfills_from_configuration_and_stops_at_yesterday()
    {
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-01-01");
        var client = new FakeGa4ReportClient(_ => Array.Empty<Ga4Row>());

        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        client.Requests[0].StartDate.Should().Be(new DateOnly(2026, 1, 1));
        client.Requests[^1].EndDate.Should().Be(new DateOnly(2026, 3, 9), "today is always partial, so it is excluded");
    }

    [Fact]
    public async Task never_rewinds_past_the_configured_backfill_start()
    {
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o =>
        {
            o.BackfillFrom = "2026-03-08";
            o.TrailingReprocessDays = 30;
        });
        var client = new FakeGa4ReportClient(_ => Array.Empty<Ga4Row>());
        var repo = new Ga4SyncWatermarkRepository(dbContext);

        var state = await repo.GetOrCreateAsync("traffic_daily");
        state.WatermarkDate = new DateOnly(2026, 3, 9);
        await repo.SaveAsync(state);

        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        client.Requests[0].StartDate.Should().Be(new DateOnly(2026, 3, 8));
    }
}
