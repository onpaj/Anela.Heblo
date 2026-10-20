using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Sync;

public class AdDailyFactUpserterTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    [Fact]
    public async Task Stores_facts_at_every_reported_level()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        var counts = await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m),
            AdsTestData.Fact(AdEntityLevel.AdGroup, "g-1", Day, 100m),
        }, ids, default);

        counts.Should().Be(new AdUpsertCounts(2, 0));
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Rerun_updates_instead_of_duplicating()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        await harness.Facts.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m) }, ids, default);

        await harness.Facts.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 130m, conversions: 3m) }, ids, default);

        await using var verify = harness.Verify();
        var row = await verify.DailyFacts.SingleAsync();
        row.Cost.Should().Be(130m);
        row.Conversions.Should().Be(3m);
    }

    [Fact]
    public async Task Entity_no_longer_reported_for_the_day_is_removed_other_days_untouched()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();
        var earlier = Day.AddDays(-1);
        await harness.Facts.ReplaceDayAsync(accountId, earlier, new[] { AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", earlier, 5m) }, ids, default);
        await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m),
            AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", Day, 5m),
        }, ids, default);

        var counts = await harness.Facts.ReplaceDayAsync(accountId, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m) }, ids, default);

        counts.Written.Should().Be(2); // one update + one delete
        await using var verify = harness.Verify();
        var rows = await verify.DailyFacts.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(f => f.Date == earlier);
    }

    [Fact]
    public async Task Unknown_entities_and_other_dates_are_skipped()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        var counts = await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "removed-campaign", Day, 7m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day.AddDays(-3), 7m),
        }, ids, default);

        counts.Should().Be(new AdUpsertCounts(0, 1));
        await using var verify = harness.Verify();
        (await verify.DailyFacts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_rows_for_one_entity_are_summed()
    {
        var harness = new AdsSyncHarness();
        var (accountId, ids) = await harness.SeedTreeAsync();

        await harness.Facts.ReplaceDayAsync(accountId, Day, new[]
        {
            AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", Day, 5m, clicks: 2),
            AdsTestData.Fact(AdEntityLevel.Ad, "ad-1", Day, 7m, clicks: 3),
        }, ids, default);

        await using var verify = harness.Verify();
        var row = await verify.DailyFacts.SingleAsync();
        row.Cost.Should().Be(12m);
        row.Clicks.Should().Be(5);
    }

    [Fact]
    public async Task Another_accounts_facts_for_the_same_day_are_untouched()
    {
        var harness = new AdsSyncHarness();
        var (accountA, idsA) = await harness.SeedTreeAsync("A");
        var (accountB, idsB) = await harness.SeedTreeAsync("B");
        await harness.Facts.ReplaceDayAsync(accountB, Day, new[] { AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 9m) }, idsB, default);

        await harness.Facts.ReplaceDayAsync(accountA, Day, Array.Empty<AdDailyFactRow>(), idsA, default);

        await using var verify = harness.Verify();
        (await verify.DailyFacts.SingleAsync()).EntityId.Should().Be(idsB[new AdEntityKey(AdEntityLevel.Campaign, "c-1")]);
    }
}
