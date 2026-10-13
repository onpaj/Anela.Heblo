using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// What happens when GA4 answers, but answers badly. Each case here is a way a report can come
/// back degraded without erroring, which is how a sync quietly destroys or distorts stored data.
/// </summary>
public class DegradedReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 4, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task refuses_to_delete_a_stored_window_when_the_report_comes_back_empty()
    {
        // Arrange — a good run stores two channels for 8 March.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.TrailingReprocessDays = 3);

        IReadOnlyList<Ga4Row> rows =
        [
            Ga4TestHarness.TrafficRow("20260308", "Organic Search", 100),
            Ga4TestHarness.TrafficRow("20260308", "Direct", 40),
        ];
        var client = new FakeGa4ReportClient(_ => rows);

        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();
        (await dbContext.TrafficDaily.CountAsync()).Should().Be(2);

        // Act — the next night GA4 returns nothing at all: wrong property, an outage, or an
        // over-restrictive filter. It never means "those days had no traffic after all".
        rows = [];
        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        // Assert — the rows survive, the run is reported as a failure, and the watermark holds
        // so the window is retried rather than skipped.
        result.IsSuccess.Should().BeFalse("an empty report is a failure, not an instruction to delete");

        await using var verify = Ga4TestHarness.NewDbContext(database);
        verify.TrafficDaily.Should().HaveCount(2, "a rolling window must not be hollowed out every night");

        var state = await Ga4TestHarness.StoredStateAsync(database, "traffic_daily");
        state.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("Refusing to delete");
    }

    [Fact]
    public async Task stores_nothing_and_stays_clean_when_an_empty_report_meets_an_empty_window()
    {
        // The mirror of the case above: with nothing stored there is nothing to protect, so a
        // genuinely empty window is not an error.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-08");
        var client = new FakeGa4ReportClient(_ => Array.Empty<Ga4Row>());

        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        result.IsSuccess.Should().BeTrue();
        var state = await Ga4TestHarness.StoredStateAsync(database, "traffic_daily");
        state.WatermarkDate.Should().Be(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public async Task keeps_a_bucketed_row_that_still_carries_a_real_date()
    {
        // The shape actually observed on this property: the 39-month backfill hit GA4's
        // cardinality limit once and GA4 bucketed only the high-cardinality dimension, leaving
        // the date intact. Those metrics are real traffic, so the row must be stored, not
        // dropped — discarding it would silently under-report the day.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");

        var client = new FakeGa4ReportClient(_ =>
        [
            new Ga4Row(["20260309", "(other)"], ["582", "0", "0", "0", "0", "0"]),
        ]);

        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        result.IsSuccess.Should().BeTrue();

        await using var verify = Ga4TestHarness.NewDbContext(database);
        var stored = await verify.TrafficDaily.ToListAsync();
        stored.Should().ContainSingle();
        stored[0].Sessions.Should().Be(582, "GA4 says these sessions are real; only the label is a bucket");
        stored[0].ChannelGroup.Should().Be("(other)");
    }

    [Fact]
    public async Task skips_a_row_whose_date_itself_was_bucketed_rather_than_wedging_the_table()
    {
        // Defensive, and not observed on this property: a row bucketed so hard that the DATE
        // reads "(other)" cannot be stored, because the date is part of every primary key here.
        // Throwing on it would wedge the table — the chunk's request is deterministic, so the
        // same window would throw again every night and the watermark would never pass it.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-08");

        var client = new FakeGa4ReportClient(_ =>
        [
            Ga4TestHarness.TrafficRow("20260308", "Organic Search", 100),
            new Ga4Row(["(other)", "(other)"], ["999", "0", "0", "0", "0", "0"]),
        ]);

        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        result.IsSuccess.Should().BeTrue("an undated bucket row must not fail the chunk");

        await using var verify = Ga4TestHarness.NewDbContext(database);
        var stored = await verify.TrafficDaily.ToListAsync();
        stored.Should().ContainSingle("the (other) row carries no usable date and is dropped");
        stored[0].ChannelGroup.Should().Be("Organic Search");

        var state = await Ga4TestHarness.StoredStateAsync(database, "traffic_daily");
        state.WatermarkDate.Should().Be(new DateOnly(2026, 3, 9), "the window is not retried forever");
    }

    [Fact]
    public async Task still_fails_on_a_genuinely_malformed_date_rather_than_skipping_it()
    {
        // The counterpart to the test above: "(other)" is a documented bucket, anything else
        // unparseable is a real format change and must be loud, not silently dropped.
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-08");

        var client = new FakeGa4ReportClient(_ =>
        [
            new Ga4Row(["2026-03-08", "Organic Search"], ["100", "0", "0", "0", "0", "0"]),
        ]);

        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        result.IsSuccess.Should().BeFalse();
    }
}
