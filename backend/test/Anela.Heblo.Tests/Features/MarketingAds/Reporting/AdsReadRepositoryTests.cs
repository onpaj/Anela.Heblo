using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Reporting;

public class AdsReadRepositoryTests
{
    private static readonly DateOnly Day = AdsTestData.Yesterday;

    /// <summary>Two campaigns (c-1, c-2), ad group g-1 under c-1, facts at campaign and ad-group level over two days.</summary>
    private static async Task<AdsSyncHarness> SeedAsync()
    {
        var harness = new AdsSyncHarness();
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, searchTerms: true, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot>
        {
            AdsTestData.Campaign("c-1"), AdsTestData.Campaign("c-2"), AdsTestData.AdGroup("g-1", "c-1"), AdsTestData.AdGroup("g-2", "c-2"),
        };
        source.Facts[AdsTestData.AccountId] = new List<AdDailyFactRow>
        {
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day, 100m, clicks: 10, impressions: 1000, value: 400m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-1", Day.AddDays(-1), 50m, clicks: 5, impressions: 500, value: 100m),
            AdsTestData.Fact(AdEntityLevel.Campaign, "c-2", Day, 300m),
            AdsTestData.Fact(AdEntityLevel.AdGroup, "g-1", Day, 100m),
        };
        source.SearchTerms[AdsTestData.AccountId] = new List<AdSearchTermRow>
        {
            AdsTestData.Term("g-1", Day, "kosmetika", 10m), AdsTestData.Term("g-1", Day.AddDays(-1), "kosmetika", 5m),
            AdsTestData.Term("g-1", Day, "zdarma", 1m), AdsTestData.Term("g-2", Day, "krém", 7m),
        };
        await harness.Daily(source).RunAsync(default);
        return harness;
    }

    private static AdsReadRepository Repo(AdsSyncHarness harness) => new(harness.Verify());

    private static AdPerformanceQuery Perf(AdEntityLevel level, string? parent = null, int page = 1, int size = 100) =>
        new(AdPlatform.GoogleAds, AdsTestData.AccountId, level, Day.AddDays(-1), Day, parent, page, size);

    [Fact]
    public async Task Performance_sums_only_the_requested_level_over_the_range()
    {
        var page = await Repo(await SeedAsync()).GetPerformanceAsync(Perf(AdEntityLevel.Campaign), default);

        page!.Currency.Should().Be("CZK");
        page.TotalCount.Should().Be(2);
        page.Rows.Select(r => r.EntityExternalId).Should().Equal("c-2", "c-1"); // by cost, descending
        var c1 = page.Rows.Single(r => r.EntityExternalId == "c-1");
        c1.Cost.Should().Be(150m);
        c1.Clicks.Should().Be(15);
        c1.CtrPercent.Should().Be(1.000m);
        c1.AverageCpc.Should().Be(10.00m);
        c1.Roas.Should().Be(3.3333m);
    }

    [Fact]
    public async Task Performance_filters_by_parent_and_pages()
    {
        var repo = Repo(await SeedAsync());

        var children = await repo.GetPerformanceAsync(Perf(AdEntityLevel.AdGroup, parent: "c-1"), default);
        var secondPage = await repo.GetPerformanceAsync(Perf(AdEntityLevel.Campaign, page: 2, size: 1), default);

        children!.Rows.Should().ContainSingle().Which.ParentName.Should().Be("Campaign c-1");
        secondPage!.Rows.Should().ContainSingle().Which.EntityExternalId.Should().Be("c-1");
        secondPage.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Unknown_account_returns_null()
    {
        var page = await Repo(await SeedAsync()).GetPerformanceAsync(Perf(AdEntityLevel.Campaign) with { AccountExternalId = "nope" }, default);

        page.Should().BeNull();
    }

    [Fact]
    public async Task Search_terms_aggregate_days_filter_by_campaign_and_respect_top_and_min_cost()
    {
        var repo = Repo(await SeedAsync());
        var query = new AdSearchTermQuery(AdPlatform.GoogleAds, AdsTestData.AccountId, "c-1", null, Day.AddDays(-1), Day, null, 10);

        var all = await repo.GetSearchTermsAsync(query, default);
        var top = await repo.GetSearchTermsAsync(query with { Top = 1 }, default);
        var expensive = await repo.GetSearchTermsAsync(query with { MinCost = 2m }, default);

        all!.Select(t => t.SearchTerm).Should().Equal("kosmetika", "zdarma");
        all[0].Cost.Should().Be(15m);
        all[0].CampaignExternalId.Should().Be("c-1");
        all[0].AdGroupName.Should().Be("Ad group g-1");
        top!.Should().ContainSingle();
        expensive!.Select(t => t.SearchTerm).Should().Equal("kosmetika");
    }

    [Fact]
    public async Task Accounts_are_managed_only_unless_asked()
    {
        var harness = await SeedAsync();
        await using (var edit = harness.Verify())
        {
            (await edit.Accounts.SingleAsync()).IsManaged = false;
            await edit.SaveChangesAsync();
        }

        (await Repo(harness).ListAccountsAsync(null, includeUnmanaged: false, default)).Should().BeEmpty();
        (await Repo(harness).ListAccountsAsync(AdPlatform.GoogleAds, includeUnmanaged: true, default))
            .Should().ContainSingle().Which.Platform.Should().Be(AdPlatform.GoogleAds);
    }

    [Fact]
    public async Task Change_events_filter_by_origin_newest_first_with_entity_names()
    {
        // Arrange — two status changes an hour apart via the snapshot-diff path
        var harness = await SeedAsync();
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, changeLog: false) { Accounts = { AdsTestData.Account() } };
        source.Entities[AdsTestData.AccountId] = new List<AdEntitySnapshot> { AdsTestData.Campaign("c-1", AdEntityStatus.Paused) };
        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Change(source).RunAsync(default);
        source.Entities[AdsTestData.AccountId][0] = AdsTestData.Campaign("c-1", AdEntityStatus.Enabled);
        harness.Time.Advance(TimeSpan.FromHours(1));
        await harness.Change(source).RunAsync(default);

        // Act
        var page = await Repo(harness).GetChangeEventsAsync(new AdChangeEventQuery(AdPlatform.GoogleAds, null, AdChangeOrigin.OutOfBand, null,
            AdsTestData.Now.AddDays(-1), AdsTestData.Now.AddDays(1), 1, 50), default);
        var heblo = await Repo(harness).GetChangeEventsAsync(new AdChangeEventQuery(null, null, AdChangeOrigin.Heblo, null,
            AdsTestData.Now.AddDays(-1), AdsTestData.Now.AddDays(1), 1, 50), default);

        // Assert
        page.TotalCount.Should().Be(2);
        page.Rows[0].OccurredAt.Should().BeAfter(page.Rows[1].OccurredAt);
        page.Rows[0].EntityName.Should().Be("Campaign c-1");
        page.Rows[0].Source.Should().Be(AdChangeSource.SnapshotDiff);
        heblo.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Change_events_for_an_unresolved_entity_return_the_stored_level_and_raw_id()
    {
        // Arrange — a change-log event for an ad that was never synced as an entity (EntityId stays null)
        var harness = await SeedAsync();
        var source = new ScriptedAdReadSource(AdPlatform.GoogleAds, changeLog: true) { Accounts = { AdsTestData.Account() } };
        source.Changes[AdsTestData.AccountId] = new List<AdChangeEventRow>
        {
            AdsTestData.Change("g-evt-x", AdsTestData.Now.AddHours(-1), AdEntityLevel.Ad, "ad-unknown", "{\"status\":\"PAUSED\"}"),
        };
        await harness.Change(source).RunAsync(default);

        // Act
        var page = await Repo(harness).GetChangeEventsAsync(new AdChangeEventQuery(AdPlatform.GoogleAds, null, null, null,
            AdsTestData.Now.AddDays(-1), AdsTestData.Now.AddDays(1), 1, 50), default);

        // Assert
        var row = page.Rows.Should().ContainSingle().Which;
        row.EntityName.Should().BeNull();
        row.EntityLevel.Should().Be(AdEntityLevel.Ad);
        row.EntityExternalId.Should().Be("ad-unknown");
    }
}
