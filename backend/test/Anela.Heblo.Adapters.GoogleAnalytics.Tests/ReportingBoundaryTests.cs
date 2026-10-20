using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

/// <summary>
/// Where a day begins, and how a report's rows collapse into stored rows. Both are places where
/// the sync can store plausible-looking numbers that are quietly wrong.
/// </summary>
public class ReportingBoundaryTests
{
    [Fact]
    public async Task takes_yesterday_from_the_propertys_timezone_not_utc()
    {
        // 23:30 UTC on 8 March is already 9 March in Prague, which is the property's reporting
        // timezone and therefore the one GA4 buckets by. Reading "yesterday" in UTC pulls the
        // wrong day for every run between 23:00Z and midnight — a retried Hangfire job, a manual
        // backfill, or any run at all once DST widens the offset.
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o =>
        {
            o.BackfillFrom = "2026-03-01";
            o.TimeZone = "Europe/Prague";
        });
        var client = new FakeGa4ReportClient(_ => Array.Empty<Ga4Row>());
        var lateEvening = new DateTimeOffset(2026, 3, 8, 23, 30, 0, TimeSpan.Zero);

        await Ga4TestHarness.TrafficSync(dbContext, client, options, lateEvening).SyncAsync();

        client.Requests[^1].EndDate.Should().Be(
            new DateOnly(2026, 3, 8),
            "it is already 9 March in Prague, so yesterday is the 8th — in UTC it would be the 7th");
    }

    [Fact]
    public async Task falls_back_to_utc_when_the_configured_timezone_is_unknown()
    {
        // A container without tzdata must not take the nightly sync down.
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o =>
        {
            o.BackfillFrom = "2026-03-01";
            o.TimeZone = "Mars/Olympus_Mons";
        });
        var client = new FakeGa4ReportClient(_ => Array.Empty<Ga4Row>());
        var now = new DateTimeOffset(2026, 3, 10, 4, 10, 0, TimeSpan.Zero);

        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, now).SyncAsync();

        result.IsSuccess.Should().BeTrue();
        client.Requests[^1].EndDate.Should().Be(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public async Task collapses_one_page_reported_under_several_titles_into_a_single_row()
    {
        // GA4 reports (date, pagePath, pageTitle) triples, and one article accumulates titles over
        // its life — a rename, an A/B test, a "| Anela" suffix appearing. Stored per triple, the
        // article's views would be split across rows and it would rank far below its real
        // position in the most-read report.
        var database = Ga4TestHarness.NewDatabaseName();
        var dbContext = Ga4TestHarness.NewDbContext(database);
        var options = Ga4TestHarness.Options(o => o.BackfillFrom = "2026-03-09");

        var client = new FakeGa4ReportClient(_ =>
        [
            new Ga4Row(["20260309", "/blog/mydlo", "Mýdlo — Anela"], ["30", "20"]),
            new Ga4Row(["20260309", "/blog/mydlo", "Mýdlo"], ["70", "40"]),
            new Ga4Row(["20260309", "/blog/krem", "Krém"], ["10", "5"]),
        ]);

        await Ga4TestHarness.PageSync(dbContext, client, options, new DateTimeOffset(2026, 3, 10, 4, 10, 0, TimeSpan.Zero))
            .SyncAsync();

        await using var verify = Ga4TestHarness.NewDbContext(database);
        var stored = await verify.PageDaily.ToListAsync();

        stored.Should().HaveCount(2, "one row per page path per day, not one per title variant");
        var article = stored.Single(x => x.PagePath == "/blog/mydlo");
        article.ScreenPageViews.Should().Be(100, "views are summed across the title variants");
        article.PageTitle.Should().Be("Mýdlo", "the busiest variant is the one worth showing");
    }
}
