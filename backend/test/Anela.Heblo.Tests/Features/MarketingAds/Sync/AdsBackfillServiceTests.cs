using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsBackfillServiceTests
{
    private static readonly DateOnly From = new(2026, 7, 20);
    private static readonly DateOnly To = new(2026, 9, 5);

    private static ScriptedAdReadSource Google(bool searchTerms = true)
    {
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, searchTerms, changeLog: true) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"),
        };
        source.Facts[AdsTestData.AccountId] = new List<AdDailyFactRow>
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", From, 100m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", new DateOnly(2026, 8, 15), 80m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", To, 60m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", To.AddDays(1), 999m),
        };
        source.SearchTerms[AdsTestData.AccountId] = new List<AdSearchTermRow>
        {
            AdsTestData.Term("g-1", new DateOnly(2026, 8, 1), "kosmetika", 10m),
        };
        return source;
    }

    [Fact]
    public async Task Range_source_is_read_one_calendar_month_per_call()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var source = new RangeAdReadSource(Google());

        // Act
        var report = await harness.Backfill(source).RunAsync(From, To, default);

        // Assert
        report.HasFailures.Should().BeFalse();
        var months = new[]
        {
            (From, new DateOnly(2026, 7, 31)), (new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), (new DateOnly(2026, 9, 1), To),
        };
        source.FactRangeRequests.Should().Equal(months);
        source.SearchTermRangeRequests.Should().Equal(months);
        await using var verify = harness.Verify();
        (await verify.DailyFacts.Select(f => f.Cost).ToListAsync()).Should().BeEquivalentTo(new[] { 100m, 80m, 60m });
        (await verify.SearchTermsDaily.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Source_without_range_reads_is_read_day_by_day()
    {
        var harness = new AdsSyncHarness();
        var source = Google();

        await harness.Backfill(source).RunAsync(From, To, default);

        source.FactRequests.Should().HaveCount(To.DayNumber - From.DayNumber + 1);
        source.SearchTermRequests.Should().HaveCount(To.DayNumber - From.DayNumber + 1);
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Rows_the_platform_no_longer_reports_are_removed_on_days_without_data()
    {
        // Arrange — a first run stores a fact, then the platform stops reporting that day
        var harness = new AdsSyncHarness();
        var google = Google();
        await harness.Backfill(new RangeAdReadSource(google)).RunAsync(From, To, default);
        google.Facts[AdsTestData.AccountId].RemoveAll(f => f.Date == To);

        // Act
        await harness.Backfill(new RangeAdReadSource(google)).RunAsync(From, To, default);

        // Assert
        await using var verify = harness.Verify();
        (await verify.DailyFacts.AnyAsync(f => f.Date == To)).Should().BeFalse();
        (await verify.DailyFacts.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Rerunning_a_range_does_not_duplicate_rows()
    {
        var harness = new AdsSyncHarness();

        await harness.Backfill(new RangeAdReadSource(Google())).RunAsync(From, To, default);
        await harness.Backfill(new RangeAdReadSource(Google())).RunAsync(From, To, default);

        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(3);
        (await verify.SearchTermsDaily.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_failed_month_stops_the_account_and_names_the_date_to_resume_from()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var source = new RangeAdReadSource(Google());
        source.FailRangesStartingOn.Add(new DateOnly(2026, 8, 1));

        // Act
        var report = await harness.Backfill(source).RunAsync(From, To, default);

        // Assert
        report.Errors.Should().ContainSingle().Which.Should().Contain("resume from 2026-08-01");
        source.FactRangeRequests.Should().HaveCount(2);
        await using var verify = harness.Verify();
        (await verify.DailyFacts.Select(f => f.Date).ToListAsync()).Should().Equal(From);
    }

    [Fact]
    public async Task Does_not_move_the_daily_jobs_fact_watermarks()
    {
        var harness = new AdsSyncHarness();

        await harness.Backfill(new RangeAdReadSource(Google())).RunAsync(From, To, default);

        await using var verify = harness.Verify();
        (await verify.SyncStates.Select(s => s.Stream).ToListAsync()).Should().Equal("Entities");
    }

    [Fact]
    public async Task Refuses_a_range_reaching_the_accounts_today()
    {
        var harness = new AdsSyncHarness();
        var source = new RangeAdReadSource(Google());
        var today = DateOnly.FromDateTime(AdsTestData.Now.UtcDateTime);

        var report = await harness.Backfill(source).RunAsync(From, today, default);

        report.Errors.Should().ContainSingle().Which.Should().Contain("must be before the account's today");
        source.FactRangeRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Sources_without_search_terms_are_never_asked_for_them()
    {
        var harness = new AdsSyncHarness();
        var source = new RangeAdReadSource(Google(searchTerms: false));

        await harness.Backfill(source).RunAsync(From, To, default);

        source.SearchTermRangeRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Reversed_range_is_rejected()
    {
        var act = () => new AdsSyncHarness().Backfill(Google()).RunAsync(To, From, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_failed_save_leaves_nothing_tracked_and_a_rerun_completes_the_range()
    {
        // Arrange — the first save of the 15 Aug fact fails
        var harness = new AdsSyncHarness(null,
            new FailOnceSaveInterceptor<Anela.Heblo.Persistence.Ads.Entities.AdDailyFact>(f => f.Date == new DateOnly(2026, 8, 15)));
        var google = Google();

        // Act
        var failed = await harness.Backfill(new RangeAdReadSource(google)).RunAsync(From, To, default);
        var trackedAfterFailure = harness.Db.ChangeTracker.Entries().Count();
        var rerun = await harness.Backfill(new RangeAdReadSource(google)).RunAsync(new DateOnly(2026, 8, 1), To, default);

        // Assert
        failed.Errors.Should().ContainSingle().Which.Should().Contain("resume from 2026-08-01");
        trackedAfterFailure.Should().Be(0);
        rerun.HasFailures.Should().BeFalse();
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(3);
        (await verify.SearchTermsDaily.CountAsync()).Should().Be(1);
    }
}
