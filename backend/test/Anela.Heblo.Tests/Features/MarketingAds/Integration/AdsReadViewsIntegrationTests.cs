using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Persistence.Ads.Entities;
using Anela.Heblo.Persistence.ShoptetOrders;
using Anela.Heblo.Persistence.ShoptetOrders.Entities;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsReadViewsIntegrationTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly DateTimeOffset Synced = new(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly string AdsScript = Path.Combine(AppContext.BaseDirectory, "MarketingAds", "Sql", "ads_read_views.sql");
    private static readonly string ShoptetScript = Path.Combine(AppContext.BaseDirectory, "MarketingAds", "Sql", "shoptet_raw_views.sql");

    private readonly PostgresSharedContainerFixture _fixture;

    public AdsReadViewsIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    private static async Task RunScriptAsync(DbContext db, string path) =>
        await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(path));

    private static async Task<long> CampaignAsync(AdsDbContext db, string platform, string account, string currency, bool managed, decimal cost)
    {
        var acc = new AdAccount { Platform = platform, ExternalId = account, Name = account, Currency = currency, TimeZone = "Europe/Prague", IsManaged = managed };
        db.Accounts.Add(acc);
        await db.SaveChangesAsync();
        var campaign = NewEntity(acc.Id, "Campaign", $"{account}-c", null);
        db.Entities.Add(campaign);
        await db.SaveChangesAsync();
        var adGroup = NewEntity(acc.Id, "AdGroup", $"{account}-g", campaign.Id);
        db.Entities.Add(adGroup);
        await db.SaveChangesAsync();
        db.DailyFacts.Add(Fact(campaign.Id, cost, currency));
        db.DailyFacts.Add(Fact(adGroup.Id, cost, currency)); // same money at a lower level: must not be added twice
        await db.SaveChangesAsync();
        return acc.Id;
    }

    private static AdEntity NewEntity(long accountId, string level, string externalId, long? parentId) => new()
    {
        AccountId = accountId,
        Level = level,
        ExternalId = externalId,
        ParentId = parentId,
        Name = externalId,
        Status = "Enabled",
        AttributesJson = "{}",
        FirstSeenAt = Synced,
        LastSeenAt = Synced,
        UpdatedAt = Synced,
    };

    private static AdDailyFact Fact(long entityId, decimal cost, string currency) => new()
    {
        EntityId = entityId,
        Date = Day,
        Impressions = 1000,
        Clicks = 50,
        Cost = cost,
        Conversions = 2,
        ConversionValue = cost * 4,
        Currency = currency,
    };

    private static ShoptetOrder Order(string code, decimal withoutVat, int status = 2, decimal rate = 1m, bool cashDesk = false) => new()
    {
        Code = code,
        CreationTime = Synced,
        OrderDate = Day,
        StatusId = status,
        PriceWithoutVat = withoutVat,
        ExchangeRate = rate,
        CashDeskOrder = cashDesk,
        SyncedAt = Synced,
    };

    private async Task<(AdsDbContext Ads, ShoptetOrdersDbContext Shoptet)> SeedAsync()
    {
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_views", withShoptet: true);
        var ads = new AdsDbContext(AdsPostgres.Options(connectionString));
        var shoptet = new ShoptetOrdersDbContext(new DbContextOptionsBuilder<ShoptetOrdersDbContext>().UseNpgsql(connectionString).Options);

        await CampaignAsync(ads, "GoogleAds", "g", "CZK", managed: true, cost: 100m);
        await CampaignAsync(ads, "Sklik", "s", "CZK", managed: true, cost: 50m);
        await CampaignAsync(ads, "GoogleAds", "agency", "CZK", managed: false, cost: 999m);
        await CampaignAsync(ads, "MetaAds", "m-eur", "EUR", managed: true, cost: 10m);

        shoptet.Orders.AddRange(
            Order("A1", 1000m),
            Order("EUR1", 40m, rate: 0.04m),        // 40 EUR / 0.04 = 1000 CZK
            Order("SHOP1", 300m, cashDesk: true),   // revenue yes, e-shop revenue no
            Order("X1", 5000m, status: -4));        // cancelled
        await shoptet.SaveChangesAsync();
        return (ads, shoptet);
    }

    [Fact]
    public async Task Campaign_monthly_reads_one_level_of_managed_accounts()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(ads, AdsScript);

        var costs = await ads.Database
            .SqlQuery<decimal>($"SELECT cost AS \"Value\" FROM ads.v_ads_campaign_monthly WHERE account_external_id = 'g'")
            .ToListAsync();
        var accounts = await ads.Database
            .SqlQuery<string>($"SELECT account_external_id AS \"Value\" FROM ads.v_ads_campaign_monthly")
            .ToListAsync();

        costs.Should().Equal(100m);
        accounts.Should().NotContain("agency");
    }

    [Fact]
    public async Task Blended_monthly_is_net_revenue_without_cancelled_over_czk_campaign_cost()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(ads, AdsScript);
        await RunScriptAsync(ads, AdsScript); // re-runnable

        var row = await ads.Database.SqlQuery<BlendedRow>($@"
            SELECT ad_cost_czk AS ""AdCost"", google_ads_cost_czk AS ""Google"", sklik_cost_czk AS ""Sklik"",
                   has_non_czk_ad_cost AS ""NonCzk"", orders::int AS ""Orders"",
                   revenue_without_vat_czk AS ""Revenue"", eshop_revenue_without_vat_czk AS ""Eshop"",
                   blended_roas AS ""Roas"", pno_pct AS ""Pno""
            FROM ads.v_ads_blended_monthly WHERE month = DATE '2026-09-01'").SingleAsync();

        row.AdCost.Should().Be(150m);
        row.Google.Should().Be(100m);
        row.Sklik.Should().Be(50m);
        row.NonCzk.Should().BeTrue();
        row.Orders.Should().Be(3);
        row.Revenue.Should().Be(2300m);
        row.Eshop.Should().Be(2000m);
        row.Roas.Should().Be(15.3333m);
        row.Pno.Should().Be(6.52m);
    }

    [Fact]
    public async Task Blended_views_expose_how_many_days_actually_have_ad_cost()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        var revenueOnlyDay = Order("NEXT1", 500m);
        revenueOnlyDay.OrderDate = Day.AddDays(1); // revenue but no synced ad cost
        shoptet.Orders.Add(revenueOnlyDay);
        await shoptet.SaveChangesAsync();
        await RunScriptAsync(ads, AdsScript);

        var monthly = await ads.Database.SqlQuery<CoverageRow>($@"
            SELECT days_with_data::int AS ""DaysWithData"", days_with_ad_cost::int AS ""DaysWithAdCost""
            FROM ads.v_ads_blended_monthly WHERE month = DATE '2026-09-01'").SingleAsync();
        var daily = await ads.Database.SqlQuery<bool>($@"
            SELECT has_ad_cost_data AS ""Value"" FROM ads.v_ads_blended_daily ORDER BY date").ToListAsync();

        monthly.DaysWithData.Should().Be(2);
        monthly.DaysWithAdCost.Should().Be(1);
        daily.Should().Equal(true, false);
    }

    [Fact]
    public async Task Change_events_view_falls_back_to_the_stored_level_and_ref_when_the_entity_is_unlinked()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        var accountId = await ads.Accounts.Where(a => a.ExternalId == "g").Select(a => a.Id).SingleAsync();
        var campaignId = await ads.Entities.Where(e => e.ExternalId == "g-c").Select(e => e.Id).SingleAsync();
        ads.ChangeEvents.AddRange(
            NewChange(accountId, "linked", entityId: campaignId, level: null, externalRef: null),
            NewChange(accountId, "unlinked", entityId: null, level: "Keyword", externalRef: "kw-42"));
        await ads.SaveChangesAsync();
        await RunScriptAsync(ads, AdsScript);

        var rows = await ads.Database.SqlQuery<ChangeRow>($@"
            SELECT entity_level AS ""Level"", entity_external_id AS ""ExternalId"", entity_name AS ""Name""
            FROM ads.v_ads_change_events ORDER BY id").ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].Should().BeEquivalentTo(new ChangeRow { Level = "Campaign", ExternalId = "g-c", Name = "g-c" });
        rows[1].Should().BeEquivalentTo(new ChangeRow { Level = "Keyword", ExternalId = "kw-42", Name = null });
    }

    private static AdChangeEvent NewChange(long accountId, string eventId, long? entityId, string? level, string? externalRef) => new()
    {
        AccountId = accountId,
        ExternalEventId = eventId,
        OccurredAt = Synced,
        ActorKind = "Human",
        EntityId = entityId,
        EntityLevel = level,
        EntityExternalRef = externalRef,
        ChangeType = "Updated",
        Source = "ChangeLog",
        Origin = "OutOfBand",
        SyncedAt = Synced,
    };

    [Fact]
    public async Task Metabase_can_read_the_granted_views_only()
    {
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(ads, AdsScript);

        async Task<bool> CanSelect(string relation) => await ads.Database
            .SqlQuery<bool>($"SELECT has_table_privilege('metabase_ro', {relation}, 'SELECT') AS \"Value\"").SingleAsync();

        (await CanSelect("ads.v_ads_campaign_monthly")).Should().BeTrue();
        (await CanSelect("ads.v_ads_blended_monthly")).Should().BeTrue();
        (await CanSelect("ads.v_ads_change_events")).Should().BeTrue();
        (await CanSelect("ads.v_ads_blended_daily")).Should().BeFalse();
        (await CanSelect("ads.ad_daily_facts")).Should().BeFalse();
        (await CanSelect("ads.ad_change_events")).Should().BeFalse();
    }

    [Fact]
    public async Task Rerunning_the_shoptet_script_does_not_drop_the_ads_views()
    {
        // shoptet_raw_views.sql drops order_fact with CASCADE; the ads views must not depend on it.
        var (ads, shoptet) = await SeedAsync();
        await using var _ = ads;
        await using var __ = shoptet;
        await RunScriptAsync(shoptet, ShoptetScript);
        await RunScriptAsync(ads, AdsScript);

        await RunScriptAsync(shoptet, ShoptetScript);

        var exists = await ads.Database
            .SqlQuery<bool>($"SELECT to_regclass('ads.v_ads_blended_monthly') IS NOT NULL AS \"Value\"").SingleAsync();
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Without_shoptet_raw_the_script_succeeds_and_skips_blended_views()
    {
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_views_noshop", withShoptet: false);
        await using var ads = new AdsDbContext(AdsPostgres.Options(connectionString));

        await RunScriptAsync(ads, AdsScript);

        var blended = await ads.Database
            .SqlQuery<bool>($"SELECT to_regclass('ads.v_ads_blended_daily') IS NULL AS \"Value\"").SingleAsync();
        var campaign = await ads.Database
            .SqlQuery<bool>($"SELECT to_regclass('ads.v_ads_campaign_monthly') IS NOT NULL AS \"Value\"").SingleAsync();
        blended.Should().BeTrue();
        campaign.Should().BeTrue();
    }

    private sealed class CoverageRow
    {
        public int DaysWithData { get; set; }
        public int DaysWithAdCost { get; set; }
    }

    private sealed class ChangeRow
    {
        public string? Level { get; set; }
        public string? ExternalId { get; set; }
        public string? Name { get; set; }
    }

    private sealed class BlendedRow
    {
        public decimal AdCost { get; set; }
        public decimal Google { get; set; }
        public decimal Sklik { get; set; }
        public bool NonCzk { get; set; }
        public int Orders { get; set; }
        public decimal Revenue { get; set; }
        public decimal Eshop { get; set; }
        public decimal? Roas { get; set; }
        public decimal? Pno { get; set; }
    }
}
