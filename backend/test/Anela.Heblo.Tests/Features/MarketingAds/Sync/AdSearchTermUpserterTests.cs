using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdSearchTermUpserterTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    [Fact]
    public async Task Inserts_updates_and_prunes_by_term_and_match_type()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Term("g-1", Day, "kosmetika", 10m),
            AdsTestData.Term("g-1", Day, "zdarma", 3m),
        }, ids, default);

        var counts = await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Term("g-1", Day, "kosmetika", 12m) }, ids, default);

        counts.Should().Be(new AdUpsertCounts(2, 0));
        await using var verify = harness.Verify();
        var row = await verify.SearchTermsDaily.SingleAsync();
        row.SearchTerm.Should().Be("kosmetika");
        row.Cost.Should().Be(12m);
        row.MatchType.Should().Be("Exact");
    }

    [Fact]
    public async Task Missing_match_type_is_stored_as_Unknown()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Term("g-1", Day, "krém", 1m, matchType: null) }, ids, default);

        await using var verify = harness.Verify();
        (await verify.SearchTermsDaily.SingleAsync()).MatchType.Should().Be(AdsDbValues.NoMatchType);
    }

    [Fact]
    public async Task Unknown_ad_group_is_counted_not_written()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        var counts = await harness.SearchTerms.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Term("gone", Day, "krém", 1m) }, ids, default);

        counts.Should().Be(new AdUpsertCounts(0, 1));
    }
}
