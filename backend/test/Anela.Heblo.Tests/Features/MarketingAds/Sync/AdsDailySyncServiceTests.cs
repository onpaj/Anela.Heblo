using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdsDailySyncServiceTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    private static ScriptedAdReadSource Google(string account = AdsTestData.AccountId, bool searchTerms = true, bool changeLog = true)
    {
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, searchTerms, changeLog) { Accounts = { AdsTestData.Account(account) } };
        source.Entities[account] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.Ad("ad-1", "g-1"),
        };
        source.Facts[account] = new List<AdDailyFactRow>
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day.AddDays(-14), 80m),
        };
        source.SearchTerms[account] = new List<AdSearchTermRow> { AdsTestData.Term("g-1", Day, "kosmetika", 10m) };
        return source;
    }

    [Fact]
    public async Task No_registered_source_skips_the_run()
    {
        var report = await new AdsSyncHarness().Daily().RunAsync(default);

        report.Skipped.Should().BeTrue();
        report.HasFailures.Should().BeFalse();
    }

    [Fact]
    public async Task Syncs_entities_facts_and_search_terms_for_yesterday_plus_lookback()
    {
        // Arrange
        var harness = new AdsSyncHarness();
        var source = Google();

        // Act
        var report = await harness.Daily(source).RunAsync(default);

        // Assert
        report.HasFailures.Should().BeFalse();
        source.FactRequests.Should().HaveCount(15);
        source.FactRequests.Select(r => r.Date).Should().Contain(new[] { Day, Day.AddDays(-14) });
        source.SearchTermRequests.Should().HaveCount(15);
        await using var verify = harness.Verify();
        (await verify.Entities.CountAsync()).Should().Be(3);
        (await verify.DailyFacts.CountAsync()).Should().Be(2);
        (await verify.SearchTermsDaily.CountAsync()).Should().Be(1);
        var states = await verify.SyncStates.ToListAsync();
        states.Select(s => s.Stream).Should().BeEquivalentTo(new[] { "Entities", "DailyFacts", "SearchTerms" });
        states.Should().OnlyContain(s => s.Status == AdsDbValues.StatusSucceeded);
        states.Single(s => s.Stream == "DailyFacts").Watermark.Should().Be(new DateTimeOffset(Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }

    [Fact]
    public async Task Unmanaged_accounts_are_not_synced()
    {
        var harness = new AdsSyncHarness(new AdsSyncOptions { ManagedAccounts = new[] { "GoogleAds:someone-else" } });
        var source = Google();

        await harness.Daily(source).RunAsync(default);

        source.FactRequests.Should().BeEmpty();
        await using var verify = harness.Verify();
        (await verify.Accounts.SingleAsync()).IsManaged.Should().BeFalse();
    }

    [Fact]
    public async Task A_failing_source_does_not_block_the_others()
    {
        var harness = new AdsSyncHarness();
        var broken = new ScriptedAdReadSource(AdPlatform.MetaAds) { FailAccounts = true };
        var google = Google();

        var report = await harness.Daily(broken, google).RunAsync(default);

        report.Errors.Should().ContainSingle().Which.Should().Contain("MetaAds");
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_failing_stream_is_recorded_and_the_next_stream_still_runs()
    {
        var harness = new AdsSyncHarness();
        var source = Google();
        source.FailFactsFor.Add(AdsTestData.AccountId);

        var report = await harness.Daily(source).RunAsync(default);

        report.Errors.Should().ContainSingle().Which.Should().Contain("DailyFacts");
        await using var verify = harness.Verify();
        var states = await verify.SyncStates.ToDictionaryAsync(s => s.Stream);
        states["DailyFacts"].Status.Should().Be(AdsDbValues.StatusFailed);
        states["DailyFacts"].LastError.Should().Contain("facts unavailable");
        states["SearchTerms"].Status.Should().Be(AdsDbValues.StatusSucceeded);
    }

    [Fact]
    public async Task Sources_without_search_terms_are_never_asked_for_them()
    {
        var harness = new AdsSyncHarness();
        var meta = Google(searchTerms: false);

        await harness.Daily(meta).RunAsync(default);

        meta.SearchTermRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Source_without_change_log_gets_snapshot_diff_events_from_the_daily_run()
    {
        // Arrange — first run stores the entities, then the ad is paused outside Heblo
        var harness = new AdsSyncHarness();
        var sklik = Google(changeLog: false);
        await harness.Daily(sklik).RunAsync(default);
        sklik.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);
        harness.Time.Advance(TimeSpan.FromDays(1));

        // Act
        await harness.Daily(sklik).RunAsync(default);

        // Assert
        await using var verify = harness.Verify();
        var change = await verify.ChangeEvents.SingleAsync();
        change.Source.Should().Be("SnapshotDiff");
        change.ChangeType.Should().Be("Status");
        change.ActorKind.Should().Be("Unknown");
        change.Origin.Should().Be("OutOfBand");
    }

    [Fact]
    public async Task Source_with_change_log_gets_no_snapshot_events()
    {
        var harness = new AdsSyncHarness();
        var google = Google(changeLog: true);
        await harness.Daily(google).RunAsync(default);
        google.Entities[AdsTestData.AccountId][2] = AdsTestData.Ad("ad-1", "g-1", AdEntityStatus.Paused);

        await harness.Daily(google).RunAsync(default);

        await using var verify = harness.Verify();
        (await verify.ChangeEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Invalid_options_fail_the_run_not_construction()
    {
        // Arrange — constructing must not throw (job discovery constructs every job)
        var harness = new AdsSyncHarness(new AdsSyncOptions { FactLookbackDays = 90 });
        var service = harness.Daily(Google());

        // Act
        var act = () => service.RunAsync(default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*FactLookbackDays*");
    }
}
