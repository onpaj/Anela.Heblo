using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

public class BackfillChunkingTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 4, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task walks_a_long_backfill_in_chunks_that_tile_the_window_without_gap_or_overlap()
    {
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o =>
        {
            o.BackfillFrom = "2026-01-01";
            o.ChunkDays = 31;
        });
        var client = new FakeGa4ReportClient(_ => Array.Empty<Ga4Row>());

        await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        client.Requests.Should().HaveCount(3);
        client.Requests[0].StartDate.Should().Be(new DateOnly(2026, 1, 1));
        for (var i = 1; i < client.Requests.Count; i++)
        {
            client.Requests[i].StartDate.Should().Be(
                client.Requests[i - 1].EndDate.AddDays(1),
                "each chunk must start the day after the previous one ended");
        }
        client.Requests[^1].EndDate.Should().Be(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public async Task advances_the_watermark_after_each_chunk_so_an_interrupted_backfill_resumes()
    {
        // Arrange — the third chunk blows up, as a multi-year backfill eventually will.
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o =>
        {
            o.BackfillFrom = "2026-01-01";
            o.ChunkDays = 31;
        });

        var calls = 0;
        var client = new FakeGa4ReportClient(_ =>
        {
            if (++calls == 3) throw new HttpRequestException("GA4 said no");
            return Array.Empty<Ga4Row>();
        });

        // Act
        var result = await Ga4TestHarness.TrafficSync(dbContext, client, options, Now).SyncAsync();

        // Assert — the failure is reported, but the two good chunks are not thrown away.
        result.IsSuccess.Should().BeFalse();
        var state = await new Ga4SyncWatermarkRepository(dbContext).GetOrCreateAsync("traffic_daily");
        state.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("GA4 said no");
        state.WatermarkDate.Should().Be(
            new DateOnly(2026, 3, 3),
            "31-day chunks run 01-01..01-31 then 02-01..03-03; the second was committed before the third threw");
    }

    [Fact]
    public async Task caps_landing_pages_to_the_top_n_of_each_day_separately()
    {
        var dbContext = Ga4TestHarness.NewDbContext();
        var options = Ga4TestHarness.Options(o =>
        {
            o.BackfillFrom = "2026-03-08";
            o.TopLandingPagesPerDay = 2;
        });

        var client = new FakeGa4ReportClient(_ => new[]
        {
            new Ga4Row(["20260308", "/a"], ["10", "5"]),
            new Ga4Row(["20260308", "/b"], ["30", "9"]),
            new Ga4Row(["20260308", "/c"], ["20", "7"]),
            new Ga4Row(["20260309", "/d"], ["1", "0"]),
        });

        var sync = new LandingPageSyncService(
            client,
            new Ga4SyncWatermarkRepository(dbContext),
            dbContext,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedTimeProvider(Now),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LandingPageSyncService>.Instance);

        await sync.SyncAsync();

        var stored = dbContext.LandingPageDaily.ToList();
        stored.Where(x => x.Date == new DateOnly(2026, 3, 8))
              .Select(x => x.LandingPage)
              .Should().BeEquivalentTo(["/b", "/c"], "the two busiest of that day, not of the window");
        stored.Should().Contain(x => x.LandingPage == "/d", "a quiet day keeps its own rows");

        var state = await new Ga4SyncWatermarkRepository(dbContext).GetOrCreateAsync("landing_page_daily");
        state.TopNPerDay.Should().Be(2, "the cap in force is recorded so a reader can tell truncated days from complete ones");
    }

    [Fact]
    public void month_grain_sync_widens_its_window_to_whole_calendar_months()
    {
        // A yearMonth report over 15 Aug - 9 Mar would return a half-August and overwrite a
        // complete one, so the window start is pulled back to the first of the month.
        var aligned = TrafficMonthlySyncService.AlignToWholeMonths(new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 9));

        aligned.Start.Should().Be(new DateOnly(2026, 8, 1));
        aligned.End.Should().Be(new DateOnly(2026, 9, 9), "the current month is legitimately partial");
    }

    [Theory]
    [InlineData("202401", 2024, 1)]
    [InlineData("202612", 2026, 12)]
    public void parses_ga4_year_month_into_the_first_of_that_month(string value, int year, int month)
    {
        TrafficMonthlySyncService.ParseYearMonth(value).Should().Be(new DateOnly(year, month, 1));
    }

    [Fact]
    public void rejects_an_unparseable_year_month_rather_than_storing_a_wrong_date()
    {
        var act = () => TrafficMonthlySyncService.ParseYearMonth("2026-08");
        act.Should().Throw<FormatException>().WithMessage("*yearMonth*");
    }
}
