using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Features.MarketingAds.Integration;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class AdsReadRepositoryBlendedIntegrationTests
{
    private static readonly string AdsScript = Path.Combine(AppContext.BaseDirectory, "MarketingAds", "Sql", "ads_read_views.sql");
    private readonly PostgresSharedContainerFixture _fixture;

    public AdsReadRepositoryBlendedIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Reads_the_blended_daily_view_with_date_parameters()
    {
        // Arrange
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_blend_repo", withShoptet: true);
        await using var db = new AdsDbContext(AdsPostgres.Options(connectionString));
        await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(AdsScript));
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO shoptet_raw.\"order\" (code, creation_time, order_date, status_id, is_paid, cash_desk_order, price_without_vat, exchange_rate, " +
            "shipping_price_with_vat, shipping_price_without_vat, billing_price_with_vat, billing_price_without_vat, discount_with_vat, discount_without_vat, " +
            "product_price_with_vat, product_price_without_vat, product_units, vat_payer, raw_payload, synced_at) VALUES " +
            "('A1', now(), DATE '2026-09-01', 2, true, false, 1000, 1, 0,0,0,0,0,0,0,0,0, true, '{{}}', now())," +
            "('A2', now(), DATE '2026-09-05', 2, true, false, 500, 1, 0,0,0,0,0,0,0,0,0, true, '{{}}', now())");

        // Act
        var rows = await new AdsReadRepository(db).GetBlendedDailyAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), default);

        // Assert
        rows.Should().NotBeNull();
        rows!.Should().ContainSingle();
        rows[0].Date.Should().Be(new DateOnly(2026, 9, 1));
        rows[0].RevenueWithoutVatCzk.Should().Be(1000m);
        rows[0].Orders.Should().Be(1);
    }

    [Fact]
    public async Task Missing_view_returns_null_instead_of_throwing()
    {
        var connectionString = await AdsPostgres.CreateMigratedAsync(_fixture, "ads_blend_missing", withShoptet: false);
        await using var db = new AdsDbContext(AdsPostgres.Options(connectionString));

        var rows = await new AdsReadRepository(db).GetBlendedDailyAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), default);

        rows.Should().BeNull();
    }
}
